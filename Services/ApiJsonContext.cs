using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudManagerWinUIApp.Services;

/// <summary>
/// Источник генерации JSON-сериализаторов. Нужен, чтобы Release-сборка с
/// <c>PublishTrimmed=true</c> не потеряла типы DTO при обрезке неиспользуемого кода.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(RegisterRequest))]
[JsonSerializable(typeof(LoginRequest))]
[JsonSerializable(typeof(TokenResponse))]
[JsonSerializable(typeof(UserResponse))]
[JsonSerializable(typeof(FileShortDto))]
[JsonSerializable(typeof(FileDto))]
[JsonSerializable(typeof(FileTypeDto[]))]
[JsonSerializable(typeof(VideoDto))]
[JsonSerializable(typeof(VideoTypeDto[]))]
[JsonSerializable(typeof(VideoPlaybackDto))]
[JsonSerializable(typeof(AudioDto))]
[JsonSerializable(typeof(AudioShortDto))]
[JsonSerializable(typeof(AudioTypeDto[]))]
[JsonSerializable(typeof(AudioPlaybackDto))]
[JsonSerializable(typeof(NewsDto))]
[JsonSerializable(typeof(CreateNewsRequest))]
[JsonSerializable(typeof(FolderShortDto))]
[JsonSerializable(typeof(FolderDto))]
[JsonSerializable(typeof(CreateFolderRequest))]
[JsonSerializable(typeof(FileUpdateRequest))]
[JsonSerializable(typeof(VideoUpdateRequest))]
[JsonSerializable(typeof(AudioUpdateRequest))]
[JsonSerializable(typeof(FolderUpdateRequest))]
[JsonSerializable(typeof(PagedResponse<NewsDto>))]
[JsonSerializable(typeof(PagedResponse<FileDto>))]
[JsonSerializable(typeof(PagedResponse<AudioDto>))]
[JsonSerializable(typeof(PagedResponse<FolderDto>))]
[JsonSerializable(typeof(PagedResponse<UserResponse>))]
[JsonSerializable(typeof(AppConfigFile))]
[JsonSerializable(typeof(UserSettings))]
[JsonSerializable(typeof(StoredSession))]
internal sealed partial class ApiJsonContext : JsonSerializerContext
{
}

/// <summary>Короткие помощники для работы с JSON бекенда (snake_case).</summary>
internal static class ApiJson
{
    public static string Serialize(object value)
        => JsonSerializer.Serialize(value, value.GetType(), ApiJsonContext.Default);

    public static T? Deserialize<T>(string json)
        => (T?)JsonSerializer.Deserialize(json, typeof(T), ApiJsonContext.Default);
}
