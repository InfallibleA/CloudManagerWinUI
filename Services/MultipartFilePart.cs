namespace CloudManagerWinUIApp.Services;

/// <summary>
/// Часть <c>multipart/form-data</c> с содержимым файла.
/// <para>
/// .NET кодирует не-ASCII имя файла в заголовке как RFC 2047
/// (<c>filename="=?utf-8?B?…?="</c>), а бэкенд разбирает часть по RFC 7578: параметр
/// <c>filename*</c> он игнорирует, а тип файла определяет по расширению из
/// <c>filename</c>. Из-за этого «заметка.txt» приезжал без расширения и загрузка
/// падала с «Недопустимый тип файла». Поэтому имя передаётся двумя параметрами:
/// ASCII-запасной <c>filename</c> (расширение сохраняется) и <c>filename*</c>
/// (RFC 5987) с настоящим именем.
/// </para>
/// </summary>
internal static class MultipartFilePart
{
    /// <summary>Имя поля формы, которое ждёт бэкенд.</summary>
    public const string FieldName = "file";

    /// <summary>
    /// Оборачивает содержимое файла в часть формы с готовым <c>Content-Disposition</c>.
    /// Заголовок собирается вручную: <see cref="MultipartFormDataContent.Add(HttpContent, string, string)"/>
    /// портит не-ASCII имена.
    /// </summary>
    public static HttpContent Wrap(HttpContent fileContent, string fileName)
    {
        fileContent.Headers.TryAddWithoutValidation(
            "Content-Disposition",
            $"form-data; name=\"{FieldName}\"; filename=\"{AsciiFallback(fileName)}\"; filename*=UTF-8''{Uri.EscapeDataString(fileName)}");
        return fileContent;
    }

    /// <summary>
    /// Имя только из ASCII: расширение остаётся как есть, остальные символы
    /// заменяются на <c>_</c> (кавычка и обратная косая черта сломали бы заголовок).
    /// </summary>
    private static string AsciiFallback(string fileName)
    {
        var chars = fileName
            .Select(ch => ch > ' ' && ch < '\x7F' && ch is not ('"' or '\\') ? ch : '_')
            .ToArray();

        return new string(chars);
    }
}
