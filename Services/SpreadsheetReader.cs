using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using CloudManagerWinUIApp.Localization;

namespace CloudManagerWinUIApp.Services;

public sealed record WorkbookData(IReadOnlyList<WorksheetData> Sheets);

public sealed record WorksheetData(
    string Name,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    int ColumnCount,
    bool IsTruncated);

/// <summary>
/// Чтение таблиц <c>.xlsx</c>, <c>.xlsm</c>, <c>.xlsb</c>, <c>.xltx</c>, <c>.xltm</c> и <c>.xls</c>
/// без внешних библиотек.
/// </summary>
public static class SpreadsheetReader
{
    public const int MaxRowsPerSheet = 5000;
    private const int MaxColumnsPerSheet = 200;

    private static readonly byte[] Ole2Magic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    static SpreadsheetReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static bool IsSpreadsheetExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext is "xlsx" or "xlsm" or "xlsb" or "xltx" or "xltm" or "xls";
    }

    public static WorkbookData Read(byte[] data, string fileName)
    {
        if (data.Length == 0)
            return new WorkbookData([]);

        if (StartsWith(data, ZipMagic))
            return ReadZipWorkbook(data, fileName);

        if (StartsWith(data, Ole2Magic))
            return ReadOleXls(data);

        var header = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 512)).TrimStart();
        if (header.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            || header.StartsWith("<Workbook", StringComparison.OrdinalIgnoreCase))
        {
            return ReadSpreadsheetMl2003(Encoding.UTF8.GetString(data));
        }

        if (header.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || header.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase)
            || header.StartsWith("<table", StringComparison.OrdinalIgnoreCase))
        {
            return ReadHtmlTable(Encoding.UTF8.GetString(data));
        }

        if (LooksLikeDelimitedText(data))
            return ReadDelimitedText(DecodeTextWithBom(data));

        throw new InvalidDataException(Loc.Current.SpreadsheetUnsupportedFormat(fileName));
    }

    // --- ZIP-форматы (.xlsx / .xlsm / .xltx / .xltm / .xlsb) --------------

    private static WorkbookData ReadZipWorkbook(byte[] data, string fileName)
    {
        using var ms = new MemoryStream(data, writable: false);
        using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

        if (GetEntryIgnoreCase(archive, "xl/workbook.bin") is not null)
            return ReadXlsb(archive);

        if (GetEntryIgnoreCase(archive, "xl/workbook.xml") is not null
            || archive.Entries.Any(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)))
        {
            return ReadXlsx(archive);
        }

        throw new InvalidDataException(Loc.Current.SpreadsheetUnsupportedFormat(fileName));
    }

    private static WorkbookData ReadXlsx(ZipArchive archive)
    {
        var sharedStrings = ReadXlsxSharedStrings(archive);
        var dateStyleIndices = ReadXlsxDateStyleIndices(archive);
        var rels = ReadRelationships(archive, "xl/_rels/workbook.xml.rels");
        var sheetEntries = ResolveXlsxSheetEntries(archive, rels);

        var sheets = new List<WorksheetData>(sheetEntries.Count);
        foreach (var (name, entry) in sheetEntries)
            sheets.Add(ReadXlsxSheet(name, entry, sharedStrings, dateStyleIndices));

        return new WorkbookData(sheets);
    }

    private static List<string> ReadXlsxSharedStrings(ZipArchive archive)
    {
        var result = new List<string>();
        var entry = GetEntryIgnoreCase(archive, "xl/sharedStrings.xml");
        if (entry is null)
            return result;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        if (doc.Root is null)
            return result;

        foreach (var si in doc.Root.Elements().Where(e => e.Name.LocalName == "si"))
        {
            var sb = new StringBuilder();
            foreach (var t in si.Descendants().Where(e => e.Name.LocalName == "t"))
                sb.Append(t.Value);
            result.Add(sb.ToString());
        }

        return result;
    }

    private static HashSet<int> ReadXlsxDateStyleIndices(ZipArchive archive)
    {
        var dateXfs = new HashSet<int>();
        var entry = GetEntryIgnoreCase(archive, "xl/styles.xml");
        if (entry is null)
            return dateXfs;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        if (doc.Root is null)
            return dateXfs;

        var customDateFmts = new HashSet<int>();
        var numFmts = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "numFmts");
        if (numFmts is not null)
        {
            foreach (var fmt in numFmts.Elements().Where(e => e.Name.LocalName == "numFmt"))
            {
                if (int.TryParse((string?)fmt.Attribute("numFmtId"), out var id)
                    && IsDateFormatCode((string?)fmt.Attribute("formatCode")))
                {
                    customDateFmts.Add(id);
                }
            }
        }

        var cellXfs = doc.Root.Elements().FirstOrDefault(e => e.Name.LocalName == "cellXfs");
        if (cellXfs is null)
            return dateXfs;

        var idx = 0;
        foreach (var xf in cellXfs.Elements().Where(e => e.Name.LocalName == "xf"))
        {
            if (int.TryParse((string?)xf.Attribute("numFmtId"), out var fmtId)
                && (IsBuiltInDateFormat(fmtId) || customDateFmts.Contains(fmtId)))
            {
                dateXfs.Add(idx);
            }
            idx++;
        }

        return dateXfs;
    }

    private static List<(string Name, ZipArchiveEntry Entry)> ResolveXlsxSheetEntries(
        ZipArchive archive,
        Dictionary<string, string> rels)
    {
        var result = new List<(string, ZipArchiveEntry)>();
        var wbEntry = GetEntryIgnoreCase(archive, "xl/workbook.xml");
        if (wbEntry is not null)
        {
            using var stream = wbEntry.Open();
            var doc = XDocument.Load(stream);
            var sheetsEl = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "sheets");
            if (sheetsEl is not null)
            {
                foreach (var sheet in sheetsEl.Elements().Where(e => e.Name.LocalName == "sheet"))
                {
                    var name = (string?)sheet.Attribute("name") ?? $"Sheet{result.Count + 1}";
                    var rId = sheet.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                    if (rId is not null && rels.TryGetValue(rId, out var target))
                    {
                        var zipPath = NormalizeXlPath(target);
                        var entry = GetEntryIgnoreCase(archive, zipPath);
                        if (entry is not null)
                            result.Add((name, entry));
                    }
                }
            }
        }

        if (result.Count > 0)
            return result;

        foreach (var entry in archive.Entries
                     .Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                                 && e.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
            result.Add((Path.GetFileNameWithoutExtension(entry.Name), entry));
        }

        return result;
    }

    private static WorksheetData ReadXlsxSheet(
        string name,
        ZipArchiveEntry entry,
        IReadOnlyList<string> sharedStrings,
        HashSet<int> dateStyleIndices)
    {
        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        var builder = new SheetBuilder(name);
        var sheetData = doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "sheetData");
        if (sheetData is null)
            return builder.Build();

        var nextRow = 0;
        foreach (var rowEl in sheetData.Elements().Where(e => e.Name.LocalName == "row"))
        {
            var rowIndex = int.TryParse((string?)rowEl.Attribute("r"), out var rNum) && rNum > 0
                ? rNum - 1
                : nextRow;
            nextRow = rowIndex + 1;

            if (rowIndex >= MaxRowsPerSheet)
            {
                builder.MarkTruncated();
                break;
            }

            var nextCol = 0;
            foreach (var cellEl in rowEl.Elements().Where(e => e.Name.LocalName == "c"))
            {
                var colIndex = TryParseCellColumn((string?)cellEl.Attribute("r"), out var parsedCol)
                    ? parsedCol
                    : nextCol;
                nextCol = colIndex + 1;

                if (colIndex >= MaxColumnsPerSheet)
                    continue;

                var value = ExtractXlsxCellValue(cellEl, sharedStrings, dateStyleIndices);
                if (!string.IsNullOrEmpty(value))
                    builder.Set(rowIndex, colIndex, value);
            }
        }

        return builder.Build();
    }

    private static string ExtractXlsxCellValue(
        XElement cellEl,
        IReadOnlyList<string> sharedStrings,
        HashSet<int> dateStyleIndices)
    {
        var type = (string?)cellEl.Attribute("t");
        if (type == "inlineStr")
        {
            var inline = cellEl.Elements().FirstOrDefault(e => e.Name.LocalName == "is");
            if (inline is null)
                return string.Empty;

            var sb = new StringBuilder();
            foreach (var t in inline.Descendants().Where(e => e.Name.LocalName == "t"))
                sb.Append(t.Value);
            return sb.ToString();
        }

        var raw = cellEl.Elements().FirstOrDefault(e => e.Name.LocalName == "v")?.Value;
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        switch (type)
        {
            case "s":
                return int.TryParse(raw, out var sstIdx) && (uint)sstIdx < (uint)sharedStrings.Count
                    ? sharedStrings[sstIdx]
                    : string.Empty;
            case "b":
                return raw == "1" ? "TRUE" : "FALSE";
            case "str":
            case "e":
                return raw;
            default:
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                {
                    if (int.TryParse((string?)cellEl.Attribute("s"), out var styleIdx)
                        && dateStyleIndices.Contains(styleIdx)
                        && TryFormatExcelDate(num, out var dateText))
                    {
                        return dateText;
                    }
                    return FormatNumber(num);
                }
                return raw;
        }
    }

    // --- XLSB (BIFF12) ----------------------------------------------------

    private static WorkbookData ReadXlsb(ZipArchive archive)
    {
        var sharedStrings = ReadXlsbSharedStrings(archive);
        var rels = ReadRelationships(archive, "xl/_rels/workbook.bin.rels");
        var sheetEntries = ResolveXlsbSheetEntries(archive, rels);

        var sheets = new List<WorksheetData>(sheetEntries.Count);
        foreach (var (name, entry) in sheetEntries)
            sheets.Add(ReadXlsbSheet(name, entry, sharedStrings));

        return new WorkbookData(sheets);
    }

    private static List<string> ReadXlsbSharedStrings(ZipArchive archive)
    {
        var result = new List<string>();
        var entry = GetEntryIgnoreCase(archive, "xl/sharedStrings.bin");
        if (entry is null)
            return result;

        var bytes = ReadEntryBytes(entry);
        var pos = 0;
        while (TryReadBiff12Record(bytes, ref pos, out var id, out var payload))
        {
            // BrtSSTItem (19): [1 byte flags][XLWideString]
            if (id == 19 && payload.Length >= 5)
                result.Add(ReadBiff12String(payload, 1, out _));
        }

        return result;
    }

    private static List<(string Name, ZipArchiveEntry Entry)> ResolveXlsbSheetEntries(
        ZipArchive archive,
        Dictionary<string, string> rels)
    {
        var result = new List<(string, ZipArchiveEntry)>();
        var wbEntry = GetEntryIgnoreCase(archive, "xl/workbook.bin");
        if (wbEntry is not null)
        {
            var bytes = ReadEntryBytes(wbEntry);
            var pos = 0;
            while (TryReadBiff12Record(bytes, ref pos, out var id, out var payload))
            {
                // BrtBundleSh (156): [uint32 hsState][uint32 iTabID][XLWideString rId][XLWideString name]
                if (id == 156 && payload.Length >= 16)
                {
                    var offset = 8;
                    var rId = ReadBiff12String(payload, offset, out offset);
                    var name = ReadBiff12String(payload, offset, out _);
                    if (string.IsNullOrEmpty(name))
                        name = $"Sheet{result.Count + 1}";

                    if (!string.IsNullOrEmpty(rId) && rels.TryGetValue(rId, out var target))
                    {
                        var zipPath = NormalizeXlPath(target);
                        var entry = GetEntryIgnoreCase(archive, zipPath);
                        if (entry is not null)
                            result.Add((name, entry));
                    }
                }
            }
        }

        if (result.Count > 0)
            return result;

        foreach (var entry in archive.Entries
                     .Where(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.OrdinalIgnoreCase)
                                 && e.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
            result.Add((Path.GetFileNameWithoutExtension(entry.Name), entry));
        }

        return result;
    }

    private static WorksheetData ReadXlsbSheet(
        string name,
        ZipArchiveEntry entry,
        IReadOnlyList<string> sharedStrings)
    {
        var bytes = ReadEntryBytes(entry);
        var builder = new SheetBuilder(name);
        var pos = 0;
        var currentRow = -1;

        while (TryReadBiff12Record(bytes, ref pos, out var id, out var payload))
        {
            if (id == 0 && payload.Length >= 4) // BrtRowHdr
            {
                currentRow = (int)BitConverter.ToUInt32(payload[..4]);
                if (currentRow >= MaxRowsPerSheet)
                {
                    builder.MarkTruncated();
                    break;
                }
                continue;
            }

            if (currentRow < 0 || currentRow >= MaxRowsPerSheet || payload.Length < 8)
                continue;

            var col = (int)BitConverter.ToUInt32(payload[..4]);
            if (col < 0 || col >= MaxColumnsPerSheet)
                continue;

            string? text = id switch
            {
                2 when payload.Length >= 12 => FormatNumber(DecodeRk(BitConverter.ToUInt32(payload.Slice(8, 4)))), // BrtCellRk
                3 when payload.Length >= 9 => FormatExcelError(payload[8]), // BrtCellError
                4 when payload.Length >= 9 => payload[8] != 0 ? "TRUE" : "FALSE", // BrtCellBool
                5 when payload.Length >= 16 => FormatNumber(BitConverter.ToDouble(payload.Slice(8, 8))), // BrtCellReal
                6 when payload.Length >= 12 => ReadBiff12String(payload, 8, out _), // BrtCellSt
                7 when payload.Length >= 12 => ReadSstSafe(sharedStrings, BitConverter.ToUInt32(payload.Slice(8, 4))), // BrtCellIsst
                8 when payload.Length >= 12 => ReadBiff12String(payload, 8, out _), // BrtFmlaString
                9 when payload.Length >= 16 => FormatNumber(BitConverter.ToDouble(payload.Slice(8, 8))), // BrtFmlaNum
                10 when payload.Length >= 9 => payload[8] != 0 ? "TRUE" : "FALSE", // BrtFmlaBool
                11 when payload.Length >= 9 => FormatExcelError(payload[8]), // BrtFmlaError
                _ => null,
            };

            if (!string.IsNullOrEmpty(text))
                builder.Set(currentRow, col, text);
        }

        return builder.Build();
    }

    private static bool TryReadBiff12Record(byte[] data, ref int pos, out int id, out ReadOnlySpan<byte> payload)
    {
        id = 0;
        payload = default;
        if (pos >= data.Length)
            return false;

        if (!TryReadBiff12Int(data, ref pos, out id) || !TryReadBiff12Int(data, ref pos, out var len))
            return false;

        if (len < 0 || pos + len > data.Length)
            return false;

        payload = data.AsSpan(pos, len);
        pos += len;
        return true;
    }

    private static bool TryReadBiff12Int(byte[] data, ref int pos, out int value)
    {
        value = 0;
        var shift = 0;
        for (var i = 0; i < 4; i++)
        {
            if (pos >= data.Length)
                return false;

            var b = data[pos++];
            value |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return true;
            shift += 7;
        }
        return true;
    }

    private static string ReadBiff12String(ReadOnlySpan<byte> payload, int offset, out int nextOffset)
    {
        nextOffset = offset;
        if (offset < 0 || offset + 4 > payload.Length)
            return string.Empty;

        var charCount = BitConverter.ToInt32(payload.Slice(offset, 4));
        offset += 4;
        if (charCount <= 0 || offset + charCount * 2 > payload.Length)
        {
            nextOffset = Math.Min(payload.Length, offset);
            return string.Empty;
        }

        nextOffset = offset + charCount * 2;
        return Encoding.Unicode.GetString(payload.Slice(offset, charCount * 2));
    }

    // --- XLS (OLE2 BIFF8 / BIFF5) ----------------------------------------

    private static WorkbookData ReadOleXls(byte[] data)
    {
        var ole = Ole2CompoundFile.Parse(data);
        var stream = ole.ReadStream("Workbook") ?? ole.ReadStream("Book")
            ?? throw new InvalidDataException(Loc.Current.SpreadsheetUnsupportedFormat(".xls"));

        var records = ParseBiffRecords(stream);
        if (records.Any(r => r.Opcode == 0x002F)) // FILEPASS
            throw new InvalidDataException(Loc.Current.SpreadsheetPasswordProtected);

        var sharedStrings = new List<string>();
        var sheetDefs = new List<(string Name, int Offset)>();

        for (var i = 0; i < records.Count; i++)
        {
            var rec = records[i];
            if (rec.Opcode == 0x000A) // EOF workbook globals
                break;

            if (rec.Opcode == 0x0085 && rec.DataLength >= 8) // BOUNDSHEET
            {
                var recData = stream.AsSpan(rec.DataOffset, rec.DataLength);
                var sheetOffset = (int)BitConverter.ToUInt32(recData);
                var sheetType = recData[5];
                if (sheetType == 0x00) // Worksheet
                {
                    var name = ReadBiff8ShortString(recData, 6);
                    sheetDefs.Add((string.IsNullOrEmpty(name) ? $"Sheet{sheetDefs.Count + 1}" : name, sheetOffset));
                }
            }
            else if (rec.Opcode == 0x00FC) // SST
            {
                var sstChunks = new List<ArraySegment<byte>> { new(stream, rec.DataOffset, rec.DataLength) };
                while (i + 1 < records.Count && records[i + 1].Opcode == 0x003C)
                {
                    i++;
                    sstChunks.Add(new(stream, records[i].DataOffset, records[i].DataLength));
                }
                sharedStrings = ParseBiff8Sst(sstChunks);
            }
        }

        if (sheetDefs.Count == 0)
            return new WorkbookData([ParseBiffSheet("Sheet1", stream, records, 0, sharedStrings)]);

        var sheets = new List<WorksheetData>(sheetDefs.Count);
        foreach (var (name, offset) in sheetDefs)
        {
            var startIndex = records.FindIndex(r => r.Offset >= offset);
            if (startIndex >= 0)
                sheets.Add(ParseBiffSheet(name, stream, records, startIndex, sharedStrings));
        }

        return new WorkbookData(sheets);
    }

    private readonly record struct BiffRecord(int Offset, ushort Opcode, int DataOffset, int DataLength);

    private static List<BiffRecord> ParseBiffRecords(byte[] stream)
    {
        var list = new List<BiffRecord>();
        var pos = 0;

        while (pos + 4 <= stream.Length)
        {
            var opcode = BitConverter.ToUInt16(stream, pos);
            var len = BitConverter.ToUInt16(stream, pos + 2);
            var recOffset = pos;
            pos += 4;
            if (pos + len > stream.Length)
                break;

            list.Add(new BiffRecord(recOffset, opcode, pos, len));
            pos += len;
        }

        return list;
    }

    private static WorksheetData ParseBiffSheet(
        string name,
        byte[] stream,
        IReadOnlyList<BiffRecord> records,
        int startIndex,
        IReadOnlyList<string> sharedStrings)
    {
        var builder = new SheetBuilder(name);
        var cp1251 = Encoding.GetEncoding(1251, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
        var depth = 0;

        for (var i = startIndex; i < records.Count; i++)
        {
            var rec = records[i];
            if (rec.Opcode == 0x0809) // BOF
            {
                depth++;
                continue;
            }
            if (rec.Opcode == 0x000A) // EOF
            {
                depth--;
                if (depth <= 0)
                    break;
                continue;
            }
            if (depth > 1)
                continue;

            var recData = stream.AsSpan(rec.DataOffset, rec.DataLength);
            switch (rec.Opcode)
            {
                case 0x00FD when recData.Length >= 10: // LABELSST
                {
                    var row = BitConverter.ToUInt16(recData);
                    var col = BitConverter.ToUInt16(recData[2..]);
                    var sstIdx = BitConverter.ToUInt32(recData[6..]);
                    SetBiffCell(builder, row, col, ReadSstSafe(sharedStrings, sstIdx));
                    break;
                }
                case 0x0203 when recData.Length >= 14: // NUMBER
                {
                    var row = BitConverter.ToUInt16(recData);
                    var col = BitConverter.ToUInt16(recData[2..]);
                    var val = BitConverter.ToDouble(recData[6..]);
                    SetBiffCell(builder, row, col, FormatNumber(val));
                    break;
                }
                case 0x027E when recData.Length >= 10: // RK
                {
                    var row = BitConverter.ToUInt16(recData);
                    var col = BitConverter.ToUInt16(recData[2..]);
                    var rk = BitConverter.ToUInt32(recData[6..]);
                    SetBiffCell(builder, row, col, FormatNumber(DecodeRk(rk)));
                    break;
                }
                case 0x00BD when recData.Length >= 6: // MULRK
                {
                    var row = BitConverter.ToUInt16(recData);
                    var firstCol = BitConverter.ToUInt16(recData[2..]);
                    var lastCol = BitConverter.ToUInt16(recData[^2..]);
                    var count = Math.Min(lastCol - firstCol + 1, (recData.Length - 6) / 6);
                    for (var c = 0; c < count; c++)
                    {
                        var rk = BitConverter.ToUInt32(recData[(4 + c * 6 + 2)..]);
                        SetBiffCell(builder, row, firstCol + c, FormatNumber(DecodeRk(rk)));
                    }
                    break;
                }
                case 0x0204 when recData.Length >= 8: // LABEL (BIFF5/8)
                case 0x00D6 when recData.Length >= 8: // RSTRING
                {
                    var row = BitConverter.ToUInt16(recData);
                    var col = BitConverter.ToUInt16(recData[2..]);
                    var cch = BitConverter.ToUInt16(recData[6..]);
                    var text = recData.Length >= 9 && (recData[8] == 0 || recData[8] == 1) && 9 + cch <= recData.Length
                        ? ReadBiff8StringBody(recData, 8, cch)
                        : cp1251.GetString(recData.Slice(8, Math.Min(cch, recData.Length - 8)));
                    SetBiffCell(builder, row, col, text);
                    break;
                }
                case 0x0205 when recData.Length >= 8: // BOOLERR
                {
                    var row = BitConverter.ToUInt16(recData);
                    var col = BitConverter.ToUInt16(recData[2..]);
                    var value = recData[7] == 0
                        ? (recData[6] != 0 ? "TRUE" : "FALSE")
                        : FormatExcelError(recData[6]);
                    SetBiffCell(builder, row, col, value);
                    break;
                }
                case 0x0006 when recData.Length >= 20: // FORMULA
                {
                    var row = BitConverter.ToUInt16(recData);
                    var col = BitConverter.ToUInt16(recData[2..]);
                    var marker = BitConverter.ToUInt16(recData[12..]);
                    if (marker == 0xFFFF)
                    {
                        var kind = recData[6];
                        if (kind == 0 && i + 1 < records.Count && records[i + 1].Opcode == 0x0207) // STRING
                        {
                            i++;
                            var strData = stream.AsSpan(records[i].DataOffset, records[i].DataLength);
                            SetBiffCell(builder, row, col, ReadBiff8String16(strData, 0));
                        }
                        else if (kind == 1)
                        {
                            SetBiffCell(builder, row, col, recData[8] != 0 ? "TRUE" : "FALSE");
                        }
                        else if (kind == 2)
                        {
                            SetBiffCell(builder, row, col, FormatExcelError(recData[8]));
                        }
                    }
                    else
                    {
                        SetBiffCell(builder, row, col, FormatNumber(BitConverter.ToDouble(recData[6..])));
                    }
                    break;
                }
            }
        }

        return builder.Build();
    }

    private static void SetBiffCell(SheetBuilder builder, int row, int col, string value)
    {
        if (row >= MaxRowsPerSheet)
        {
            builder.MarkTruncated();
            return;
        }
        if (col < MaxColumnsPerSheet && !string.IsNullOrEmpty(value))
            builder.Set(row, col, value);
    }

    private static string ReadBiff8ShortString(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 2 > data.Length)
            return string.Empty;

        var cch = data[offset];
        return ReadBiff8StringBody(data, offset + 1, cch);
    }

    private static string ReadBiff8String16(ReadOnlySpan<byte> data, int offset)
    {
        if (offset + 3 > data.Length)
            return string.Empty;

        var cch = BitConverter.ToUInt16(data[offset..]);
        return ReadBiff8StringBody(data, offset + 2, cch);
    }

    private static string ReadBiff8StringBody(ReadOnlySpan<byte> data, int flagsOffset, int cch)
    {
        if (cch <= 0 || flagsOffset >= data.Length)
            return string.Empty;

        var isUnicode = (data[flagsOffset] & 0x01) != 0;
        var textOffset = flagsOffset + 1;
        if (isUnicode)
        {
            var bytes = Math.Min(cch * 2, data.Length - textOffset);
            bytes -= bytes % 2;
            return bytes > 0 ? Encoding.Unicode.GetString(data.Slice(textOffset, bytes)) : string.Empty;
        }

        var len = Math.Min(cch, data.Length - textOffset);
        return len > 0 ? Encoding.Latin1.GetString(data.Slice(textOffset, len)) : string.Empty;
    }

    private static List<string> ParseBiff8Sst(IReadOnlyList<ArraySegment<byte>> chunks)
    {
        var reader = new Biff8SstReader(chunks);
        if (!reader.TryReadUInt32(out _) || !reader.TryReadUInt32(out var uniqueCount))
            return [];

        var strings = new List<string>((int)Math.Min(uniqueCount, 1_000_000));
        for (uint i = 0; i < uniqueCount; i++)
        {
            if (!reader.TryReadString(out var s))
                break;
            strings.Add(s);
        }
        return strings;
    }

    private sealed class Biff8SstReader(IReadOnlyList<ArraySegment<byte>> chunks)
    {
        private int _chunkIndex;
        private int _offset;

        public bool TryReadUInt32(out uint value)
        {
            Span<byte> buf = stackalloc byte[4];
            if (!TryReadRawBytes(buf))
            {
                value = 0;
                return false;
            }
            value = BitConverter.ToUInt32(buf);
            return true;
        }

        public bool TryReadString(out string result)
        {
            result = string.Empty;
            Span<byte> hdr = stackalloc byte[3];
            if (!TryReadRawBytes(hdr))
                return false;

            var cch = BitConverter.ToUInt16(hdr[..2]);
            var flags = hdr[2];
            var isUnicode = (flags & 0x01) != 0;
            var hasPhonetic = (flags & 0x04) != 0;
            var hasRich = (flags & 0x08) != 0;

            ushort richRuns = 0;
            if (hasRich)
            {
                Span<byte> b = stackalloc byte[2];
                if (!TryReadRawBytes(b))
                    return false;
                richRuns = BitConverter.ToUInt16(b);
            }

            uint phoneticBytes = 0;
            if (hasPhonetic)
            {
                if (!TryReadUInt32(out phoneticBytes))
                    return false;
            }

            var sb = new StringBuilder(cch);
            int remainingChars = cch;
            while (remainingChars > 0)
            {
                if (!EnsureChunkAvailable())
                    break;

                var chunk = chunks[_chunkIndex];
                var availBytes = chunk.Count - _offset;
                var bytesPerChar = isUnicode ? 2 : 1;
                var takeChars = Math.Min(remainingChars, availBytes / bytesPerChar);

                if (takeChars > 0)
                {
                    var byteLen = takeChars * bytesPerChar;
                    var slice = chunk.AsSpan(_offset, byteLen);
                    sb.Append(isUnicode
                        ? Encoding.Unicode.GetString(slice)
                        : Encoding.Latin1.GetString(slice));
                    _offset += byteLen;
                    remainingChars -= takeChars;
                }

                if (remainingChars > 0 && _offset >= chunk.Count)
                {
                    _chunkIndex++;
                    _offset = 0;
                    if (_chunkIndex >= chunks.Count)
                        break;

                    // При переносе строки через CONTINUE первый байт — флаг кодировки остатка.
                    if (chunks[_chunkIndex].Count > 0)
                    {
                        isUnicode = (chunks[_chunkIndex][0] & 0x01) != 0;
                        _offset = 1;
                    }
                }
                else if (remainingChars > 0)
                {
                    break;
                }
            }

            SkipBytes(richRuns * 4 + (int)phoneticBytes);
            result = sb.ToString();
            return true;
        }

        private bool TryReadRawBytes(Span<byte> dest)
        {
            var written = 0;
            while (written < dest.Length)
            {
                if (!EnsureChunkAvailable())
                    return false;

                var chunk = chunks[_chunkIndex];
                var copy = Math.Min(dest.Length - written, chunk.Count - _offset);
                chunk.AsSpan(_offset, copy).CopyTo(dest[written..]);
                _offset += copy;
                written += copy;
            }
            return true;
        }

        private void SkipBytes(int count)
        {
            var left = count;
            while (left > 0 && EnsureChunkAvailable())
            {
                var step = Math.Min(left, chunks[_chunkIndex].Count - _offset);
                _offset += step;
                left -= step;
            }
        }

        private bool EnsureChunkAvailable()
        {
            while (_chunkIndex < chunks.Count && _offset >= chunks[_chunkIndex].Count)
            {
                _chunkIndex++;
                _offset = 0;
            }
            return _chunkIndex < chunks.Count;
        }
    }

    // --- SpreadsheetML 2003 XML и HTML-таблицы (.xls-экспорт) --------------

    private static WorkbookData ReadSpreadsheetMl2003(string xml)
    {
        var doc = XDocument.Parse(xml);
        if (doc.Root is null)
            return new WorkbookData([]);

        var sheets = new List<WorksheetData>();
        foreach (var ws in doc.Root.Elements().Where(e => e.Name.LocalName == "Worksheet"))
        {
            var name = ws.Attributes().FirstOrDefault(a => a.Name.LocalName == "Name")?.Value
                       ?? $"Sheet{sheets.Count + 1}";
            var builder = new SheetBuilder(name);
            var table = ws.Elements().FirstOrDefault(e => e.Name.LocalName == "Table");
            if (table is not null)
            {
                var rowIdx = 0;
                foreach (var row in table.Elements().Where(e => e.Name.LocalName == "Row"))
                {
                    if (int.TryParse(row.Attributes().FirstOrDefault(a => a.Name.LocalName == "Index")?.Value, out var explicitRow)
                        && explicitRow > 0)
                    {
                        rowIdx = explicitRow - 1;
                    }

                    if (rowIdx >= MaxRowsPerSheet)
                    {
                        builder.MarkTruncated();
                        break;
                    }

                    var colIdx = 0;
                    foreach (var cell in row.Elements().Where(e => e.Name.LocalName == "Cell"))
                    {
                        if (int.TryParse(cell.Attributes().FirstOrDefault(a => a.Name.LocalName == "Index")?.Value, out var explicitCol)
                            && explicitCol > 0)
                        {
                            colIdx = explicitCol - 1;
                        }

                        var dataText = cell.Elements().FirstOrDefault(e => e.Name.LocalName == "Data")?.Value;
                        if (colIdx < MaxColumnsPerSheet && !string.IsNullOrEmpty(dataText))
                            builder.Set(rowIdx, colIdx, dataText);

                        colIdx++;
                    }
                    rowIdx++;
                }
            }
            sheets.Add(builder.Build());
        }

        return new WorkbookData(sheets);
    }

    private static WorkbookData ReadHtmlTable(string html)
    {
        var builder = new SheetBuilder("Sheet1");
        var rowIdx = -1;
        var colIdx = -1;
        var inCell = false;
        var cellSb = new StringBuilder();

        for (var i = 0; i < html.Length; i++)
        {
            if (html[i] == '<')
            {
                var close = html.IndexOf('>', i + 1);
                if (close < 0)
                    break;

                var rawTag = html[(i + 1)..close].Trim();
                var space = rawTag.IndexOfAny([' ', '\t', '\r', '\n']);
                var tag = (space >= 0 ? rawTag[..space] : rawTag).ToLowerInvariant();

                if (tag == "tr")
                {
                    if (rowIdx + 1 >= MaxRowsPerSheet)
                    {
                        builder.MarkTruncated();
                        break;
                    }
                    rowIdx++;
                    colIdx = -1;
                }
                else if (tag is "td" or "th")
                {
                    if (rowIdx < 0)
                        rowIdx = 0;
                    colIdx++;
                    inCell = true;
                    cellSb.Clear();
                }
                else if (tag is "/td" or "/th" && inCell)
                {
                    inCell = false;
                    var text = System.Net.WebUtility.HtmlDecode(cellSb.ToString()).Trim();
                    if (colIdx >= 0 && colIdx < MaxColumnsPerSheet && !string.IsNullOrEmpty(text))
                        builder.Set(rowIdx, colIdx, text);
                }
                else if (tag is "br" or "br/" && inCell)
                {
                    cellSb.Append(' ');
                }

                i = close;
                continue;
            }

            if (inCell && html[i] is not '\r' and not '\n')
                cellSb.Append(html[i]);
        }

        return new WorkbookData([builder.Build()]);
    }

    // --- Общие помощники --------------------------------------------------

    private sealed class SheetBuilder(string name)
    {
        private readonly List<string[]?> _rows = [];
        private int _maxRow = -1;
        private int _maxCol = -1;
        private bool _truncated;

        public void Set(int row, int col, string value)
        {
            while (_rows.Count <= row)
                _rows.Add(null);

            var rowArr = _rows[row];
            if (rowArr is null)
            {
                rowArr = new string[Math.Max(col + 1, 8)];
                _rows[row] = rowArr;
            }
            else if (col >= rowArr.Length)
            {
                Array.Resize(ref rowArr, Math.Max(col + 1, rowArr.Length * 2));
                _rows[row] = rowArr;
            }

            rowArr[col] = value;
            if (row > _maxRow) _maxRow = row;
            if (col > _maxCol) _maxCol = col;
        }

        public void MarkTruncated() => _truncated = true;

        public WorksheetData Build()
        {
            if (_maxRow < 0 || _maxCol < 0)
                return new WorksheetData(name, [], 0, _truncated);

            var colCount = _maxCol + 1;
            var rows = new List<IReadOnlyList<string>>(_maxRow + 1);
            for (var r = 0; r <= _maxRow; r++)
            {
                var rowArray = new string[colCount];
                var src = _rows[r];
                if (src is null)
                {
                    Array.Fill(rowArray, string.Empty);
                }
                else
                {
                    var copyLen = Math.Min(src.Length, colCount);
                    for (var c = 0; c < copyLen; c++)
                        rowArray[c] = src[c] ?? string.Empty;
                    for (var c = copyLen; c < colCount; c++)
                        rowArray[c] = string.Empty;
                }
                rows.Add(rowArray);
            }

            return new WorksheetData(name, rows, colCount, _truncated);
        }
    }

    private static Dictionary<string, string> ReadRelationships(ZipArchive archive, string relsPath)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var entry = GetEntryIgnoreCase(archive, relsPath);
        if (entry is null)
            return map;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        if (doc.Root is null)
            return map;

        foreach (var rel in doc.Root.Elements().Where(e => e.Name.LocalName == "Relationship"))
        {
            var id = (string?)rel.Attribute("Id");
            var target = (string?)rel.Attribute("Target");
            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                map[id] = target;
        }
        return map;
    }

    private static string NormalizeXlPath(string target)
    {
        var clean = target.Replace('\\', '/').TrimStart('/');
        while (clean.StartsWith("./", StringComparison.Ordinal))
            clean = clean[2..];
        while (clean.StartsWith("../", StringComparison.Ordinal))
            clean = clean[3..];

        return clean.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
            ? clean
            : "xl/" + clean;
    }

    private static bool LooksLikeDelimitedText(byte[] data)
    {
        var sample = Math.Min(data.Length, 4096);
        for (var i = 0; i < sample; i++)
        {
            if (data[i] == 0)
                return false;
        }
        return true;
    }

    private static string DecodeTextWithBom(byte[] data)
    {
        using var ms = new MemoryStream(data, writable: false);
        using var reader = new StreamReader(ms, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static WorkbookData ReadDelimitedText(string text)
    {
        var builder = new SheetBuilder("Sheet1");
        var firstLine = text.Split('\n', 2)[0];
        var commaCount = firstLine.Count(c => c == ',');
        var semiCount = firstLine.Count(c => c == ';');
        var tabCount = firstLine.Count(c => c == '\t');
        var delimiter = tabCount >= semiCount && tabCount >= commaCount && tabCount > 0
            ? '\t'
            : semiCount > commaCount ? ';' : ',';

        var rowIdx = 0;
        var colIdx = 0;
        var inQuotes = false;
        var cell = new StringBuilder();

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(ch);
                }
                continue;
            }

            if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                if (colIdx < MaxColumnsPerSheet && cell.Length > 0)
                    builder.Set(rowIdx, colIdx, cell.ToString());
                cell.Clear();
                colIdx++;
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;

                if (colIdx < MaxColumnsPerSheet && cell.Length > 0)
                    builder.Set(rowIdx, colIdx, cell.ToString());
                cell.Clear();
                colIdx = 0;
                rowIdx++;
                if (rowIdx >= MaxRowsPerSheet)
                {
                    builder.MarkTruncated();
                    break;
                }
            }
            else
            {
                cell.Append(ch);
            }
        }

        if (rowIdx < MaxRowsPerSheet && colIdx < MaxColumnsPerSheet && cell.Length > 0)
            builder.Set(rowIdx, colIdx, cell.ToString());

        return new WorkbookData([builder.Build()]);
    }

    private static ZipArchiveEntry? GetEntryIgnoreCase(ZipArchive archive, string path)
        => archive.Entries.FirstOrDefault(e => string.Equals(e.FullName, path, StringComparison.OrdinalIgnoreCase));

    private static byte[] ReadEntryBytes(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private static bool TryParseCellColumn(string? reference, out int colIndex)
    {
        colIndex = 0;
        if (string.IsNullOrEmpty(reference))
            return false;

        var found = false;
        foreach (var ch in reference)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                colIndex = colIndex * 26 + (ch - 'A' + 1);
                found = true;
            }
            else if (ch is >= 'a' and <= 'z')
            {
                colIndex = colIndex * 26 + (ch - 'a' + 1);
                found = true;
            }
            else
            {
                break;
            }
        }

        if (!found)
            return false;

        colIndex--;
        return true;
    }

    private static double DecodeRk(uint rk)
    {
        double value;
        if ((rk & 0x02) != 0)
        {
            value = ((int)rk) >> 2;
        }
        else
        {
            var bits = (ulong)(rk & 0xFFFFFFFC) << 32;
            value = BitConverter.Int64BitsToDouble((long)bits);
        }

        return (rk & 0x01) != 0 ? value / 100.0 : value;
    }

    private static string FormatExcelError(byte code) => code switch
    {
        0x00 => "#NULL!",
        0x07 => "#DIV/0!",
        0x0F => "#VALUE!",
        0x17 => "#REF!",
        0x1D => "#NAME?",
        0x24 => "#NUM!",
        0x2A => "#N/A",
        0x2B => "#GETTING_DATA",
        _ => "#ERR",
    };

    private static string FormatNumber(double value)
        => value.ToString("G15", CultureInfo.CurrentCulture);

    private static bool IsBuiltInDateFormat(int fmtId)
        => fmtId is (>= 14 and <= 22) or (>= 45 and <= 47);

    private static bool IsDateFormatCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return false;

        // Исключаем литералы в кавычках и цветовые/локальные теги [Red] / [$-419].
        var inQuotes = false;
        var inBrackets = false;
        foreach (var ch in code)
        {
            if (ch == '"') { inQuotes = !inQuotes; continue; }
            if (inQuotes) continue;
            if (ch == '[') { inBrackets = true; continue; }
            if (ch == ']') { inBrackets = false; continue; }
            if (inBrackets) continue;

            if (ch is 'y' or 'Y' or 'd' or 'D')
                return true;
        }
        return false;
    }

    private static bool TryFormatExcelDate(double serial, out string formatted)
    {
        formatted = string.Empty;
        if (serial is < 1 or >= 2958466)
            return false;

        try
        {
            var dt = DateTime.FromOADate(serial);
            formatted = dt.TimeOfDay == TimeSpan.Zero
                ? dt.ToString("d", CultureInfo.CurrentCulture)
                : dt.ToString("g", CultureInfo.CurrentCulture);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string ReadSstSafe(IReadOnlyList<string> sst, uint index)
        => index < (uint)sst.Count ? sst[(int)index] : string.Empty;

    private static bool StartsWith(byte[] data, byte[] prefix)
        => data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);
}
