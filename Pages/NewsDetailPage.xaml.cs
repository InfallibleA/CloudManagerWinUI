using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace CloudManagerWinUIApp.Pages;

/// <summary>Полная новость: изображение и полное описание.</summary>
public sealed partial class NewsDetailPage : Page, IFrameBackHandler
{
    private Guid _newsId;

    public NewsDetailPage()
    {
        InitializeComponent();
    }

    // Состояние кнопки «Назад» здесь не меняется — событие не используется.
    public event EventHandler? BackStateChanged
    {
        add { }
        remove { }
    }

    public bool CanGoBack => Frame?.CanGoBack == true;

    public Task<bool> GoBackAsync()
    {
        if (Frame?.CanGoBack != true)
            return Task.FromResult(false);

        Frame.GoBack();
        return Task.FromResult(true);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is NewsItem item)
        {
            _newsId = item.Id;
            NameText.Text = item.Name;
            DescriptionText.Text = item.Description;
            NewsImage.Source = item.Image;
        }

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        SetBusy(true);
        try
        {
            var news = await AppServices.Api.GetNewsByIdAsync(_newsId);

            NameText.Text = news.Name;
            DescriptionText.Text = string.IsNullOrWhiteSpace(news.Description)
                ? Loc.Current.NoDescription
                : news.Description;

            if (news.MediaId is not null && NewsImage.Source is null)
                NewsImage.Source = await ImageLoader.GetAsync(news.MediaId.Value);
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(Loc.Current.NewsItemLoadFailed(ex.Message));
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
    }
}
