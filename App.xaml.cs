using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;

using System.Runtime.InteropServices;
using System.Globalization;
using Windows.Globalization;


namespace CloudManagerWinUIApp;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);
    private const int ATTACH_PARENT_PROCESS = -1;
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

        // Для отладки — вывести язык системы в консоль
        Console.WriteLine($"System language: {SystemLanguage}");
        Console.WriteLine($"Language code:   {SystemLanguageCode}");
        Console.WriteLine($"Display name:    {SystemLanguageDisplayName}");
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