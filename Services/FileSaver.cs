using Windows.Storage.Pickers;

namespace CloudManagerWinUIApp.Services;

/// <summary>Скачивание файлов с бекенда: выбор места сохранения + загрузка с прогрессом.</summary>
public static class FileSaver
{
    /// <summary>
    /// Спрашивает у пользователя путь сохранения и скачивает файл.
    /// Возвращает путь к сохранённому файлу или <c>null</c>, если пользователь отменил выбор.
    /// </summary>
    public static Task<string?> DownloadAsync(
        Guid fileId,
        string fileName,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
        => SaveWithPickerAsync(
            string.IsNullOrWhiteSpace(fileName) ? "file.bin" : fileName,
            destination => AppServices.Api.DownloadFileAsync(fileId, destination, progress, ct));

    /// <summary>
    /// Получает presigned-ссылку на видео, спрашивает у пользователя путь сохранения и скачивает видео.
    /// Возвращает путь к сохранённому файлу или <c>null</c>, если пользователь отменил выбор.
    /// </summary>
    public static async Task<string?> DownloadVideoAsync(
        Guid videoId,
        string videoName,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var playback = await AppServices.Api.GetVideoPlaybackAsync(videoId, ct);
        return await DownloadPlaybackMediaAsync(playback.Url, playback.ContentType, videoName, "video", ".mp4", progress, ct);
    }

    /// <summary>
    /// Получает presigned-ссылку на аудио, спрашивает у пользователя путь сохранения и скачивает аудио.
    /// Возвращает путь к сохранённому файлу или <c>null</c>, если пользователь отменил выбор.
    /// </summary>
    public static async Task<string?> DownloadAudioAsync(
        Guid audioId,
        string audioName,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var playback = await AppServices.Api.GetAudioPlaybackAsync(audioId, ct);
        return await DownloadPlaybackMediaAsync(playback.Url, playback.ContentType, audioName, "audio", ".mp3", progress, ct);
    }

    private static Task<string?> DownloadPlaybackMediaAsync(
        string url,
        string? contentType,
        string mediaName,
        string fallbackBaseName,
        string defaultExtension,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var suggestedName = MediaClassifier.EnsureExtension(mediaName, contentType, fallbackBaseName, defaultExtension);
        return SaveWithPickerAsync(
            suggestedName,
            destination => AppServices.Api.DownloadFromUrlAsync(url, destination, progress, ct));
    }

    private static async Task<string?> SaveWithPickerAsync(
        string suggestedFileName,
        Func<string, Task> downloadTo)
    {
        string? destination;
        try
        {
            destination = await PickDestinationAsync(suggestedFileName);
        }
        catch (Exception)
        {
            // Если системный диалог недоступен, сохраняем в папку «Загрузки».
            destination = Path.Combine(GetDownloadsFolder(), SanitizeFileName(suggestedFileName));
        }

        if (destination is null)
            return null;

        await downloadTo(destination);
        return destination;
    }

    private static async Task<string?> PickDestinationAsync(string suggestedFileName)
    {
        var extension = Path.GetExtension(suggestedFileName);
        if (string.IsNullOrEmpty(extension))
            extension = ".bin";

        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.Downloads,
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedFileName),
        };
        picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant(), [extension]);

        InitializeWithWindow(picker);

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>Привязывает системный диалог к главному окну (обязательно для десктопа).</summary>
    public static void InitializeWithWindow(object picker)
    {
        if (App.MainWindow is null)
            return;

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
    }

    private static string GetDownloadsFolder()
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        return Directory.Exists(downloads)
            ? downloads
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private static string SanitizeFileName(string fileName)
    {
        foreach (var invalid in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalid, '_');

        return fileName;
    }
}
