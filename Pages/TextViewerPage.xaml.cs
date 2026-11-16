using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace CloudManagerWinUIApp.Pages;

/// <summary>Текстовый файл для просмотра и путь папки для возврата после закрытия.</summary>
public sealed record TextViewerRequest(Guid FileId, string Name, IReadOnlyList<FolderCrumb> Path);

/// <summary>
/// Встроенный просмотр текстовых файлов (чтение, перенос строк, копирование, скачивание).
/// </summary>
public sealed partial class TextViewerPage : Page, IFrameBackHandler
{
    private Guid _fileId;
    private string _fileName = string.Empty;
    private IReadOnlyList<FolderCrumb> _returnPath = [];
    private CancellationTokenSource? _loadCts;
    private bool _closed;

    public TextViewerPage()
    {
        InitializeComponent();
    }

    /// <summary>Строки текущего языка (страница пересоздаётся при смене языка).</summary>
    public Strings L { get; } = Loc.Current;

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

        if (e.Parameter is not TextViewerRequest request)
            return;

        _fileId = request.FileId;
        _fileName = request.Name;
        _returnPath = request.Path;
        TitleText.Text = request.Name;

        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        CancelLoad();
    }

    private async Task LoadAsync()
    {
        CancelLoad();
        _closed = false;
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        SetBusy(true);
        StatusBar.IsOpen = false;
        CopyButton.IsEnabled = false;

        try
        {
            FileTextContent result;
            if (DocumentTextExtractor.IsDocumentExtension(_fileName))
            {
                var bytes = await AppServices.Api.DownloadFileBytesAsync(_fileId, ct: cts.Token);
                var extracted = await Task.Run(() => DocumentTextExtractor.ExtractText(bytes, _fileName), cts.Token);
                result = new FileTextContent(extracted, IsTruncated: false);
            }
            else
            {
                result = await AppServices.Api.DownloadFileTextAsync(_fileId, ct: cts.Token);
            }

            if (_closed || cts.IsCancellationRequested)
                return;

            ContentBox.Text = result.Text;
            CopyButton.IsEnabled = true;

            if (result.IsTruncated)
                ShowNotice(Loc.Current.TextFileTruncated, InfoBarSeverity.Warning);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiException ex)
        {
            if (!_closed)
                ShowNotice(ex.Message, InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            if (!_closed)
                ShowNotice(Loc.Current.TextLoadFailed(ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            if (!_closed)
                SetBusy(false);
        }
    }

    private void WrapToggle_Click(object sender, RoutedEventArgs e)
        => ContentBox.TextWrapping = WrapToggle.IsChecked == true
            ? TextWrapping.Wrap
            : TextWrapping.NoWrap;

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(ContentBox.Text);
        Clipboard.SetContent(package);
        ShowNotice(Loc.Current.Copied, InfoBarSeverity.Success);
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var path = await FileSaver.DownloadAsync(_fileId, _fileName);
            if (path is not null)
                ShowNotice($"{Loc.Current.FileSaved}: {path}", InfoBarSeverity.Success);
        }
        catch (ApiException ex)
        {
            ShowNotice(ex.Message, InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            ShowNotice(Loc.Current.TextLoadFailed(ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _ = GoBackAsync();

    /// <summary>
    /// Возвращает в ту же папку файлового менеджера и убирает страницу просмотра из истории.
    /// </summary>
    private void Close()
    {
        if (Frame is null)
            return;

        CancelLoad();
        Frame.Navigate(typeof(FilesPage), new FolderPathRequest(_returnPath));

        var backStack = Frame.BackStack;
        if (backStack.Count > 0 && backStack[^1].SourcePageType == typeof(TextViewerPage))
            backStack.RemoveAt(backStack.Count - 1);
    }

    private void CancelLoad()
    {
        _closed = true;
        var cts = _loadCts;
        _loadCts = null;
        if (cts is null)
            return;

        cts.Cancel();
        cts.Dispose();
    }

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
