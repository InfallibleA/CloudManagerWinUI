using CloudManagerWinUIApp.Dialogs;
using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CloudManagerWinUIApp.Pages;

/// <summary>Настройки: язык интерфейса, адрес сервера и состояние аккаунта.</summary>
public sealed partial class SettingsPage : Page
{
    private bool _suppressLanguageChange;

    public SettingsPage()
    {
        InitializeComponent();
    }

    /// <summary>Строки текущего языка (страница пересоздаётся при смене языка).</summary>
    public Strings L { get; } = Loc.Current;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        AppServices.Auth.StateChanged += OnAuthStateChanged;

        _suppressLanguageChange = true;
        try
        {
            LanguageCombo.SelectedIndex = Loc.Language == Loc.RussianCode ? 0 : 1;
        }
        finally
        {
            _suppressLanguageChange = false;
        }

        UpdateConnectionInfo();
        UpdateSession();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        AppServices.Auth.StateChanged -= OnAuthStateChanged;
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
            UpdateSession();
        else
            DispatcherQueue.TryEnqueue(UpdateSession);
    }

    // --- Язык ------------------------------------------------------------

    private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLanguageChange)
            return;

        if (LanguageCombo.SelectedItem is not ComboBoxItem { Tag: string code } || code == Loc.Language)
            return;

        var settings = UserSettings.Load();
        settings.Language = code;
        settings.Save();

        Loc.Set(code);

        // Пересобираем окно и текущую страницу уже на новом языке.
        DispatcherQueue.TryEnqueue(() => App.MainWindow?.ApplyLocalization());
    }

    // --- Подключение -----------------------------------------------------

    private void UpdateConnectionInfo()
    {
        var settings = AppServices.Settings;
        var strings = Loc.Current;

        ApiAddressText.Text = settings.BaseUrl;

        var source = settings.SourceKind switch
        {
            ApiSettingsSourceKind.Environment => strings.SourceEnvironment(settings.SourceDetail ?? ApiSettings.EnvironmentVariableName),
            ApiSettingsSourceKind.ConfigFile => settings.SourceDetail ?? string.Empty,
            _ => strings.SourceDefault,
        };

        ApiSourceText.Text = strings.SourceLabel(source);

        var warning = settings.WarningKind switch
        {
            ApiSettingsWarningKind.InvalidEnvironmentValue => strings.ConfigInvalidEnvironment(settings.WarningDetail ?? string.Empty),
            ApiSettingsWarningKind.ConfigInvalidJson => strings.ConfigInvalidJson(settings.WarningDetail ?? string.Empty),
            ApiSettingsWarningKind.ConfigUnreadable => strings.ConfigUnreadable(settings.WarningDetail ?? string.Empty),
            ApiSettingsWarningKind.ConfigMissingKey => strings.ConfigMissingKey(settings.WarningDetail ?? string.Empty),
            ApiSettingsWarningKind.ConfigInvalidAddress => strings.ConfigInvalidAddress(settings.WarningDetail ?? string.Empty),
            _ => null,
        };

        ConfigWarningBar.IsOpen = warning is not null;
        ConfigWarningBar.Title = warning is null ? string.Empty : strings.ConfigWarningTitle;
        ConfigWarningBar.Message = warning ?? string.Empty;
    }

    // --- Аккаунт ---------------------------------------------------------

    private void UpdateSession()
    {
        var auth = AppServices.Auth;
        var strings = Loc.Current;

        if (auth.IsAuthenticated && auth.User is not null)
        {
            var until = auth.TokenExpiresAt?.ToLocalTime().ToString("dd.MM.yyyy HH:mm") ?? "—";

            SessionBar.Severity = InfoBarSeverity.Success;
            SessionBar.Title = strings.SignedInAs(auth.User.Login);
            SessionBar.Message = strings.RoleAndToken(auth.User.Role, until);
            SessionBar.IsOpen = true;
        }
        else
        {
            SessionBar.Severity = InfoBarSeverity.Informational;
            SessionBar.Title = strings.NotSignedIn;
            SessionBar.Message = strings.SignInToAccess;
            SessionBar.IsOpen = true;
        }

        SignInButton.Visibility = auth.IsAuthenticated ? Visibility.Collapsed : Visibility.Visible;
        RegisterButton.Visibility = auth.IsAuthenticated ? Visibility.Collapsed : Visibility.Visible;
        SignOutButton.Visibility = auth.IsAuthenticated ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
        => await AuthDialog.ShowAsync(AuthMode.Login);

    private async void Register_Click(object sender, RoutedEventArgs e)
        => await AuthDialog.ShowAsync(AuthMode.Register);

    private void SignOut_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Auth.Logout();
        UpdateSession();
    }
}
