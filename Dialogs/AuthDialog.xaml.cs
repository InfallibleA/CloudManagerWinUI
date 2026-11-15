using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CloudManagerWinUIApp.Dialogs;

public enum AuthMode
{
    Login,
    Register,
}

/// <summary>Диалог входа и регистрации. Переключается между режимами одной кнопкой.</summary>
public sealed partial class AuthDialog : ContentDialog
{
    private AuthMode _mode;

    public AuthDialog()
        : this(AuthMode.Login)
    {
    }

    public AuthDialog(AuthMode mode)
    {
        InitializeComponent();
        SetMode(mode);
    }

    /// <summary>Строки текущего языка.</summary>
    public Strings L { get; } = Loc.Current;

    /// <summary>Показывает диалог. Возвращает <c>true</c>, если пользователь вошёл.</summary>
    public static async Task<bool> ShowAsync(AuthMode mode)
    {
        var dialog = new AuthDialog(mode);
        if (App.MainWindow is not null)
            dialog.XamlRoot = App.MainWindow.Content.XamlRoot;

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void SetMode(AuthMode mode)
    {
        var strings = Loc.Current;
        _mode = mode;
        ErrorBar.IsOpen = false;
        CloseButtonText = strings.Cancel;

        if (mode == AuthMode.Register)
        {
            Title = strings.RegisterTitle;
            PrimaryButtonText = strings.RegisterButton;
            ConfirmBox.Visibility = Visibility.Visible;
            SwitchModeLink.Content = strings.HaveAccountSignIn;
        }
        else
        {
            Title = strings.SignInTitle;
            PrimaryButtonText = strings.SignIn;
            ConfirmBox.Visibility = Visibility.Collapsed;
            SwitchModeLink.Content = strings.NoAccountRegister;
        }
    }

    private void SwitchMode_Click(object sender, RoutedEventArgs e)
        => SetMode(_mode == AuthMode.Login ? AuthMode.Register : AuthMode.Login);

    private void OnFieldChanged(object sender, RoutedEventArgs e) => ErrorBar.IsOpen = false;

    private async void AuthDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            ErrorBar.IsOpen = false;

            var login = LoginBox.Text.Trim();
            var password = PasswordBox.Password;
            var strings = Loc.Current;

            if (login.Length < 3)
            {
                Fail(strings.LoginTooShort);
                args.Cancel = true;
                return;
            }

            if (password.Length < 8)
            {
                Fail(strings.PasswordTooShort);
                args.Cancel = true;
                return;
            }

            if (_mode == AuthMode.Register && password != ConfirmBox.Password)
            {
                Fail(strings.PasswordsDoNotMatch);
                args.Cancel = true;
                return;
            }

            IsPrimaryButtonEnabled = false;
            try
            {
                if (_mode == AuthMode.Register)
                    await AppServices.Auth.RegisterAsync(login, password);
                else
                    await AppServices.Auth.LoginAsync(login, password);
            }
            catch (ApiException ex)
            {
                // Блокировка приходит как 403: показываем локализованную причину и
                // оставляем диалог открытым — войти в заблокированный аккаунт нельзя.
                Fail(ex.IsBlockedUser ? strings.AccountBlocked(ex.DetailOrMessage) : ex.Message);
                args.Cancel = true;
            }
            catch (Exception ex)
            {
                Fail(Loc.Current.ServerUnreachable(ex.Message));
                args.Cancel = true;
            }
            finally
            {
                IsPrimaryButtonEnabled = true;
            }
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Fail(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
