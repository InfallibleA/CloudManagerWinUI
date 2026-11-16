using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using CloudManagerWinUIApp.Localization;

namespace CloudManagerWinUIApp.Services;

/// <summary>
/// Извлекает читаемый текст из документов <c>.docx</c> и <c>.doc</c> (включая OLE2 MS-DOC,
/// RTF и переименованные OOXML/HTML-файлы) без внешних зависимостей.
/// </summary>
public static class DocumentTextExtractor
{
    private static readonly byte[] Ole2Magic = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    static DocumentTextExtractor()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static bool IsDocumentExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext is "doc" or "docx" or "rtf";
    }

    public static string ExtractText(byte[] data, string fileName)
    {
        if (data.Length == 0)
            return string.Empty;

        if (StartsWith(data, ZipMagic))
            return ExtractDocx(data);

        if (StartsWith(data, Ole2Magic))
            return ExtractOleDoc(data);

        var header = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 256)).TrimStart();
        if (header.StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase))
            return ExtractRtf(data);

        if (header.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
            || header.StartsWith("<w:wordDocument", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractWordXml(Encoding.UTF8.GetString(data));
        }

        if (header.StartsWith("<html", StringComparison.OrdinalIgnoreCase)
            || header.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase))
        {
            return ExtractHtml(Encoding.UTF8.GetString(data));
        }

        throw new InvalidDataException(Loc.Current.DocumentUnsupportedFormat(fileName));
    }

    // --- DOCX (OOXML) -----------------------------------------------------

    private static string ExtractDocx(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var documentEntry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidDataException(Loc.Current.DocxMissingBody);

        using var entryStream = documentEntry.Open();
        var doc = XDocument.Load(entryStream);
        return ExtractWordXml(doc);
    }

    private static string ExtractWordXml(string xml)
        => ExtractWordXml(XDocument.Parse(xml));

    private static string ExtractWordXml(XDocument doc)
    {
        var sb = new StringBuilder();
        var root = doc.Root;
        if (root is null)
            return string.Empty;

        var body = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "body") ?? root;
        AppendWordXmlElement(body, sb);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static void AppendWordXmlElement(XElement element, StringBuilder sb)
    {
        switch (element.Name.LocalName)
        {
            case "p":
                var startLen = sb.Length;
                var isListItem = element.Elements().Any(e => e.Name.LocalName == "pPr")
                    && element.Descendants().Any(e => e.Name.LocalName == "numPr");
                if (isListItem)
                    sb.Append("• ");

                foreach (var child in element.Elements())
                    AppendWordXmlInline(child, sb);

                if (sb.Length > startLen || sb.Length > 0)
                    sb.AppendLine();
                return;

            case "tbl":
                foreach (var row in element.Elements().Where(e => e.Name.LocalName == "tr"))
                {
                    var cells = row.Elements().Where(e => e.Name.LocalName == "tc").ToList();
                    for (var i = 0; i < cells.Count; i++)
                    {
                        if (i > 0)
                            sb.Append('\t');

                        var cellSb = new StringBuilder();
                        foreach (var child in cells[i].Elements())
                            AppendWordXmlElement(child, cellSb);

                        sb.Append(cellSb.ToString().TrimEnd('\r', '\n').Replace("\r\n", " ").Replace('\n', ' '));
                    }
                    sb.AppendLine();
                }
                return;

            default:
                foreach (var child in element.Elements())
                    AppendWordXmlElement(child, sb);
                return;
        }
    }

    private static void AppendWordXmlInline(XElement element, StringBuilder sb)
    {
        switch (element.Name.LocalName)
        {
            case "t" or "delText" or "instrText" when element.Name.LocalName == "t":
                sb.Append(element.Value);
                return;
            case "tab":
                sb.Append('\t');
                return;
            case "br" or "cr":
                sb.AppendLine();
                return;
            case "pPr" or "rPr" or "instrText" or "delText":
                return;
            default:
                foreach (var child in element.Elements())
                    AppendWordXmlInline(child, sb);
                return;
        }
    }

    // --- DOC (OLE2 Compound Document + MS-DOC) ----------------------------

    private static string ExtractOleDoc(byte[] data)
    {
        var ole = Ole2Reader.Parse(data);
        var wordDoc = ole.ReadStream("WordDocument")
            ?? throw new InvalidDataException(Loc.Current.DocMissingWordStream);

        if (wordDoc.Length < 32)
            throw new InvalidDataException("Invalid WordDocument header.");

        var wIdent = ReadUInt16(wordDoc, 0x00);
        if (wIdent != 0xA5EC && wIdent != 0xA5DC)
            throw new InvalidDataException("Unrecognized Word binary signature.");

        var nFib = ReadUInt16(wordDoc, 0x02);
        var flags = ReadUInt16(wordDoc, 0x0A);

        if ((flags & 0x0100) != 0)
            throw new InvalidDataException(Loc.Current.DocPasswordProtected);

        // Старые версии Word 6.0 / 95 (nFib < 0x00C1) хранят текст единым куском [fcMin..fcMac].
        if (nFib < 0x00C1)
            return ExtractLegacyWordText(wordDoc);

        var tableName = (flags & 0x0200) != 0 ? "1Table" : "0Table";
        var tableStream = ole.ReadStream(tableName)
            ?? throw new InvalidDataException($"Missing {tableName} stream in .doc file.");

        if (wordDoc.Length < 0x01AA)
            return ExtractLegacyWordText(wordDoc);

        var ccpText = ReadUInt32(wordDoc, 0x004C);
        var fcClx = ReadUInt32(wordDoc, 0x01A2);
        var lcbClx = ReadUInt32(wordDoc, 0x01A6);

        if (lcbClx == 0 || fcClx + lcbClx > (ulong)tableStream.Length)
            return ExtractLegacyWordText(wordDoc);

        var rawText = ReadPieceTableText(wordDoc, tableStream, (int)fcClx, (int)lcbClx, ccpText);
        return CleanMsDocControlChars(rawText);
    }

    private static string ExtractLegacyWordText(byte[] wordDoc)
    {
        var fcMin = (int)ReadUInt32(wordDoc, 0x18);
        var fcMac = (int)ReadUInt32(wordDoc, 0x1C);
        if (fcMin < 0 || fcMac <= fcMin || fcMac > wordDoc.Length)
            return string.Empty;

        var raw = Encoding.GetEncoding(1251, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
        var text = raw.GetString(wordDoc, fcMin, fcMac - fcMin);
        return CleanMsDocControlChars(text);
    }

    private static string ReadPieceTableText(
        byte[] wordDoc,
        byte[] tableStream,
        int fcClx,
        int lcbClx,
        uint ccpText)
    {
        var pos = fcClx;
        var end = fcClx + lcbClx;

        // Пропускаем необязательные Prc-блоки (clxt == 0x01).
        while (pos < end && tableStream[pos] == 0x01)
        {
            if (pos + 3 > end)
                return string.Empty;

            var cbGrpprl = ReadUInt16(tableStream, pos + 1);
            pos += 3 + cbGrpprl;
        }

        if (pos + 5 > end || tableStream[pos] != 0x02)
            return string.Empty;

        var lcbPcdt = (int)ReadUInt32(tableStream, pos + 1);
        pos += 5;

        if (lcbPcdt < 4 || pos + lcbPcdt > end)
            return string.Empty;

        var pieceCount = (lcbPcdt - 4) / 12;
        if (pieceCount <= 0)
            return string.Empty;

        var pcdStart = pos + (pieceCount + 1) * 4;
        var sb = new StringBuilder();
        var cpLimit = ccpText > 0 ? ccpText : uint.MaxValue;
        var cp1252 = Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);

        for (var i = 0; i < pieceCount; i++)
        {
            var cpStart = ReadUInt32(tableStream, pos + i * 4);
            var cpEnd = ReadUInt32(tableStream, pos + (i + 1) * 4);
            if (cpStart >= cpLimit)
                break;

            var effectiveEnd = Math.Min(cpEnd, cpLimit);
            if (effectiveEnd <= cpStart)
                continue;

            var charCount = (int)(effectiveEnd - cpStart);
            var fcRaw = ReadUInt32(tableStream, pcdStart + i * 8 + 2);
            var isCompressed = (fcRaw & 0x40000000) != 0;
            var offset = (int)(fcRaw & 0x3FFFFFFF);

            if (isCompressed)
            {
                var byteOffset = offset / 2;
                if (byteOffset < 0 || byteOffset >= wordDoc.Length)
                    continue;

                var available = Math.Min(charCount, wordDoc.Length - byteOffset);
                sb.Append(cp1252.GetString(wordDoc, byteOffset, available));
            }
            else
            {
                if (offset < 0 || offset >= wordDoc.Length)
                    continue;

                var availableBytes = Math.Min(charCount * 2, wordDoc.Length - offset);
                availableBytes -= availableBytes % 2;
                sb.Append(Encoding.Unicode.GetString(wordDoc, offset, availableBytes));
            }
        }

        return sb.ToString();
    }

    private static string CleanMsDocControlChars(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        var fieldDepth = 0;
        var inFieldResult = false;
        var inCell = false;

        foreach (var ch in raw)
        {
            if (ch == '\x13') // Начало поля (HYPERLINK, PAGE и т. п.)
            {
                fieldDepth++;
                inFieldResult = false;
                continue;
            }
            if (ch == '\x14') // Разделитель инструкции поля и отображаемого текста
            {
                inFieldResult = true;
                continue;
            }
            if (ch == '\x15') // Конец поля
            {
                if (fieldDepth > 0)
                    fieldDepth--;
                inFieldResult = false;
                continue;
            }
            if (fieldDepth > 0 && !inFieldResult)
                continue;

            switch (ch)
            {
                case '\r':
                case '\x0B':
                    inCell = false;
                    sb.AppendLine();
                    break;
                case '\x0C':
                    inCell = false;
                    sb.AppendLine();
                    sb.AppendLine();
                    break;
                case '\x07':
                    if (!inCell)
                    {
                        inCell = true;
                        sb.Append('\t');
                    }
                    else
                    {
                        inCell = false;
                        sb.AppendLine();
                    }
                    break;
                case '\t':
                    sb.Append('\t');
                    break;
                case '\x01' or '\x08':
                    break; // Встроенные якоря картинок/фигур
                default:
                    if (!char.IsControl(ch))
                        sb.Append(ch);
                    break;
            }
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    // --- RTF / HTML запасные декодеры -------------------------------------

    private static string ExtractRtf(byte[] data)
    {
        var src = Encoding.ASCII.GetString(data);
        var sb = new StringBuilder();
        var skipDepth = -1;
        var depth = 0;
        var i = 0;

        while (i < src.Length)
        {
            var ch = src[i];
            if (ch == '{')
            {
                depth++;
                i++;
                continue;
            }
            if (ch == '}')
            {
                if (skipDepth == depth)
                    skipDepth = -1;
                depth = Math.Max(0, depth - 1);
                i++;
                continue;
            }
            if (ch == '\\')
            {
                i++;
                if (i >= src.Length)
                    break;

                var esc = src[i];
                if (esc is '\\' or '{' or '}')
                {
                    if (skipDepth < 0)
                        sb.Append(esc);
                    i++;
                    continue;
                }
                if (esc == '\'')
                {
                    if (i + 2 < src.Length
                        && byte.TryParse(src.AsSpan(i + 1, 2), System.Globalization.NumberStyles.HexNumber, null, out var b))
                    {
                        if (skipDepth < 0)
                            sb.Append(Encoding.GetEncoding(1251, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback).GetString([b]));
                        i += 3;
                        continue;
                    }
                }
                if (esc == '*')
                {
                    if (skipDepth < 0)
                        skipDepth = depth;
                    i++;
                    continue;
                }

                var wordStart = i;
                while (i < src.Length && char.IsAsciiLetter(src[i]))
                    i++;
                var word = src[wordStart..i];

                var numStart = i;
                if (i < src.Length && src[i] == '-')
                    i++;
                while (i < src.Length && char.IsAsciiDigit(src[i]))
                    i++;
                var hasParam = i > numStart;
                var param = hasParam && int.TryParse(src.AsSpan(numStart, i - numStart), out var p) ? p : 0;

                if (i < src.Length && src[i] == ' ')
                    i++;

                if (skipDepth < 0)
                {
                    if (word is "fonttbl" or "colortbl" or "stylesheet" or "info" or "pict" or "object" or "header" or "footer")
                    {
                        skipDepth = depth;
                    }
                    else if (word is "par" or "line")
                    {
                        sb.AppendLine();
                    }
                    else if (word == "tab")
                    {
                        sb.Append('\t');
                    }
                    else if (word == "u" && hasParam)
                    {
                        var code = param < 0 ? param + 65536 : param;
                        sb.Append((char)code);
                        if (i < src.Length && src[i] == '?')
                            i++;
                    }
                }
                continue;
            }

            if (skipDepth < 0 && ch is not '\r' and not '\n')
                sb.Append(ch);
            i++;
        }

        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static string ExtractHtml(string html)
    {
        var sb = new StringBuilder(html.Length);
        var inTag = false;
        var tagBuilder = new StringBuilder();

        foreach (var ch in html)
        {
            if (ch == '<')
            {
                inTag = true;
                tagBuilder.Clear();
                continue;
            }
            if (ch == '>' && inTag)
            {
                inTag = false;
                var tag = tagBuilder.ToString().Trim().ToLowerInvariant();
                if (tag.StartsWith("br") || tag.StartsWith("/p") || tag.StartsWith("/div") || tag.StartsWith("/tr") || tag.StartsWith("li"))
                    sb.AppendLine();
                continue;
            }
            if (inTag)
            {
                tagBuilder.Append(ch);
                continue;
            }
            if (ch is not '\r' and not '\n')
                sb.Append(ch);
        }

        return System.Net.WebUtility.HtmlDecode(sb.ToString()).Trim();
    }

    // --- Минимальный read-only парсер OLE2 Compound File (CFBF) -----------

    private sealed class Ole2Reader
    {
        private const uint EndOfChain = 0xFFFFFFFE;
        private const uint FreeSect = 0xFFFFFFFF;

        private readonly byte[] _data;
        private readonly int _sectorSize;
        private readonly int _miniSectorSize;
        private readonly uint _miniCutoff;
        private readonly uint[] _fat;
        private readonly uint[] _miniFat;
        private readonly byte[] _miniStream;
        private readonly Dictionary<string, (uint StartSector, ulong Size)> _streams;

        private Ole2Reader(
            byte[] data,
            int sectorSize,
            int miniSectorSize,
            uint miniCutoff,
            uint[] fat,
            uint[] miniFat,
            byte[] miniStream,
            Dictionary<string, (uint, ulong)> streams)
        {
            _data = data;
            _sectorSize = sectorSize;
            _miniSectorSize = miniSectorSize;
            _miniCutoff = miniCutoff;
            _fat = fat;
            _miniFat = miniFat;
            _miniStream = miniStream;
            _streams = streams;
        }

        public static Ole2Reader Parse(byte[] data)
        {
            if (data.Length < 512)
                throw new InvalidDataException("Invalid OLE2 header.");

            var majorVersion = ReadUInt16(data, 0x1A);
            var sectorShift = ReadUInt16(data, 0x1E);
            var miniSectorShift = ReadUInt16(data, 0x20);
            var sectorSize = 1 << sectorShift;
            var miniSectorSize = 1 << miniSectorShift;
            var numFatSectors = (int)ReadUInt32(data, 0x2C);
            var firstDirSector = ReadUInt32(data, 0x30);
            var miniCutoff = ReadUInt32(data, 0x38);
            var firstMiniFatSector = ReadUInt32(data, 0x3C);
            var firstDifatSector = ReadUInt32(data, 0x44);

            var fatSectorIds = new List<uint>(numFatSectors);
            for (var i = 0; i < 109 && fatSectorIds.Count < numFatSectors; i++)
            {
                var sid = ReadUInt32(data, 0x4C + i * 4);
                if (sid != FreeSect)
                    fatSectorIds.Add(sid);
            }

            var difatSector = firstDifatSector;
            var difatGuard = 0;
            while (difatSector < EndOfChain && fatSectorIds.Count < numFatSectors && difatGuard++ < 1024)
            {
                var off = SectorOffset(difatSector, sectorSize);
                if (off < 0 || off + sectorSize > data.Length)
                    break;

                var entriesPerSector = sectorSize / 4 - 1;
                for (var i = 0; i < entriesPerSector && fatSectorIds.Count < numFatSectors; i++)
                {
                    var sid = ReadUInt32(data, off + i * 4);
                    if (sid != FreeSect)
                        fatSectorIds.Add(sid);
                }

                difatSector = ReadUInt32(data, off + entriesPerSector * 4);
            }

            var fat = new uint[fatSectorIds.Count * (sectorSize / 4)];
            for (var s = 0; s < fatSectorIds.Count; s++)
            {
                var off = SectorOffset(fatSectorIds[s], sectorSize);
                if (off < 0 || off + sectorSize > data.Length)
                    continue;

                var count = sectorSize / 4;
                for (var i = 0; i < count; i++)
                    fat[s * count + i] = ReadUInt32(data, off + i * 4);
            }

            var dirBytes = ReadChain(data, fat, firstDirSector, sectorSize);
            var streams = new Dictionary<string, (uint StartSector, ulong Size)>(StringComparer.OrdinalIgnoreCase);
            uint rootStart = EndOfChain;
            ulong rootSize = 0;

            for (var pos = 0; pos + 128 <= dirBytes.Length; pos += 128)
            {
                var objType = dirBytes[pos + 0x42];
                var nameBytes = ReadUInt16(dirBytes, pos + 0x40);
                if (objType == 0 || nameBytes < 2 || nameBytes > 64)
                    continue;

                var name = Encoding.Unicode.GetString(dirBytes, pos, nameBytes - 2);
                var startSector = ReadUInt32(dirBytes, pos + 0x74);
                var size = majorVersion == 3
                    ? ReadUInt32(dirBytes, pos + 0x78)
                    : ReadUInt64(dirBytes, pos + 0x78);

                if (objType == 5)
                {
                    rootStart = startSector;
                    rootSize = size;
                }
                else if (objType == 2)
                {
                    streams[name] = (startSector, size);
                }
            }

            var miniFatBytes = firstMiniFatSector < EndOfChain
                ? ReadChain(data, fat, firstMiniFatSector, sectorSize)
                : [];
            var miniFat = new uint[miniFatBytes.Length / 4];
            for (var i = 0; i < miniFat.Length; i++)
                miniFat[i] = ReadUInt32(miniFatBytes, i * 4);

            var miniStream = rootStart < EndOfChain
                ? ReadChain(data, fat, rootStart, sectorSize, rootSize)
                : [];

            return new Ole2Reader(data, sectorSize, miniSectorSize, miniCutoff, fat, miniFat, miniStream, streams);
        }

        public byte[]? ReadStream(string name)
        {
            if (!_streams.TryGetValue(name, out var entry))
                return null;

            return entry.Size < _miniCutoff
                ? ReadMiniChain(_miniStream, _miniFat, entry.StartSector, _miniSectorSize, entry.Size)
                : ReadChain(_data, _fat, entry.StartSector, _sectorSize, entry.Size);
        }

        private static int SectorOffset(uint sectorId, int sectorSize)
            => checked((int)((sectorId + 1UL) * (ulong)sectorSize));

        private static byte[] ReadChain(byte[] data, uint[] fat, uint startSector, int sectorSize, ulong? maxLength = null)
        {
            using var ms = new MemoryStream();
            var visited = new HashSet<uint>();
            var sector = startSector;

            while (sector < EndOfChain && visited.Add(sector))
            {
                var off = SectorOffset(sector, sectorSize);
                if (off < 0 || off >= data.Length)
                    break;

                var len = Math.Min(sectorSize, data.Length - off);
                ms.Write(data, off, len);

                if (maxLength is not null && (ulong)ms.Length >= maxLength.Value)
                    break;

                sector = sector < (uint)fat.Length ? fat[sector] : EndOfChain;
            }

            var bytes = ms.ToArray();
            if (maxLength is not null && (ulong)bytes.Length > maxLength.Value)
                Array.Resize(ref bytes, (int)maxLength.Value);

            return bytes;
        }

        private static byte[] ReadMiniChain(byte[] miniStream, uint[] miniFat, uint startSector, int miniSectorSize, ulong maxLength)
        {
            using var ms = new MemoryStream();
            var visited = new HashSet<uint>();
            var sector = startSector;

            while (sector < EndOfChain && visited.Add(sector))
            {
                var off = checked((int)(sector * (ulong)miniSectorSize));
                if (off < 0 || off >= miniStream.Length)
                    break;

                var len = Math.Min(miniSectorSize, miniStream.Length - off);
                ms.Write(miniStream, off, len);

                if ((ulong)ms.Length >= maxLength)
                    break;

                sector = sector < (uint)miniFat.Length ? miniFat[sector] : EndOfChain;
            }

            var bytes = ms.ToArray();
            if ((ulong)bytes.Length > maxLength)
                Array.Resize(ref bytes, (int)maxLength);

            return bytes;
        }
    }

    private static bool StartsWith(byte[] data, byte[] prefix)
        => data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static ushort ReadUInt16(byte[] data, int offset)
        => BitConverter.ToUInt16(data, offset);

    private static uint ReadUInt32(byte[] data, int offset)
        => BitConverter.ToUInt32(data, offset);

    private static ulong ReadUInt64(byte[] data, int offset)
        => BitConverter.ToUInt64(data, offset);
}
