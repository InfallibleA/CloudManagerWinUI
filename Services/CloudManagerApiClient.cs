using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace CloudManagerWinUIApp.Services;

/// <summary>
/// HTTP-клиент CloudManager API. Все пути относительны базовому адресу
/// (например <c>http://localhost</c>). Токен подставляется автоматически;
/// при 401 клиент один раз пытается молча переавторизоваться через
/// <see cref="TokenRefresher"/>.
/// </summary>
public sealed class CloudManagerApiClient
{
    private static readonly TimeSpan DefaultJsonTimeout = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;

    public CloudManagerApiClient(Uri baseAddress)
    {
        // Дефолтные 100 секунд рвут загрузку/скачивание больших файлов,
        // поэтому ограничиваем время через CancellationToken.
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        BaseAddress = baseAddress;
    }

    /// <summary>Текущий access-токен (или <c>null</c>, если пользователь не авторизован).</summary>
    public string? Token { get; set; }

    /// <summary>
    /// Вызывается при получении 401. Возвращает <c>true</c>, если удалось получить
    /// свежий токен — тогда исходный запрос повторяется один раз.
    /// </summary>
    public Func<CancellationToken, Task<bool>>? TokenRefresher { get; set; }

    /// <summary>Событие «сессия потеряна»: повторная авторизация не удалась.</summary>
    public event EventHandler? SessionLost;

    /// <summary>
    /// Пользователь заблокирован администратором: сервер ответил <c>403</c> с признаком
    /// <see cref="ApiException.IsBlockedUser"/>. Событие поднимается <b>до</b> исключения,
    /// чтобы сессию успели забыть (<see cref="AuthService"/>), а вызывающий код получил
    /// обычное исключение и просто показал ошибку.
    /// </summary>
    public event EventHandler<ApiException>? UserBlocked;

    public Uri BaseAddress
    {
        get => _http.BaseAddress!;
        set
        {
            var text = value.OriginalString;
            _http.BaseAddress = text.EndsWith('/') ? value : new Uri(text + "/");
        }
    }

    // --- Авторизация ------------------------------------------------------

    public Task<UserResponse> RegisterAsync(string login, string password, CancellationToken ct = default)
        => SendJsonAsync<UserResponse>(
            () => JsonRequest(HttpMethod.Post, "api/auth/register", new RegisterRequest(login, password)),
            allowRefresh: false,
            ct);

    public Task<TokenResponse> LoginAsync(string login, string password, CancellationToken ct = default)
        => SendJsonAsync<TokenResponse>(
            () => JsonRequest(HttpMethod.Post, "api/auth/login", new LoginRequest(login, password)),
            allowRefresh: false,
            ct);

    public Task<UserResponse> GetMeAsync(CancellationToken ct = default)
        => SendJsonAsync<UserResponse>(() => CreateRequest(HttpMethod.Get, "api/auth/me"), true, ct);

    /// <summary>
    /// Список пользователей (<c>GET api/auth/users</c>, только admin).
    /// <paramref name="login"/> — подстрока без учёта регистра, <paramref name="role"/> — <c>admin</c> или <c>client</c>.
    /// </summary>
    public Task<PagedResponse<UserResponse>> GetUsersAsync(
        int page = 1,
        int pageSize = 25,
        string? login = null,
        string? role = null,
        CancellationToken ct = default)
    {
        var query = new StringBuilder($"api/auth/users?page={page}&page_size={pageSize}");
        AppendQuery(query, "login", login);
        AppendQuery(query, "role", role);

        return SendJsonAsync<PagedResponse<UserResponse>>(() => CreateRequest(HttpMethod.Get, query.ToString()), true, ct);
    }

    /// <summary>
    /// Блокировка пользователя (<c>POST api/auth/users/{id}/block</c>, только admin).
    /// Тело не нужно; ответ <c>200</c> — карточка пользователя с новым <c>is_blocked</c>.
    /// Повторная блокировка идемпотентна и тоже возвращает <c>200</c>.
    /// </summary>
    public Task<UserResponse> BlockUserAsync(Guid userId, CancellationToken ct = default)
        => SendJsonAsync<UserResponse>(
            () => CreateRequest(HttpMethod.Post, $"api/auth/users/{userId}/block"),
            allowRefresh: true,
            ct);

    /// <summary>Разблокировка пользователя (<c>POST api/auth/users/{id}/unblock</c>), ответ тот же.</summary>
    public Task<UserResponse> UnblockUserAsync(Guid userId, CancellationToken ct = default)
        => SendJsonAsync<UserResponse>(
            () => CreateRequest(HttpMethod.Post, $"api/auth/users/{userId}/unblock"),
            allowRefresh: true,
            ct);

    // --- Новости ----------------------------------------------------------

    public Task<PagedResponse<NewsDto>> GetNewsAsync(
        int page = 1,
        int pageSize = 25,
        string? name = null,
        int? weightFrom = null,
        int? weightTo = null,
        CancellationToken ct = default)
    {
        var query = new StringBuilder($"api/news?page={page}&page_size={pageSize}");
        AppendQuery(query, "name", name);
        if (weightFrom is not null) query.Append("&weight_from=").Append(weightFrom.Value);
        if (weightTo is not null) query.Append("&weight_to=").Append(weightTo.Value);

        return SendJsonAsync<PagedResponse<NewsDto>>(() => CreateRequest(HttpMethod.Get, query.ToString()), true, ct);
    }

    public Task<NewsDto> GetNewsByIdAsync(Guid newsId, CancellationToken ct = default)
        => SendJsonAsync<NewsDto>(() => CreateRequest(HttpMethod.Get, $"api/news/{newsId}"), true, ct);

    /// <summary>Создание новости (<c>POST api/news</c>, только admin). Weight назначает сервер.</summary>
    public Task<NewsDto> CreateNewsAsync(
        string name,
        string? description,
        Guid? mediaId = null,
        CancellationToken ct = default)
        => SendJsonAsync<NewsDto>(
            () => JsonRequest(HttpMethod.Post, "api/news", new CreateNewsRequest(name, description, mediaId)),
            true,
            ct);

    // --- Файлы ------------------------------------------------------------

    public Task<FileTypeDto[]> GetFileTypesAsync(CancellationToken ct = default)
        => SendJsonAsync<FileTypeDto[]>(() => CreateRequest(HttpMethod.Get, "api/files/types"), false, ct);

    /// <summary>
    /// Список файлов (<c>GET api/files</c>). <paramref name="types"/> уходит повторяющимся
    /// параметром <c>type</c>, <paramref name="creatorId"/> — фильтр по владельцу
    /// (<c>creator_id</c>); без него админ видит файлы всех пользователей.
    /// </summary>
    public Task<PagedResponse<FileDto>> GetFilesAsync(
        int page = 1,
        int pageSize = 25,
        string? name = null,
        IEnumerable<string>? types = null,
        Guid? creatorId = null,
        CancellationToken ct = default)
    {
        var query = new StringBuilder($"api/files?page={page}&page_size={pageSize}");
        AppendQuery(query, "name", name);
        foreach (var type in types ?? [])
            AppendQuery(query, "type", type);
        if (creatorId is not null) query.Append("&creator_id=").Append(creatorId.Value);

        return SendJsonAsync<PagedResponse<FileDto>>(() => CreateRequest(HttpMethod.Get, query.ToString()), true, ct);
    }

    public Task<FileDto> UploadFileAsync(
        string name,
        string filePath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
        => UploadAsync<FileDto>("api/files", name, filePath, progress, ct);

    public async Task DownloadFileAsync(
        Guid fileId,
        string destinationPath,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromHours(2));

        using var response = await SendWithRetryAsync(
            () => CreateRequest(HttpMethod.Get, $"api/files/{fileId}/download"),
            allowRefresh: true,
            cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            throw CreateApiError(response.StatusCode, error);
        }

        var total = response.Content.Headers.ContentLength ?? -1;
        await using var source = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        await using var target = File.Create(destinationPath);

        var buffer = new byte[81920];
        long transferred = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cts.Token).ConfigureAwait(false)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cts.Token).ConfigureAwait(false);
            transferred += read;
            if (total > 0)
                progress?.Report(Math.Clamp((double)transferred / total, 0, 1));
        }
    }

    public Task DeleteFileAsync(Guid fileId, CancellationToken ct = default)
        => SendNoContentAsync(() => CreateRequest(HttpMethod.Delete, $"api/files/{fileId}"), true, ct);

    // --- Папки ------------------------------------------------------------

    public Task<PagedResponse<FolderDto>> GetFoldersAsync(
        int page = 1,
        int pageSize = 50,
        string? name = null,
        Guid? parentId = null,
        bool rootOnly = false,
        Guid? creatorId = null,
        CancellationToken ct = default)
    {
        var query = new StringBuilder($"api/folders?page={page}&page_size={pageSize}");
        AppendQuery(query, "name", name);
        if (parentId is not null) query.Append("&parent_id=").Append(parentId.Value);
        if (rootOnly) query.Append("&root_only=true");

        // Админский фильтр: с creator_id админ получает папки выбранного пользователя,
        // остальным сервер возвращает только их собственные папки.
        if (creatorId is not null) query.Append("&creator_id=").Append(creatorId.Value);

        return SendJsonAsync<PagedResponse<FolderDto>>(() => CreateRequest(HttpMethod.Get, query.ToString()), true, ct);
    }

    public Task<FolderDto> GetFolderAsync(Guid folderId, CancellationToken ct = default)
        => SendJsonAsync<FolderDto>(() => CreateRequest(HttpMethod.Get, $"api/folders/{folderId}"), true, ct);

    public Task<FolderDto> CreateFolderAsync(string name, Guid? parentId = null, CancellationToken ct = default)
        => SendJsonAsync<FolderDto>(
            () => JsonRequest(HttpMethod.Post, "api/folders", new CreateFolderRequest(name, parentId)),
            true,
            ct);

    public Task<FolderDto> UpdateFolderAsync(
        Guid folderId,
        string? name = null,
        Guid? parentId = null,
        IReadOnlyList<Guid>? fileIds = null,
        CancellationToken ct = default)
        => SendJsonAsync<FolderDto>(
            () => JsonRequest(HttpMethod.Patch, $"api/folders/{folderId}", new FolderUpdateRequest(name, parentId, fileIds)),
            true,
            ct);

    public Task DeleteFolderAsync(Guid folderId, CancellationToken ct = default)
        => SendNoContentAsync(() => CreateRequest(HttpMethod.Delete, $"api/folders/{folderId}"), true, ct);

    /// <summary>Переименование файла (доступно владельцу файла и админу).</summary>
    public Task<FileDto> UpdateFileAsync(Guid fileId, string name, CancellationToken ct = default)
        => SendJsonAsync<FileDto>(
            () => JsonRequest(HttpMethod.Patch, $"api/files/{fileId}", new FileUpdateRequest(name)),
            true,
            ct);

    // --- Видео ------------------------------------------------------------

    /// <summary>
    /// Справочник разрешённых типов видео (<c>GET api/videos/types</c>) — для фильтра
    /// в диалоге выбора файла. Авторизация не требуется.
    /// </summary>
    public Task<VideoTypeDto[]> GetVideoTypesAsync(CancellationToken ct = default)
        => SendJsonAsync<VideoTypeDto[]>(() => CreateRequest(HttpMethod.Get, "api/videos/types"), false, ct);

    /// <summary>Загрузка видео (<c>POST api/videos</c>, только admin).</summary>
    public Task<VideoDto> UploadVideoAsync(
        string name,
        string filePath,
        Guid? folderId = null,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
        => UploadAsync<VideoDto>("api/videos", name, filePath, progress, ct, folderId);

    /// <summary>
    /// Presigned-ссылка на воспроизведение (<c>GET api/videos/{id}/playback</c>, без авторизации).
    /// Ссылка ведёт в S3, поэтому поток отдаётся без проксирования через API.
    /// </summary>
    public Task<VideoPlaybackDto> GetVideoPlaybackAsync(Guid videoId, CancellationToken ct = default)
        => SendJsonAsync<VideoPlaybackDto>(
            () => CreateRequest(HttpMethod.Get, $"api/videos/{videoId}/playback"),
            false,
            ct);

    /// <summary>
    /// Загрузка медиафайла (<c>multipart/form-data</c>: <c>name</c> + <c>file</c>, у видео ещё
    /// <c>folder_id</c>). Файлы и видео отличаются только путём и типом ответа.
    /// </summary>
    private async Task<T> UploadAsync<T>(
        string path,
        string name,
        string filePath,
        IProgress<double>? progress,
        CancellationToken ct,
        Guid? folderId = null)
    {
        Func<HttpRequestMessage> factory = () =>
        {
            var content = new MultipartFormDataContent();
            content.Add(new StringContent(name, Encoding.UTF8), "name");

            if (folderId is not null)
                content.Add(new StringContent(folderId.Value.ToString(), Encoding.UTF8), "folder_id");

            var stream = File.OpenRead(filePath);
            var streamContent = progress is null
                ? new StreamContent(stream)
                : new StreamContent(new ProgressStream(stream, stream.Length, progress));
            streamContent.Headers.ContentType = new MediaTypeHeaderValue(GetContentType(filePath));
            content.Add(MultipartFilePart.Wrap(streamContent, Path.GetFileName(filePath)));

            var request = CreateRequest(HttpMethod.Post, path);
            request.Content = content;
            return request;
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromHours(2));

        using var response = await SendWithRetryAsync(factory, allowRefresh: true, cts.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw CreateApiError(response.StatusCode, body);

        return ApiJson.Deserialize<T>(body)
               ?? throw CreateApiError(response.StatusCode, body);
    }

    // --- Низкий уровень ---------------------------------------------------

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(Token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return request;
    }

    private HttpRequestMessage JsonRequest(HttpMethod method, string path, object payload)
    {
        var request = CreateRequest(method, path);
        request.Content = new StringContent(
            ApiJson.Serialize(payload),
            Encoding.UTF8,
            "application/json");
        return request;
    }

    private static void AppendQuery(StringBuilder query, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            query.Append('&').Append(key).Append('=').Append(Uri.EscapeDataString(value));
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<HttpRequestMessage> factory,
        bool allowRefresh,
        CancellationToken ct)
    {
        var response = await _http.SendAsync(factory(), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        var refreshed = false;
        if (allowRefresh && TokenRefresher is not null)
            refreshed = await TokenRefresher(ct).ConfigureAwait(false);

        if (!refreshed)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var status = response.StatusCode;
            response.Dispose();
            SessionLost?.Invoke(this, EventArgs.Empty);
            throw CreateApiError(status, body);
        }

        response.Dispose();
        var retried = await _http.SendAsync(factory(), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        // Свежий токен тоже не принят — сессия действительно потеряна.
        if (retried.StatusCode == HttpStatusCode.Unauthorized)
        {
            var body = await retried.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            retried.Dispose();
            SessionLost?.Invoke(this, EventArgs.Empty);
            throw CreateApiError(HttpStatusCode.Unauthorized, body);
        }

        return retried;
    }

    private async Task<T> SendJsonAsync<T>(
        Func<HttpRequestMessage> factory,
        bool allowRefresh,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultJsonTimeout);

        using var response = await SendWithRetryAsync(factory, allowRefresh, cts.Token).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
            throw CreateApiError(response.StatusCode, body);

        if (string.IsNullOrWhiteSpace(body))
            return default!;

        return ApiJson.Deserialize<T>(body)
               ?? throw CreateApiError(response.StatusCode, body);
    }

    private async Task SendNoContentAsync(Func<HttpRequestMessage> factory, bool allowRefresh, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(DefaultJsonTimeout);

        using var response = await SendWithRetryAsync(factory, allowRefresh, cts.Token).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
        throw CreateApiError(response.StatusCode, body);
    }

    /// <summary>
    /// Ошибка по ответу сервера. Если сервер сообщил о блокировке пользователя, событие
    /// <see cref="UserBlocked"/> поднимается здесь — до того, как исключение получит
    /// вызывающий код, иначе сессию было бы уже некому забыть.
    /// </summary>
    private ApiException CreateApiError(HttpStatusCode status, string body)
    {
        var error = ApiException.From(status, body);
        if (error.IsBlockedUser)
            UserBlocked?.Invoke(this, error);

        return error;
    }

    private static string GetContentType(string filePath) => Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        ".jpeg" or ".jpg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".avif" => "image/avif",
        ".mp4" or ".m4v" => "video/mp4",
        ".webm" => "video/webm",
        ".mov" => "video/quicktime",
        ".mkv" => "video/x-matroska",
        ".avi" => "video/x-msvideo",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".xlsm" => "application/vnd.ms-excel.sheet.macroEnabled.12",
        _ => "application/octet-stream",
    };
}
