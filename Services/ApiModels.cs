using System.Text.Json.Serialization;

namespace CloudManagerWinUIApp.Services;

// --- Авторизация ---------------------------------------------------------

public sealed record RegisterRequest(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("password")] string Password);

public sealed record LoginRequest(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("password")] string Password);

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

public sealed record UserResponse(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("is_blocked")] bool IsBlocked = false);

// --- Файлы ---------------------------------------------------------------

public sealed record FileShortDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt = null,
    [property: JsonPropertyName("creator_id")] Guid? CreatorId = null,
    [property: JsonPropertyName("creator_login")] string? CreatorLogin = null);

public sealed record FileDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("object_key")] string? ObjectKey,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("creator_id")] Guid? CreatorId = null,
    [property: JsonPropertyName("creator_login")] string? CreatorLogin = null);

public sealed record FileTypeDto(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("content_type")] string ContentType,
    [property: JsonPropertyName("is_image")] bool IsImage);

public sealed record FileTextContent(string Text, bool IsTruncated);

// --- Видео ---------------------------------------------------------------

public sealed record VideoDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("object_key")] string? ObjectKey,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);

public sealed record VideoTypeDto(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("content_type")] string ContentType);

/// <summary>Видео в содержимом папки: без <c>object_key</c>, видео адресуется по id.</summary>
public sealed record VideoShortDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt = null);

public sealed record VideoPlaybackDto(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("content_type")] string ContentType,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("expires_in")] int ExpiresIn);

// --- Новости -------------------------------------------------------------

public sealed record NewsDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("weight")] int Weight,
    [property: JsonPropertyName("media_id")] Guid? MediaId);

public sealed record CreateNewsRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description = null,
    [property: JsonPropertyName("media_id")] Guid? MediaId = null);

// --- Папки ---------------------------------------------------------------

public sealed record FolderShortDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parent_id")] Guid? ParentId,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt = null);

public sealed record FolderDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parent_id")] Guid? ParentId,
    [property: JsonPropertyName("is_root")] bool IsRoot = false,
    [property: JsonPropertyName("files")] IReadOnlyList<FileShortDto>? Files = null,
    [property: JsonPropertyName("videos")] IReadOnlyList<VideoShortDto>? Videos = null,
    [property: JsonPropertyName("children")] IReadOnlyList<FolderShortDto>? Children = null,
    [property: JsonPropertyName("created_at")] DateTimeOffset? CreatedAt = null);

public sealed record CreateFolderRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("parent_id")] Guid? ParentId = null,
    [property: JsonPropertyName("file_ids")] IReadOnlyList<Guid>? FileIds = null);

public sealed record FileUpdateRequest(
    [property: JsonPropertyName("name")] string? Name);

public sealed record VideoUpdateRequest(
    [property: JsonPropertyName("name")] string? Name);

public sealed record FolderUpdateRequest(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("parent_id")] Guid? ParentId = null,
    [property: JsonPropertyName("file_ids")] IReadOnlyList<Guid>? FileIds = null);

// --- Пагинация -----------------------------------------------------------

public sealed record PagedResponse<T>(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("page_size")] int PageSize,
    [property: JsonPropertyName("data")] IReadOnlyList<T>? Data)
{
    public IReadOnlyList<T> Items => Data ?? [];
}
