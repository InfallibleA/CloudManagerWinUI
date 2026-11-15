using System.Globalization;
using System.Runtime.InteropServices;

using Windows.Globalization;

namespace CloudManagerWinUIApp.Localization;

/// <summary>
/// Текущий язык интерфейса. Язык выбирается один раз при запуске
/// (сохранённый выбор пользователя, иначе язык системы, иначе английский)
/// и может быть изменён на странице «Настройки».
/// </summary>
public static class Loc
{
    public const string RussianCode = "ru";
    public const string EnglishCode = "en";

    [DllImport("kernel32.dll")]
    private static extern ushort GetUserDefaultUILanguage();

    /// <summary>Код текущего языка: <c>ru</c> или <c>en</c>.</summary>
    public static string Language { get; private set; } = EnglishCode;

    /// <summary>Строки текущего языка.</summary>
    public static Strings Current { get; private set; } = new(russian: false);

    public static void Initialize(string? savedLanguage)
    {
        if (!string.IsNullOrWhiteSpace(savedLanguage) && TryNormalize(savedLanguage, out var saved))
        {
            Set(saved);
            return;
        }

        Set(DetectSystemLanguage() ?? EnglishCode);
    }

    public static void Set(string language)
    {
        Language = TryNormalize(language, out var normalized) ? normalized : EnglishCode;
        Current = new Strings(russian: Language == RussianCode);
    }

    /// <summary>Первый поддерживаемый язык системы (ru или en) или <c>null</c>.</summary>
    public static string? DetectSystemLanguage()
    {
        // 1. Язык интерфейса Windows — самый точный признак ожиданий пользователя.
        if (TryNormalize(GetWindowsUiLanguageTag(), out var ui))
            return ui;

        // 2. Список языков из параметров Windows: берём первый поддерживаемый.
        foreach (var tag in ApplicationLanguages.Languages)
        {
            if (TryNormalize(tag, out var preferred))
                return preferred;
        }

        // 3. Язык текущей культуры процесса.
        return TryNormalize(CultureInfo.CurrentUICulture.Name, out var culture) ? culture : null;
    }

    /// <summary>Тег языка системы (например, <c>ru-RU</c>) или <c>null</c>.</summary>
    public static string? DetectSystemLanguageTag()
    {
        var uiLanguage = GetWindowsUiLanguageTag();
        if (!string.IsNullOrWhiteSpace(uiLanguage))
            return uiLanguage;

        foreach (var tag in ApplicationLanguages.Languages)
        {
            if (!string.IsNullOrWhiteSpace(tag))
                return tag;
        }

        var culture = CultureInfo.CurrentUICulture.Name;
        return string.IsNullOrWhiteSpace(culture) ? null : culture;
    }

    private static string? GetWindowsUiLanguageTag()
    {
        try
        {
            var lcid = GetUserDefaultUILanguage();
            return lcid == 0 ? null : CultureInfo.GetCultureInfo(lcid).Name;
        }
        catch (Exception ex) when (ex is ArgumentException or CultureNotFoundException)
        {
            return null;
        }
    }

    private static bool TryNormalize(string? value, out string language)
    {
        language = EnglishCode;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var tag = value.Trim().ToLowerInvariant();

        if (tag.StartsWith(RussianCode, StringComparison.Ordinal))
        {
            language = RussianCode;
            return true;
        }

        if (tag.StartsWith(EnglishCode, StringComparison.Ordinal))
        {
            language = EnglishCode;
            return true;
        }

        return false;
    }
}
