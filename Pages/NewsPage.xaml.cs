using System.Collections.ObjectModel;
using System.ComponentModel;
using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace CloudManagerWinUIApp.Pages;

/// <summary>Карточка новости: картинка, имя и описание в одну строку.</summary>
public sealed class NewsItem : INotifyPropertyChanged
{
    private ImageSource? _image;
    private CancellationTokenSource? _imageLoadCts;

    public NewsItem(NewsDto dto)
    {
        Id = dto.Id;
        MediaId = dto.MediaId;
        Name = string.IsNullOrWhiteSpace(dto.Name) ? Loc.Current.Untitled : dto.Name;
        Description = string.IsNullOrWhiteSpace(dto.Description) ? Loc.Current.NoDescription : dto.Description!;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public Guid? MediaId { get; }

    public string Name { get; }

    public string Description { get; }

    /// <summary>Картинка подгружается лениво при появлении в viewport; до этого показывается заглушка.</summary>
    public ImageSource? Image
    {
        get => _image;
        private set
        {
            if (ReferenceEquals(_image, value))
                return;

            _image = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PlaceholderVisibility)));
        }
    }

    public Visibility PlaceholderVisibility => _image is null ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Имя для средств доступности (экранный диктор).</summary>
    public string AutomationName => Loc.Current.OpenNews(Name);

    public async Task EnsureImageLoadedAsync()
    {
        if (_image is not null || MediaId is not Guid fileId || _imageLoadCts is not null)
            return;

        var cts = new CancellationTokenSource();
        _imageLoadCts = cts;
        try
        {
            var image = await ImageLoader.GetAsync(fileId, decodePixelWidth: 480, cts.Token);
            if (!cts.IsCancellationRequested && image is not null)
                Image = image;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_imageLoadCts, cts))
                _imageLoadCts = null;
            cts.Dispose();
        }
    }

    public void CancelPendingImageLoad()
    {
        var cts = _imageLoadCts;
        _imageLoadCts = null;
        cts?.Cancel();
    }
}

/// <summary>Витрина новостей: карточки с переходом к полному описанию.</summary>
public sealed partial class NewsPage : Page
{
    private const int PageSize = 25;

    private readonly ObservableCollection<NewsItem> _items = [];
    private int _loadedPages;

    /// <summary>Строки текущего языка (страница пересоздаётся при смене языка).</summary>
    public Strings L { get; } = Loc.Current;

    public NewsPage()
    {
        InitializeComponent();
        NewsGrid.ItemsSource = _items;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (_items.Count == 0)
            await ReloadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);

        foreach (var item in _items)
            item.CancelPendingImageLoad();
    }

    private async Task ReloadAsync()
    {
        _loadedPages = 0;
        foreach (var item in _items)
            item.CancelPendingImageLoad();
        _items.Clear();
        await LoadNextPageAsync();
    }

    private async Task LoadNextPageAsync()
    {
        SetBusy(true);
        ErrorBar.IsOpen = false;

        try
        {
            var page = _loadedPages + 1;
            var response = await AppServices.Api.GetNewsAsync(page: page, pageSize: PageSize);

            foreach (var dto in response.Items)
                _items.Add(new NewsItem(dto));

            _loadedPages = page;
            MoreButton.Visibility = _items.Count < response.Count ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(Loc.Current.NewsLoadFailed(ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void NewsGrid_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.Item is not NewsItem item)
            return;

        if (args.InRecycleQueue)
        {
            item.CancelPendingImageLoad();
            return;
        }

        if (args.Phase == 0 && item.MediaId is not null && item.Image is null)
            args.RegisterUpdateCallback(OnNewsImagePhase);
    }

    private void OnNewsImagePhase(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (!args.InRecycleQueue && args.Item is NewsItem item)
            _ = item.EnsureImageLoadedAsync();
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

        if (_items.Count == 0)
            EmptyText.Visibility = Visibility.Visible;
    }

    private async void More_Click(object sender, RoutedEventArgs e) => await LoadNextPageAsync();

    private void NewsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NewsItem item)
            Frame.Navigate(typeof(NewsDetailPage), item);
    }
}
