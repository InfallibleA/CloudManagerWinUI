using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudManagerWinUIApp.Services;

/// <summary>Откуда взят адрес API.</summary>
public enum ApiSettingsSourceKind
{
    Default,
    Environment,
    ConfigFile,
}

/// <summary>Проблема с конфигурацией адреса API (текст для пользователя собирает UI).</summary>
public enum ApiSettingsWarningKind
{
    None,
    InvalidEnvironmentValue,
    ConfigUnreadable,
    ConfigInvalidJson,
    ConfigMissingKey,
    ConfigInvalidAddress,
}

/// <summary>
/// Настройки подключения к бекенду. Читаются один раз при запуске, в порядке приоритета:
/// <list type="number">
///   <item>переменная окружения <c>CLOUDMANAGER_API_URL</c>;</item>
///   <item>файл <c>appsettings.json</c> рядом с исполняемым файлом:
///     <code>{ "Api": { "BaseUrl": "http://192.168.1.10:8080" } }</code>;</item>
///   <item>значение по умолчанию <see cref="DefaultBaseUrl"/>.</item>
/// </list>
/// </summary>
public sealed class ApiSettings
{
    public const string DefaultBaseUrl = "http://localhost";

    public const string EnvironmentVariableName = "CLOUDMANAGER_API_URL";

    public const string ConfigFileName = "appsettings.json";

    /// <summary>Базовый адрес API (всегда с завершающим «/»).</summary>
    public string BaseUrl { get; private init; } = DefaultBaseUrl;

    /// <summary>Откуда взято значение.</summary>
    public ApiSettingsSourceKind SourceKind { get; private init; }

    /// <summary>Путь к файлу настроек или имя переменной окружения.</summary>
    public string? SourceDetail { get; private init; }

    /// <summary>Вид проблемы в конфигурации.</summary>
    public ApiSettingsWarningKind WarningKind { get; private init; }

    /// <summary>Подробность проблемы: путь к файлу или некорректное значение.</summary>
    public string? WarningDetail { get; private init; }

    /// <summary>Пути, в которых ищется файл настроек (сначала рядом с .exe, потом рядом со сборкой).</summary>
    public static IReadOnlyList<string> ConfigFileCandidates()
    {
        var directories = new List<string>();

        var processDirectory = Path.GetDirectoryName(Environment.ProcessPath);
        if (!string.IsNullOrEmpty(processDirectory))
            directories.Add(processDirectory!);

        if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
            directories.Add(AppContext.BaseDirectory);

        return directories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(directory => Path.Combine(directory, ConfigFileName))
            .ToList();
    }

    public static ApiSettings Load()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariableName);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            if (TryNormalize(fromEnvironment, out var environmentUrl))
            {
                return new ApiSettings
                {
                    BaseUrl = environmentUrl,
                    SourceKind = ApiSettingsSourceKind.Environment,
                    SourceDetail = EnvironmentVariableName,
                };
            }

            return new ApiSettings
            {
                WarningKind = ApiSettingsWarningKind.InvalidEnvironmentValue,
                WarningDetail = fromEnvironment,
            };
        }

        foreach (var path in ConfigFileCandidates())
        {
            if (!File.Exists(path))
                continue;

            string? configured;
            try
            {
                configured = ApiJson.Deserialize<AppConfigFile>(File.ReadAllText(path))?.Api?.BaseUrl;
            }
            catch (JsonException)
            {
                return new ApiSettings
                {
                    WarningKind = ApiSettingsWarningKind.ConfigInvalidJson,
                    WarningDetail = path,
                };
            }
            catch (Exception)
            {
                return new ApiSettings
                {
                    WarningKind = ApiSettingsWarningKind.ConfigUnreadable,
                    WarningDetail = path,
                };
            }

            if (string.IsNullOrWhiteSpace(configured))
            {
                return new ApiSettings
                {
                    WarningKind = ApiSettingsWarningKind.ConfigMissingKey,
                    WarningDetail = path,
                };
            }

            if (!TryNormalize(configured, out var fileUrl))
            {
                return new ApiSettings
                {
                    WarningKind = ApiSettingsWarningKind.ConfigInvalidAddress,
                    WarningDetail = configured,
                };
            }

            return new ApiSettings
            {
                BaseUrl = fileUrl,
                SourceKind = ApiSettingsSourceKind.ConfigFile,
                SourceDetail = path,
            };
        }

        return new ApiSettings();
    }

    /// <summary>Базовый адрес в виде <see cref="Uri"/> для HTTP-клиента.</summary>
    public Uri ToUri() => new(TryNormalize(BaseUrl, out var url) ? url : DefaultBaseUrl);

    private static bool TryNormalize(string? text, out string normalized)
    {
        normalized = DefaultBaseUrl;

        if (string.IsNullOrWhiteSpace(text))
            return false;

        var candidate = text.Trim();
        if (!candidate.Contains("://", StringComparison.Ordinal))
            candidate = "http://" + candidate;

        if (!Uri.TryCreate(candidate.TrimEnd('/') + "/", UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme is not ("http" or "https"))
            return false;

        normalized = parsed.OriginalString;
        return true;
    }
}

/// <summary>Содержимое <c>appsettings.json</c>.</summary>
internal sealed class AppConfigFile
{
    [JsonPropertyName("Api")]
    public AppConfigApi? Api { get; set; }
}

/// <summary>Секция <c>Api</c> файла настроек.</summary>
internal sealed class AppConfigApi
{
    [JsonPropertyName("BaseUrl")]
    public string? BaseUrl { get; set; }
}
