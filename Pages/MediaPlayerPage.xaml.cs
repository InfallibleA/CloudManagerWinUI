using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace CloudManagerWinUIApp.Pages;

/// <summary>Тип воспроизводимого медиаэлемента.</summary>
public enum MediaKind
{
    Video,
    Audio,
}

/// <summary>Медиаэлемент для открытия в плеере и путь папки для возврата после закрытия.</summary>
public sealed record MediaPlaybackRequest(Guid MediaId, string Name, MediaKind Kind, IReadOnlyList<FolderCrumb> Path);

/// <summary>
/// Встроенный плеер видео и аудио. Presigned-ссылка на поток берётся у API
/// (<c>api/videos/{id}/playback</c> или <c>api/audios/{id}/playback</c>) и ведёт прямо в S3:
/// файл не скачивается целиком на диск и не проксируется через API.
/// </summary>
public sealed partial class MediaPlayerPage : Page, IFrameBackHandler
{
    private MediaPlayer? _player;
    private MediaKind _kind = MediaKind.Video;

    /// <summary>Папка, из которой открыли медиафайл: в неё возвращает кнопка «Назад».</summary>
    private IReadOnlyList<FolderCrumb> _returnPath = [];

    /// <summary>Страница уже закрыта: ответ API, пришедший позже, не должен включать плеер.</summary>
    private bool _closed;

    public MediaPlayerPage()
    {
        InitializeComponent();
    }

    /// <summary>Строки текущего языка (страница пересоздаётся при смене языка).</summary>
    public Strings L { get; } = Loc.Current;

    // Кнопка «Назад» закрывает плеер; состояние страницы при этом не меняется.
    public event EventHandler? BackStateChanged
    {
        add { }
        remove { }
    }

    public bool CanGoBack => Frame is not null;

    public Task<bool> GoBackAsync()
    {
        if (Frame is null)
            return Task.FromResult(false);

        Close();
        return Task.FromResult(true);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not MediaPlaybackRequest request)
            return;

        _kind = request.Kind;
        _returnPath = request.Path;
        TitleText.Text = request.Name;
        ApplyLayoutMode(request.Kind, request.Name);
        SetBusy(true);
        await StartAsync(request);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Stop();
    }

    private void ApplyLayoutMode(MediaKind kind, string name)
    {
        if (kind == MediaKind.Audio)
        {
            FindName(nameof(AudioCard));
            AudioCard.Visibility = Visibility.Visible;
            AudioTrackTitle.Text = name;
            Grid.SetRow(Player, 1);
            Grid.SetRowSpan(Player, 1);
            Player.Height = double.NaN;
            Player.MinHeight = 120;
            Player.MaxWidth = 640;
            Player.HorizontalAlignment = HorizontalAlignment.Stretch;
            Player.VerticalAlignment = VerticalAlignment.Bottom;
            TransportControls.ShowAndHideAutomatically = false;
            TransportControls.IsZoomButtonVisible = false;
            TransportControls.IsCompact = false;
            AutomationProperties.SetName(Player, L.Audio);
        }
        else
        {
            if (AudioCard is not null)
                AudioCard.Visibility = Visibility.Collapsed;
            Grid.SetRow(Player, 0);
            Grid.SetRowSpan(Player, 2);
            Player.Height = double.NaN;
            Player.MinHeight = 0;
            Player.MaxWidth = double.PositiveInfinity;
            Player.HorizontalAlignment = HorizontalAlignment.Stretch;
            Player.VerticalAlignment = VerticalAlignment.Stretch;
            TransportControls.ShowAndHideAutomatically = true;
            TransportControls.IsZoomButtonVisible = true;
            AutomationProperties.SetName(Player, L.Video);
        }
    }

    /// <summary>
    /// Presigned-ссылка живёт ограниченное время, поэтому запрашиваем её при каждом
    /// открытии страницы и сразу передаём плееру.
    /// </summary>
    private async Task StartAsync(MediaPlaybackRequest request)
    {
        string url;
        try
        {
            url = request.Kind == MediaKind.Audio
                ? (await AppServices.Api.GetAudioPlaybackAsync(request.MediaId)).Url
                : (await AppServices.Api.GetVideoPlaybackAsync(request.MediaId)).Url;
        }
        catch (ApiException ex)
        {
            Fail(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            Fail(FormatPlaybackError(ex.Message));
            return;
        }

        if (_closed)
            return;

        var player = new MediaPlayer { AutoPlay = true };
        player.MediaOpened += OnMediaOpened;
        player.MediaFailed += OnMediaFailed;

        _player = player;
        Player.SetMediaPlayer(player);
        player.Source = MediaSource.CreateFromUri(new Uri(url));
    }

    private string FormatPlaybackError(string reason)
        => _kind == MediaKind.Audio
            ? Loc.Current.AudioPlaybackFailed(reason)
            : Loc.Current.VideoPlaybackFailed(reason);

    private void Fail(string message)
    {
        if (_closed)
            return;

        SetBusy(false);
        ShowError(message);
    }

    /// <summary>Останавливает и освобождает плеер: уход со страницы не должен оставлять звук.</summary>
    private void Stop()
    {
        _closed = true;

        var player = _player;
        _player = null;
        if (player is null)
            return;

        player.MediaOpened -= OnMediaOpened;
        player.MediaFailed -= OnMediaFailed;
        Player.SetMediaPlayer(null);
        player.Pause();
        player.Dispose();
    }

    // События MediaPlayer приходят не из UI-потока, поэтому всё, что трогает XAML,
    // выполняется через DispatcherQueue; ответ устаревшего плеера игнорируется.
    private void OnMediaOpened(MediaPlayer sender, object args)
        => DispatcherQueue.TryEnqueue(() =>
        {
            if (ReferenceEquals(_player, sender))
                SetBusy(false);
        });

    private void OnMediaFailed(MediaPlayer sender, MediaPlayerFailedEventArgs args)
    {
        var reason = string.IsNullOrWhiteSpace(args.ErrorMessage)
            ? args.ExtendedErrorCode?.Message ?? string.Empty
            : args.ErrorMessage;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!ReferenceEquals(_player, sender))
                return;

            SetBusy(false);
            ShowError(FormatPlaybackError(reason));
        });
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _ = GoBackAsync();

    /// <summary>
    /// Закрывает плеер и возвращает в ту же папку. Страница файлов создаётся заново,
    /// поэтому путь передаётся ей параметром, а запись плеера убирается из истории —
    /// иначе кнопка «Назад» на странице файлов снова открыла бы плеер.
    /// </summary>
    private void Close()
    {
        if (Frame is null)
            return;

        Stop();
        Frame.Navigate(typeof(FilesPage), new FolderPathRequest(_returnPath));

        var backStack = Frame.BackStack;
        if (backStack.Count > 0 && backStack[^1].SourcePageType == typeof(MediaPlayerPage))
            backStack.RemoveAt(backStack.Count - 1);
    }

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
