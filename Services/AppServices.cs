using System.Net;

namespace CloudManagerWinUIApp.Services;

/// <summary>Общие сервисы приложения (создаются один раз при запуске).</summary>
public static class AppServices
{
    internal static readonly SocketsHttpHandler SharedHttpHandler = new()
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
    };

    private sealed class TypeCacheSlot<T>
    {
        private readonly Lock _lock = new();
        private Task<IReadOnlyList<T>>? _task;

        public Task<IReadOnlyList<T>> GetOrFetchAsync(Func<Task<T[]>> fetch)
        {
            lock (_lock)
            {
                return _task ??= FetchAsync(fetch);
            }
        }

        private async Task<IReadOnlyList<T>> FetchAsync(Func<Task<T[]>> fetch)
        {
            try
            {
                var items = await fetch().ConfigureAwait(false);
                if (items is { Length: > 0 })
                    return items;
            }
            catch (Exception)
            {
                // Повторная попытка при следующем обращении.
            }

            lock (_lock)
            {
                _task = null;
            }

            return [];
        }
    }

    private static readonly TypeCacheSlot<FileTypeDto> FileTypesSlot = new();
    private static readonly TypeCacheSlot<VideoTypeDto> VideoTypesSlot = new();
    private static readonly TypeCacheSlot<AudioTypeDto> AudioTypesSlot = new();

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
        Api = new CloudManagerApiClient(Settings.ToUri(), SharedHttpHandler);
        Auth = new AuthService(Api, Credentials);
    }

    public static Task<IReadOnlyList<FileTypeDto>> GetFileTypesCachedAsync()
        => FileTypesSlot.GetOrFetchAsync(() => Api.GetFileTypesAsync());

    public static Task<IReadOnlyList<VideoTypeDto>> GetVideoTypesCachedAsync()
        => VideoTypesSlot.GetOrFetchAsync(() => Api.GetVideoTypesAsync());

    public static Task<IReadOnlyList<AudioTypeDto>> GetAudioTypesCachedAsync()
        => AudioTypesSlot.GetOrFetchAsync(() => Api.GetAudioTypesAsync());

    /// <summary>Прямая ссылка на изображение файла (доступна без токена).</summary>
    public static Uri ImageUri(Guid fileId) => new(Api.BaseAddress, $"api/files/{fileId}/image");

    /// <summary>Прямая ссылка на скачивание файла (доступна без токена).</summary>
    public static Uri DownloadUri(Guid fileId) => new(Api.BaseAddress, $"api/files/{fileId}/download");
}
