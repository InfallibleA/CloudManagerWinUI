using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Media.Core;
using Windows.Media.Playback;

namespace CloudManagerWinUIApp.Pages;

/// <summary>Видео, которое нужно открыть в плеере, и путь папки для возврата после закрытия.</summary>
public sealed record VideoPlaybackRequest(Guid VideoId, string Name, IReadOnlyList<FolderCrumb> Path);

/// <summary>
/// Просмотр видео. Ссылка на поток берётся у API (<c>api/videos/{id}/playback</c>) и ведёт
/// прямо в S3-хранилище: файл не скачивается на диск и не проксируется через API.
/// </summary>
public sealed partial class VideoPlayerPage : Page, IFrameBackHandler
{
    private MediaPlayer? _player;

    /// <summary>Папка, из которой открыли видео: в неё возвращает кнопка «Назад».</summary>
    private IReadOnlyList<FolderCrumb> _returnPath = [];

    /// <summary>Страница уже закрыта: ответ API, пришедший позже, не должен включать плеер.</summary>
    private bool _closed;

    public VideoPlayerPage()
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

        if (e.Parameter is not VideoPlaybackRequest request)
            return;

        _returnPath = request.Path;
        TitleText.Text = request.Name;
        SetBusy(true);
        await StartAsync(request.VideoId);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        Stop();
    }

    /// <summary>
    /// Presigned-ссылка живёт ограниченное время, поэтому запрашиваем её при каждом
    /// открытии страницы и сразу передаём плееру.
    /// </summary>
    private async Task StartAsync(Guid videoId)
    {
        VideoPlaybackDto playback;
        try
        {
            playback = await AppServices.Api.GetVideoPlaybackAsync(videoId);
        }
        catch (ApiException ex)
        {
            Fail(ex.Message);
            return;
        }
        catch (Exception ex)
        {
            Fail(Loc.Current.VideoPlaybackFailed(ex.Message));
            return;
        }

        if (_closed)
            return;

        var player = new MediaPlayer { AutoPlay = true };
        player.MediaOpened += OnMediaOpened;
        player.MediaFailed += OnMediaFailed;

        _player = player;
        Player.SetMediaPlayer(player);
        player.Source = MediaSource.CreateFromUri(new Uri(playback.Url));
    }

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
            ShowError(Loc.Current.VideoPlaybackFailed(reason));
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
        if (backStack.Count > 0 && backStack[^1].SourcePageType == typeof(VideoPlayerPage))
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
