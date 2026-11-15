namespace CloudManagerWinUIApp.Services;

/// <summary>Общие сервисы приложения (создаются один раз при запуске).</summary>
public static class AppServices
{
    public static ApiSettings Settings { get; private set; } = null!;

    public static CredentialStore Credentials { get; private set; } = null!;

    public static CloudManagerApiClient Api { get; private set; } = null!;

    public static AuthService Auth { get; private set; } = null!;

    public static bool IsInitialized => Api is not null;

    public static void Initialize()
    {
        if (IsInitialized)
            return;

        Settings = ApiSettings.Load();
        Credentials = new CredentialStore();
        Api = new CloudManagerApiClient(Settings.ToUri());
        Auth = new AuthService(Api, Credentials);
    }

    /// <summary>Прямая ссылка на изображение файла (доступна без токена).</summary>
    public static Uri ImageUri(Guid fileId) => new(Api.BaseAddress, $"api/files/{fileId}/image");

    /// <summary>Прямая ссылка на скачивание файла (доступна без токена).</summary>
    public static Uri DownloadUri(Guid fileId) => new(Api.BaseAddress, $"api/files/{fileId}/download");
}
