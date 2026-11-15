using System.Text.Json.Serialization;

namespace CloudManagerWinUIApp.Services;

/// <summary>
/// Пользовательские настройки (в отличие от appsettings.json рядом с приложением,
/// этот файл принадлежит пользователю и лежит в его профиле).
/// </summary>
public sealed class UserSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CloudManagerWinUIApp",
        "user.json");

    /// <summary>Код выбранного языка интерфейса: <c>ru</c> или <c>en</c>.</summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = ApiJson.Deserialize<UserSettings>(File.ReadAllText(SettingsPath));
                if (loaded is not null)
                    return loaded;
            }
        }
        catch (Exception)
        {
            // Повреждённый файл не должен мешать запуску приложения.
        }

        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, ApiJson.Serialize(this));
        }
        catch (Exception)
        {
            // Не критично: настройка просто не сохранится.
        }
    }
}
