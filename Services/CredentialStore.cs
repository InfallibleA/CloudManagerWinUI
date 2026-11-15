using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace CloudManagerWinUIApp.Services;

/// <summary>Сохранённая сессия: логин, пароль и текущий токен.</summary>
public sealed record StoredSession(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("password")] string Password,
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("user_id")] Guid UserId,
    [property: JsonPropertyName("role")] string Role);

/// <summary>
/// Хранит логин, пароль и токен между запусками приложения.
/// Файл шифруется средствами Windows DPAPI (<see cref="DataProtectionScope.CurrentUser"/>),
/// поэтому прочитать его может только тот же пользователь на той же машине.
/// </summary>
public sealed class CredentialStore
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CloudManagerWinUIApp",
        "session.dat");

    private readonly string _path;

    public CredentialStore(string? path = null) => _path = path ?? DefaultPath;

    public StoredSession? Load()
    {
        try
        {
            if (!File.Exists(_path))
                return null;

            var encrypted = File.ReadAllBytes(_path);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            return ApiJson.Deserialize<StoredSession>(Encoding.UTF8.GetString(plain));
        }
        catch (Exception)
        {
            // Файл повреждён или создан другим пользователем — просто начинаем заново.
            return null;
        }
    }

    public void Save(StoredSession session)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var plain = Encoding.UTF8.GetBytes(ApiJson.Serialize(session));
            var encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_path, encrypted);
        }
        catch (Exception)
        {
            // Не критично: сессия просто не переживёт перезапуск.
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception)
        {
            // ignore
        }
    }
}
