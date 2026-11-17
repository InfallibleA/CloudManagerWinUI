using CloudManagerWinUIApp.Dialogs;
using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Pages;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CloudManagerWinUIApp;

public sealed partial class MainWindow : Window
{
    private bool _syncingSelection;
    private IFrameBackHandler? _backHandler;

    /// <summary>Причина блокировки для баннера; <c>null</c>, если баннер не актуален.</summary>
    private string? _blockedReason;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon("Assets/AppIcon.ico");

        AppServices.Auth.StateChanged += OnAuthStateChanged;
        AppServices.Auth.Blocked += OnUserBlocked;
        Closed += (_, _) =>
        {
            AppServices.Auth.StateChanged -= OnAuthStateChanged;
            AppServices.Auth.Blocked -= OnUserBlocked;
        };

        ApplyLocalization(reloadPage: false);
        NavFrame.Navigate(typeof(NewsPage));
        UpdateAuthUi();

        // Пункт «Настройки» создаётся шаблоном NavigationView, поэтому его текст
        // локализуем ещё раз после загрузки панели.
        NavView.Loaded += OnNavViewLoaded;
    }

    private void OnNavViewLoaded(object sender, RoutedEventArgs e)
    {
        NavView.Loaded -= OnNavViewLoaded;
        ApplyLocalization(reloadPage: false);
    }

    /// <summary>Применяет строки текущего языка к заголовку окна и меню.</summary>
    public void ApplyLocalization(bool reloadPage = true)
    {
        var strings = Loc.Current;

        AppTitleBar.Title = strings.AppTitle;

        NewsNavItem.Content = strings.NewsTab;
        FilesNavItem.Content = strings.FilesTab;
        AdminNavItem.Content = strings.AdminTab;
        AboutNavItem.Content = strings.AboutTab;

        if (NavView.SettingsItem is NavigationViewItem settingsItem)
            settingsItem.Content = strings.SettingsTab;

        SignInText.Text = strings.SignIn;
        AutomationProperties.SetName(SignInButton, strings.SignInAutomation);
        ToolTipService.SetToolTip(SignInButton, strings.SignInTooltip);
        ToolTipService.SetToolTip(AccountButton, strings.AccountTooltip);

        if (AccountButton.Flyout is MenuFlyout menu && menu.Items.OfType<MenuFlyoutItem>().FirstOrDefault() is { } signOut)
        {
            signOut.Text = strings.SignOut;
            AutomationProperties.SetName(signOut, strings.SignOutAutomation);
        }

        // Баннер блокировки тоже переводится на новый язык.
        if (BlockedBar.IsOpen)
            ShowBlockedBar(_blockedReason);

        UpdateAuthUi();

        if (reloadPage && NavFrame.Content is Page currentPage)
            NavFrame.Navigate(currentPage.GetType());
    }

    /// <summary>Восстанавливает сохранённую сессию и обновляет интерфейс.</summary>
    public async Task InitializeSessionAsync()
    {
        await AppServices.Auth.InitializeAsync();
        UpdateAuthUi();
    }

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
            UpdateAuthUi();
        else
            DispatcherQueue.TryEnqueue(UpdateAuthUi);
    }

    /// <summary>
    /// Сессия потеряна из-за блокировки администратором: показываем причину баннером.
    /// Модальный диалог намеренно не используется — остальной интерфейс (новости,
    /// настройки) остаётся доступным без входа.
    /// </summary>
    private void OnUserBlocked(object? sender, EventArgs e)
    {
        var reason = AppServices.Auth.BlockedReason;

        if (DispatcherQueue.HasThreadAccess)
            ShowBlockedBar(reason);
        else
            DispatcherQueue.TryEnqueue(() => ShowBlockedBar(reason));
    }

    private void ShowBlockedBar(string? reason)
    {
        _blockedReason = string.IsNullOrWhiteSpace(reason) ? null : reason;

        var strings = Loc.Current;
        BlockedBar.Severity = InfoBarSeverity.Error;
        BlockedBar.Title = strings.AccountBlockedShort;
        BlockedBar.Message = _blockedReason is null
            ? strings.AccountBlockedShort
            : strings.SignedOutBecauseBlocked(_blockedReason);
        BlockedBar.IsOpen = true;
    }

    /// <summary>Баннер больше не актуален (вход или ручной выход).</summary>
    private void CloseBlockedBar()
    {
        _blockedReason = null;
        BlockedBar.IsOpen = false;
    }

    private void UpdateAuthUi()
    {
        var auth = AppServices.Auth;
        var strings = Loc.Current;

        if (auth.IsAuthenticated)
        {
            AccountLoginText.Text = auth.Login;
            AutomationProperties.SetName(AccountButton, strings.AccountAutomation(auth.Login));
            AccountButton.Visibility = Visibility.Visible;
            SignInButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            AccountButton.Visibility = Visibility.Collapsed;
            SignInButton.Visibility = Visibility.Visible;
        }

        // «Админ-панель» видна только администратору.
        var isAdmin = auth.IsAdmin;
        AdminNavItem.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

        // Успешный вход снимает баннер блокировки: причина к нему уже не относится.
        if (auth.IsAuthenticated)
            CloseBlockedBar();

        // Права потеряны, пока страница была открыта, — уходим на «Новости».
        if (!isAdmin && NavFrame.Content is AdminPage)
            NavFrame.Navigate(typeof(NewsPage));
    }

    private async void SignInButton_Click(object sender, RoutedEventArgs e)
        => await AuthDialog.ShowAsync(AuthMode.Login);

    private void SignOutItem_Click(object sender, RoutedEventArgs e)
    {
        AppServices.Auth.Logout();

        // Ручной выход — не блокировка: старый баннер не должен оставаться на экране.
        CloseBlockedBar();
        UpdateAuthUi();
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        if (_backHandler is not null)
            _backHandler.BackStateChanged -= OnBackStateChanged;

        _backHandler = NavFrame.Content as IFrameBackHandler;
        if (_backHandler is not null)
            _backHandler.BackStateChanged += OnBackStateChanged;

        SyncNavSelection(e.SourcePageType);
        UpdateBackButton();
    }

    private void OnBackStateChanged(object? sender, EventArgs e) => UpdateBackButton();

    /// <summary>Кнопка «Назад» видна только если страница умеет возвращаться назад.</summary>
    private void UpdateBackButton()
        => AppTitleBar.IsBackButtonVisible = _backHandler?.CanGoBack == true;

    /// <summary>Держит выбранный пункт меню в соответствии с открытой страницей.</summary>
    private void SyncNavSelection(Type? pageType)
    {
        if (pageType is null)
            return;

        object? target = pageType == typeof(SettingsPage)
            ? NavView.SettingsItem
            : pageType switch
            {
                _ when pageType == typeof(FilesPage) => FindMenuItem("files"),
                _ when pageType == typeof(MediaPlayerPage) => FindMenuItem("files"),
                _ when pageType == typeof(TextViewerPage) => FindMenuItem("files"),
                _ when pageType == typeof(SpreadsheetViewerPage) => FindMenuItem("files"),
                _ when pageType == typeof(AdminPage) => FindMenuItem("admin"),
                _ when pageType == typeof(AboutPage) => FindMenuItem("about"),
                _ when pageType == typeof(NewsPage) || pageType == typeof(NewsDetailPage) => FindMenuItem("news"),
                _ => null,
            };

        if (target is null || ReferenceEquals(NavView.SelectedItem, target))
            return;

        _syncingSelection = true;
        try
        {
            NavView.SelectedItem = target;
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private NavigationViewItem? FindMenuItem(string tag)
        => NavView.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(item => Equals(item.Tag, tag));

    private void TitleBar_PaneToggleRequested(TitleBar sender, object args)
    {
        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    private async void TitleBar_BackRequested(TitleBar sender, object args)
    {
        if (_backHandler is not null)
            await _backHandler.GoBackAsync();
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingSelection)
            return;

        if (args.IsSettingsSelected)
        {
            NavFrame.Navigate(typeof(SettingsPage));
        }
        else if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag)
            {
                case "news":
                    NavFrame.Navigate(typeof(NewsPage));
                    break;
                case "files":
                    NavFrame.Navigate(typeof(FilesPage));
                    break;
                case "admin":
                    NavFrame.Navigate(typeof(AdminPage));
                    break;
                case "about":
                    NavFrame.Navigate(typeof(AboutPage));
                    break;
                default:
                    throw new InvalidOperationException($"Unknown navigation item tag: {item.Tag}");
            }
        }
    }
}
