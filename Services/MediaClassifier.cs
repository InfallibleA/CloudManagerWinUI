namespace CloudManagerWinUIApp.Services;

/// <summary>Целевой раздел API для загрузки выбранного пользователем файла.</summary>
public enum MediaUploadKind
{
    File,
    Video,
    Audio,
}

/// <summary>
/// Чистая классификация расширений и MIME-типов для загрузки и сохранения медиафайлов.
/// </summary>
public static class MediaClassifier
{
    private static readonly string[] FallbackVideoExtensions = ["mp4", "m4v", "webm", "mov", "mkv", "avi"];
    private static readonly string[] FallbackAudioExtensions = ["mp3", "wav", "ogg", "aac", "flac", "m4a", "opus", "webm"];

    /// <summary>
    /// Определяет по расширению файла и справочникам сервера, куда его загружать:
    /// в видео, в аудио или в обычные файлы.
    /// </summary>
    public static MediaUploadKind ClassifyUpload(
        string fileName,
        IReadOnlyList<string> videoExtensions,
        IReadOnlyList<string> audioExtensions)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.');
        if (string.IsNullOrEmpty(ext))
            return MediaUploadKind.File;

        if (MatchesExtension(ext, videoExtensions, FallbackVideoExtensions))
            return MediaUploadKind.Video;

        if (MatchesExtension(ext, audioExtensions, FallbackAudioExtensions))
            return MediaUploadKind.Audio;

        return MediaUploadKind.File;
    }

    /// <summary>
    /// Определяет MIME-тип аудиофайла: сначала по справочнику сервера (<c>GET api/audios/types</c>),
    /// затем по стандартному сопоставлению расширений.
    /// </summary>
    public static string ResolveAudioContentType(string filePath, IReadOnlyList<AudioTypeDto>? audioTypes = null)
    {
        var ext = Path.GetExtension(filePath).TrimStart('.');
        if (!string.IsNullOrEmpty(ext) && audioTypes is not null)
        {
            var matched = audioTypes.FirstOrDefault(t =>
                string.Equals(t.Value.TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(t.ContentType));
            if (matched is not null)
                return matched.ContentType;
        }

        return ext.ToLowerInvariant() switch
        {
            "mp3" => "audio/mpeg",
            "wav" => "audio/wav",
            "ogg" => "audio/ogg",
            "aac" => "audio/aac",
            "flac" => "audio/flac",
            "m4a" => "audio/mp4",
            "opus" => "audio/opus",
            "webm" => "audio/webm",
            _ => "application/octet-stream",
        };
    }

    /// <summary>Подставляет расширение по <paramref name="contentType"/>, если у имени файла его ещё нет.</summary>
    public static string EnsureExtension(
        string fileName,
        string? contentType,
        string fallbackName,
        string defaultExtension)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? fallbackName : fileName;
        if (!string.IsNullOrEmpty(Path.GetExtension(name)))
            return name;

        var ext = contentType?.ToLowerInvariant() switch
        {
            "video/mp4" => ".mp4",
            "video/webm" => ".webm",
            "video/quicktime" => ".mov",
            "video/x-matroska" => ".mkv",
            "video/x-msvideo" => ".avi",
            "audio/mpeg" or "audio/mp3" => ".mp3",
            "audio/wav" or "audio/x-wav" => ".wav",
            "audio/ogg" => ".ogg",
            "audio/aac" => ".aac",
            "audio/flac" => ".flac",
            "audio/mp4" or "audio/x-m4a" => ".m4a",
            "audio/opus" => ".opus",
            "audio/webm" => ".webm",
            _ => defaultExtension,
        };
        return name + ext;
    }

    private static bool MatchesExtension(string ext, IReadOnlyList<string> serverList, string[] fallback)
    {
        var candidates = serverList.Count > 0 ? serverList : fallback;
        return candidates.Any(item => string.Equals(item.TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase));
    }
}
