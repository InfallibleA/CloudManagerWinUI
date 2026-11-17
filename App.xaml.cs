using System.Globalization;
using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Xaml;
using Windows.Globalization;


namespace CloudManagerWinUIApp;

public partial class App : Application
{
    private Window? _window;

    /// <summary>Главное окно приложения (нужно для диалогов и файловых пикеров).</summary>
    public static MainWindow? MainWindow { get; private set; }

    public static string SystemLanguage =>
        Loc.DetectSystemLanguageTag() ?? CultureInfo.CurrentUICulture.Name;

    public static string SystemLanguageCode =>
        new CultureInfo(SystemLanguage).TwoLetterISOLanguageName;

    public static string SystemLanguageDisplayName =>
        new CultureInfo(SystemLanguage).NativeName;

    public static IReadOnlyList<string> PreferredLanguages =>
        (IReadOnlyList<string>)ApplicationLanguages.Languages;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Сохранённый выбор пользователя → язык системы → английский.
        var userSettings = UserSettings.Load();
        Loc.Initialize(userSettings.Language);

        AppServices.Initialize();

        var window = new MainWindow();
        MainWindow = window;
        _window = window;
        _window.Activate();

        // Восстановление сохранённой сессии не блокирует показ окна.
        _ = window.InitializeSessionAsync();
    }
}