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

    public NewsItem(NewsDto dto)
    {
        Id = dto.Id;
        Name = string.IsNullOrWhiteSpace(dto.Name) ? Loc.Current.Untitled : dto.Name;
        Description = string.IsNullOrWhiteSpace(dto.Description) ? Loc.Current.NoDescription : dto.Description!;

        if (dto.MediaId is not null)
            _ = LoadImageAsync(dto.MediaId.Value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string Name { get; }

    public string Description { get; }

    /// <summary>Картинка подгружается асинхронно; до этого показывается заглушка.</summary>
    public ImageSource? Image
    {
        get => _image;
        private set
        {
            _image = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Image)));
        }
    }

    /// <summary>Имя для средств доступности (экранный диктор).</summary>
    public string AutomationName => Loc.Current.OpenNews(Name);

    private async Task LoadImageAsync(Guid fileId)
    {
        var image = await ImageLoader.GetAsync(fileId);
        if (image is not null)
            Image = image;
    }
}

/// <summary>Витрина новостей: карточки с переходом к полному описанию.</summary>
public sealed partial class NewsPage : Page
{
    private const int PageSize = 25;

    private int _loadedPages;

    /// <summary>Строки текущего языка (страница пересоздаётся при смене языка).</summary>
    public Strings L { get; } = Loc.Current;

    public NewsPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (NewsGrid.Items.Count == 0)
            await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        _loadedPages = 0;
        NewsGrid.Items.Clear();
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
                NewsGrid.Items.Add(new NewsItem(dto));

            _loadedPages = page;
            MoreButton.Visibility = NewsGrid.Items.Count < response.Count ? Visibility.Visible : Visibility.Collapsed;
            EmptyText.Visibility = NewsGrid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;

        if (NewsGrid.Items.Count == 0)
            EmptyText.Visibility = Visibility.Visible;
    }

    private async void More_Click(object sender, RoutedEventArgs e) => await LoadNextPageAsync();

    private void NewsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NewsItem item)
            Frame.Navigate(typeof(NewsDetailPage), item);
    }
}
