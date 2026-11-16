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
        var ole = Ole2CompoundFile.Parse(data);
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

    private static bool StartsWith(byte[] data, byte[] prefix)
        => data.Length >= prefix.Length && data.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static ushort ReadUInt16(byte[] data, int offset)
        => BitConverter.ToUInt16(data, offset);

    private static uint ReadUInt32(byte[] data, int offset)
        => BitConverter.ToUInt32(data, offset);
}
