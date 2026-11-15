using Windows.Storage.Pickers;

namespace CloudManagerWinUIApp.Services;

/// <summary>Скачивание файлов с бекенда: выбор места сохранения + загрузка с прогрессом.</summary>
public static class FileSaver
{
    /// <summary>
    /// Спрашивает у пользователя путь сохранения и скачивает файл.
    /// Возвращает путь к сохранённому файлу или <c>null</c>, если пользователь отменил выбор.
    /// </summary>
    public static async Task<string?> DownloadAsync(
        Guid fileId,
        string fileName,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "file.bin";

        string? destination;
        try
        {
            destination = await PickDestinationAsync(fileName);
        }
        catch (Exception)
        {
            // Если системный диалог недоступен, сохраняем в папку «Загрузки».
            destination = Path.Combine(GetDownloadsFolder(), SanitizeFileName(fileName));
        }

        if (destination is null)
            return null;

        await AppServices.Api.DownloadFileAsync(fileId, destination, progress, ct);
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
