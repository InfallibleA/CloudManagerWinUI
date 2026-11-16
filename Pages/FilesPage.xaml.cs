using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace CloudManagerWinUIApp.Pages;

/// <summary>Элемент сетки файлового менеджера: папка, файл или видео.</summary>
public sealed class ExplorerItem
{
    private ExplorerItem(Guid id, string name, bool isFolder, bool isVideo, bool isText, bool isSpreadsheet)
    {
        Id = id;
        Name = name;
        IsFolder = isFolder;
        IsVideo = isVideo;
        IsText = isText;
        IsSpreadsheet = isSpreadsheet;
    }

    public Guid Id { get; }

    public string Name { get; }

    public bool IsFolder { get; }

    /// <summary>Видео открывается кликом в плеере.</summary>
    public bool IsVideo { get; }

    /// <summary>Текстовый файл открывается кликом во встроенном просмотре.</summary>
    public bool IsText { get; }

    /// <summary>Таблица (.xlsx/.xlsm/.xlsb/.xltx/.xltm/.xls) открывается во встроенном просмотре таблиц.</summary>
    public bool IsSpreadsheet { get; }

    public string Glyph => ExplorerIcons.GlyphFor(IsFolder, IsVideo, IsText, IsSpreadsheet);

    public SolidColorBrush IconBrush => ExplorerIcons.BrushFor(IsFolder, IsVideo, IsSpreadsheet);

    public string AutomationName => IsFolder
        ? Loc.Current.FolderAutomation(Name)
        : IsVideo
            ? Loc.Current.VideoAutomation(Name)
            : Loc.Current.FileAutomation(Name);

    public static ExplorerItem FromFolder(FolderShortDto dto)
        => new(dto.Id, dto.Name, isFolder: true, isVideo: false, isText: false, isSpreadsheet: false);

    public static ExplorerItem FromFile(FileShortDto dto, bool isText = false, bool isSpreadsheet = false)
        => new(dto.Id, dto.Name, isFolder: false, isVideo: false, isText: isText, isSpreadsheet: isSpreadsheet);

    public static ExplorerItem FromVideo(VideoShortDto dto)
        => new(dto.Id, dto.Name, isFolder: false, isVideo: true, isText: false, isSpreadsheet: false);
}

/// <summary>Сегмент пути в «хлебных крошках».</summary>
public sealed class FolderCrumb(string name, Guid folderId)
{
    public string Name { get; } = name;

    public Guid FolderId { get; } = folderId;

    public override string ToString() => Name;
}

/// <summary>Возврат из плеера: путь, который страница файлов должна показать снова.</summary>
public sealed record FolderPathRequest(IReadOnlyList<FolderCrumb> Path);

/// <summary>
/// Файловый менеджер пользователя: сетка папок, файлов и видео текущей папки.
/// Показываются только папки текущего пользователя (фильтр <c>creator_id</c>)
/// и файлы/видео внутри его папок; видео открывается кликом в плеере.
/// </summary>
public sealed partial class FilesPage : Page, IFrameBackHandler
{
    private const int PageSize = 100;

    private readonly List<FolderCrumb> _path = [];
    private IReadOnlyList<FileTypeDto> _types = [];
    private IReadOnlyList<VideoTypeDto> _videoTypes = [];

    public FilesPage()
    {
        InitializeComponent();
    }

    /// <summary>Строки текущего языка (страница пересоздаётся при смене языка).</summary>
    public Strings L { get; } = Loc.Current;

    public event EventHandler? BackStateChanged;

    private Guid? CurrentFolderId => _path.Count > 0 ? _path[^1].FolderId : null;

    /// <summary>Назад = подняться из папки; в корневой папке переходить некуда.</summary>
    public bool CanGoBack => _path.Count > 1;

    public async Task<bool> GoBackAsync()
    {
        if (_path.Count <= 1)
            return false;

        _path.RemoveAt(_path.Count - 1);
        await OpenFolderAsync(_path[^1].FolderId);
        return true;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        AppServices.Auth.StateChanged += OnAuthStateChanged;
        UpdateAuthState();

        await LoadTypesAsync();

        if (!AppServices.Auth.IsAuthenticated)
            return;

        // Возврат из плеера: показываем ту же папку, из которой открывали видео.
        if (e.Parameter is FolderPathRequest restore && restore.Path.Count > 0)
        {
            _path.Clear();
            _path.AddRange(restore.Path);
            await RunAsync(() => OpenFolderAsync(_path[^1].FolderId));
            return;
        }

        await RunAsync(LoadRootAsync);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        AppServices.Auth.StateChanged -= OnAuthStateChanged;
    }

    // --- Состояние авторизации -------------------------------------------

    private void OnAuthStateChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
            ApplyAuthChange();
        else
            DispatcherQueue.TryEnqueue(ApplyAuthChange);
    }

    private void ApplyAuthChange()
    {
        UpdateAuthState();

        if (AppServices.Auth.IsAuthenticated)
            _ = RunAsync(LoadRootAsync);
        else
            ClearItems();
    }

    private void UpdateAuthState()
    {
        var authorized = AppServices.Auth.IsAuthenticated;
        FilesRoot.Visibility = authorized ? Visibility.Visible : Visibility.Collapsed;
        AuthRequiredPanel.Visibility = authorized ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearItems()
    {
        _path.Clear();
        ItemsGrid.Items.Clear();
        FolderBreadcrumb.ItemsSource = Array.Empty<FolderCrumb>();
        EmptyText.Visibility = Visibility.Collapsed;
        NoRootText.Visibility = Visibility.Collapsed;
        ErrorBar.IsOpen = false;
        BackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
        => await Dialogs.AuthDialog.ShowAsync(Dialogs.AuthMode.Login);

    private async void Register_Click(object sender, RoutedEventArgs e)
        => await Dialogs.AuthDialog.ShowAsync(Dialogs.AuthMode.Register);

    // --- Загрузка содержимого --------------------------------------------

    /// <summary>
    /// Находит корневую папку пользователя (<c>is_root</c>) и открывает её.
    /// Если корневой папки нет (аккаунты, созданные до её появления) — показывает сообщение.
    /// </summary>
    private async Task LoadRootAsync()
    {
        SetBusy(true);
        ErrorBar.IsOpen = false;
        EmptyText.Visibility = Visibility.Collapsed;
        NoRootText.Visibility = Visibility.Collapsed;

        try
        {
            var userId = AppServices.Auth.User?.Id;
            var roots = await AppServices.Api.GetFoldersAsync(pageSize: PageSize, rootOnly: true, creatorId: userId);
            var root = roots.Items.FirstOrDefault(folder => folder.IsRoot) ?? roots.Items.FirstOrDefault();

            if (root is null)
            {
                _path.Clear();
                ItemsGrid.Items.Clear();
                FolderBreadcrumb.ItemsSource = Array.Empty<FolderCrumb>();
                AddButton.IsEnabled = false;
                NoRootText.Visibility = Visibility.Visible;
                return;
            }

            AddButton.IsEnabled = true;
            _path.Clear();
            _path.Add(new FolderCrumb(root.Name, root.Id));
            await LoadFolderContentAsync(root.Id);
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(Loc.Current.FilesOpenFailed(ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task OpenFolderAsync(Guid folderId)
    {
        SetBusy(true);
        ErrorBar.IsOpen = false;

        try
        {
            await LoadFolderContentAsync(folderId);
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(Loc.Current.FolderOpenFailed(ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadFolderContentAsync(Guid folderId)
    {
        var folder = await AppServices.Api.GetFolderAsync(folderId);

        ItemsGrid.Items.Clear();
        foreach (var child in folder.Children ?? [])
            ItemsGrid.Items.Add(ExplorerItem.FromFolder(child));
        foreach (var file in folder.Files ?? [])
            ItemsGrid.Items.Add(ExplorerItem.FromFile(file, IsTextFile(file), IsSpreadsheetFile(file)));
        foreach (var video in folder.Videos ?? [])
            ItemsGrid.Items.Add(ExplorerItem.FromVideo(video));

        FolderBreadcrumb.ItemsSource = _path.ToList();
        EmptyText.Visibility = ItemsGrid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoRootText.Visibility = Visibility.Collapsed;
        BackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ReloadCurrentAsync()
    {
        var folderId = CurrentFolderId;
        if (folderId is null)
            return;

        await LoadFolderContentAsync(folderId.Value);
    }

    private async Task LoadTypesAsync()
    {
        if (_types.Count > 0)
            return;

        try
        {
            _types = await AppServices.Api.GetFileTypesAsync() ?? [];
        }
        catch (Exception)
        {
            _types = [];
        }
    }

    private static bool IsSpreadsheetFile(FileShortDto dto)
    {
        var typeExt = dto.Type.TrimStart('.');
        return SpreadsheetReader.IsSpreadsheetExtension(dto.Name)
               || SpreadsheetReader.IsSpreadsheetExtension("f." + typeExt);
    }

    private bool IsTextFile(FileShortDto dto)
    {
        var typeExt = dto.Type.TrimStart('.');
        var nameExt = Path.GetExtension(dto.Name).TrimStart('.');

        if (_types.Any(t =>
                (string.Equals(t.Value.TrimStart('.'), typeExt, StringComparison.OrdinalIgnoreCase)
                 || string.Equals(t.Value.TrimStart('.'), nameExt, StringComparison.OrdinalIgnoreCase))
                && IsTextContentType(t.ContentType)))
        {
            return true;
        }

        return IsKnownTextExtension(typeExt) || IsKnownTextExtension(nameExt);
    }

    private static bool IsTextContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
            return false;

        return contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/javascript", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/msword", StringComparison.OrdinalIgnoreCase)
            || contentType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownTextExtension(string ext) => ext.ToLowerInvariant() switch
    {
        "txt" or "md" or "markdown" or "json" or "xml" or "csv" or "tsv" or "log"
            or "yaml" or "yml" or "ini" or "cfg" or "conf" or "cs" or "py" or "js"
            or "ts" or "html" or "htm" or "css" or "sql" or "sh" or "ps1" or "bat"
            or "doc" or "docx" or "rtf" => true,
        _ => false,
    };

    // --- Навигация -------------------------------------------------------

    private async void ItemsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ExplorerItem item)
            return;

        if (item.IsVideo)
        {
            Frame.Navigate(
                typeof(VideoPlayerPage),
                new VideoPlaybackRequest(item.Id, item.Name, _path.ToList()));
            return;
        }

        if (item.IsSpreadsheet)
        {
            OpenSpreadsheetViewer(item);
            return;
        }

        if (item.IsText)
        {
            OpenTextViewer(item);
            return;
        }

        if (!item.IsFolder)
            return;

        _path.Add(new FolderCrumb(item.Name, item.Id));
        await OpenFolderAsync(item.Id);
    }

    private void OpenTextViewer(ExplorerItem item)
        => Frame.Navigate(
            typeof(TextViewerPage),
            new TextViewerRequest(item.Id, item.Name, _path.ToList()));

    private void OpenSpreadsheetViewer(ExplorerItem item)
        => Frame.Navigate(
            typeof(SpreadsheetViewerPage),
            new SpreadsheetViewerRequest(item.Id, item.Name, _path.ToList()));

    private async void FolderBreadcrumb_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is not FolderCrumb crumb)
            return;

        var index = _path.FindIndex(item => item.FolderId == crumb.FolderId);
        if (index < 0)
            return;

        _path.RemoveRange(index + 1, _path.Count - index - 1);
        await OpenFolderAsync(crumb.FolderId);
    }

    // --- Контекстное меню ------------------------------------------------

    private void Item_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement element)
            return;

        var item = ResolveItem(element);
        if (item is null)
            return;

        var position = new Windows.Foundation.Point(0, 0);
        args.TryGetPosition(element, out position);

        ShowItemMenu(item, element, position);
        args.Handled = true;
    }

    private static ExplorerItem? ResolveItem(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is FrameworkElement { DataContext: ExplorerItem fromContext })
                return fromContext;

            if (source is GridViewItem { Content: ExplorerItem fromContainer })
                return fromContainer;

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void ShowItemMenu(ExplorerItem item, FrameworkElement target, Windows.Foundation.Point position)
    {
        var strings = Loc.Current;
        var flyout = new MenuFlyout();

        if (item.IsFolder)
        {
            flyout.Items.Add(CreateMenuItem(strings.Delete, () => DeleteFolderAsync(item)));
            flyout.Items.Add(CreateMenuItem(strings.Rename, () => RenameFolderAsync(item)));
        }
        else if (item.IsVideo)
        {
            flyout.Items.Add(CreateMenuItem(strings.Delete, () => DeleteVideoAsync(item)));
            flyout.Items.Add(CreateMenuItem(strings.Rename, () => RenameVideoAsync(item)));
            flyout.Items.Add(CreateMenuItem(strings.Download, () => DownloadVideoAsync(item)));
        }
        else
        {
            if (item.IsSpreadsheet || item.IsText)
            {
                flyout.Items.Add(CreateMenuItem(strings.Open, () =>
                {
                    if (item.IsSpreadsheet)
                        OpenSpreadsheetViewer(item);
                    else
                        OpenTextViewer(item);
                    return Task.CompletedTask;
                }));
            }

            flyout.Items.Add(CreateMenuItem(strings.Delete, () => DeleteFileAsync(item)));
            flyout.Items.Add(CreateMenuItem(strings.Rename, () => RenameFileAsync(item)));
            flyout.Items.Add(CreateMenuItem(strings.Download, () => DownloadFileAsync(item)));
        }

        flyout.ShowAt(target, new FlyoutShowOptions { Position = position });
    }

    private static MenuFlyoutItem CreateMenuItem(string text, Func<Task> action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += async (_, _) => await action();
        return item;
    }

    private Task RenameFileAsync(ExplorerItem item)
        => RenameItemAsync(Loc.Current.RenameFile, item.Name, name => AppServices.Api.UpdateFileAsync(item.Id, name));

    private Task RenameFolderAsync(ExplorerItem item)
        => RenameItemAsync(Loc.Current.RenameFolder, item.Name, name => AppServices.Api.UpdateFolderAsync(item.Id, name: name));

    private Task RenameVideoAsync(ExplorerItem item)
        => RenameItemAsync(Loc.Current.RenameVideo, item.Name, name => AppServices.Api.UpdateVideoAsync(item.Id, name));

    private async Task RenameItemAsync(string title, string currentName, Func<string, Task> renameApi)
    {
        var name = await PromptNameAsync(title, currentName);
        if (name is null || name == currentName)
            return;

        await RunAsync(async () =>
        {
            await renameApi(name);
            await ReloadCurrentAsync();
        });
    }

    private Task DeleteFileAsync(ExplorerItem item)
    {
        var strings = Loc.Current;
        return DeleteItemAsync(
            strings.DeleteFileTitle,
            strings.DeleteFileMessage(item.Name),
            () => AppServices.Api.DeleteFileAsync(item.Id));
    }

    private Task DeleteFolderAsync(ExplorerItem item)
    {
        var strings = Loc.Current;
        return DeleteItemAsync(
            strings.DeleteFolderTitle,
            strings.DeleteFolderMessage(item.Name),
            () => AppServices.Api.DeleteFolderAsync(item.Id));
    }

    private Task DeleteVideoAsync(ExplorerItem item)
    {
        var strings = Loc.Current;
        return DeleteItemAsync(
            strings.DeleteVideoTitle,
            strings.DeleteVideoMessage(item.Name),
            () => AppServices.Api.DeleteVideoAsync(item.Id));
    }

    private async Task DeleteItemAsync(string title, string message, Func<Task> deleteApi)
    {
        var confirmed = await ConfirmAsync(title, message, Loc.Current.Delete);
        if (!confirmed)
            return;

        await RunAsync(async () =>
        {
            await deleteApi();
            await ReloadCurrentAsync();
        });
    }

    private async Task DownloadFileAsync(ExplorerItem item)
    {
        await RunAsync(async () =>
        {
            var path = await FileSaver.DownloadAsync(item.Id, item.Name);
            if (path is not null)
                ShowSuccess(Loc.Current.FileSaved, path);
        });
    }

    private async Task DownloadVideoAsync(ExplorerItem item)
    {
        await RunAsync(async () =>
        {
            var path = await FileSaver.DownloadVideoAsync(item.Id, item.Name);
            if (path is not null)
                ShowSuccess(Loc.Current.VideoSaved, path);
        });
    }

    // --- Добавление папки и файла ----------------------------------------

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var folderId = CurrentFolderId;
        if (folderId is null)
            return;

        var strings = Loc.Current;

        var nameBox = new TextBox
        {
            Header = strings.FolderName,
            PlaceholderText = strings.FolderNamePlaceholder,
            MaxLength = 255,
        };

        var errorBar = new InfoBar
        {
            IsClosable = false,
            IsOpen = false,
            Severity = InfoBarSeverity.Error,
        };

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(errorBar);
        content.Children.Add(nameBox);

        var dialog = new ContentDialog
        {
            Title = strings.NewFolder,
            Content = content,
            PrimaryButtonText = strings.AddEntry,
            CloseButtonText = strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = false,
            XamlRoot = XamlRoot,
        };

        nameBox.TextChanged += (_, _) =>
        {
            dialog.IsPrimaryButtonEnabled = nameBox.Text.Trim().Length > 0;
            errorBar.IsOpen = false;
        };

        nameBox.Loaded += (_, _) => nameBox.Focus(FocusState.Programmatic);

        dialog.PrimaryButtonClick += async (sender, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var name = nameBox.Text.Trim();
                if (name.Length == 0)
                {
                    args.Cancel = true;
                    return;
                }

                dialog.IsPrimaryButtonEnabled = false;
                await AppServices.Api.CreateFolderAsync(name, folderId.Value);
                await ReloadCurrentAsync();
            }
            catch (ApiException ex)
            {
                errorBar.Message = ex.Message;
                errorBar.IsOpen = true;
                args.Cancel = true;
            }
            catch (Exception ex)
            {
                errorBar.Message = strings.CreateFolderFailed(ex.Message);
                errorBar.IsOpen = true;
                args.Cancel = true;
            }
            finally
            {
                dialog.IsPrimaryButtonEnabled = true;
                deferral.Complete();
            }
        };

        await dialog.ShowAsync();
    }

    private async void AddFile_Click(object sender, RoutedEventArgs e)
    {
        var folderId = CurrentFolderId;
        if (folderId is null)
            return;

        try
        {
            await LoadTypesAsync();
            var videoExtensions = await LoadVideoTypesAsync();
            var allExtensions = _types.Select(type => type.Value)
                .Concat(videoExtensions)
                .Distinct(StringComparer.OrdinalIgnoreCase);

            var file = await PickUploadFileAsync(PickerLocationId.DocumentsLibrary, allExtensions);
            if (file is null)
                return;

            if (IsVideoExtension(file.Name, videoExtensions))
                await UploadVideoAsync(folderId.Value, file.Name, file.Path);
            else
                await UploadIntoFolderAsync(folderId.Value, file.Name, file.Path);
        }
        catch (Exception ex)
        {
            ShowError(Loc.Current.FilePickFailed(ex.Message));
        }
    }

    private static bool IsVideoExtension(string fileName, IReadOnlyList<string> videoExtensions)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.');
        if (string.IsNullOrEmpty(ext))
            return false;

        if (videoExtensions.Count > 0)
            return videoExtensions.Any(v => string.Equals(v.TrimStart('.'), ext, StringComparison.OrdinalIgnoreCase));

        // Запасной список на случай, если справочник видеотипов не загрузился.
        return ext.ToLowerInvariant() is "mp4" or "m4v" or "webm" or "mov" or "mkv" or "avi";
    }

    /// <summary>
    /// Диалог выбора загружаемого файла. <paramref name="extensions"/> — расширения без точки;
    /// пустой список означает «любые файлы».
    /// </summary>
    private static async Task<StorageFile?> PickUploadFileAsync(
        PickerLocationId location,
        IEnumerable<string> extensions)
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = location,
            ViewMode = PickerViewMode.List,
        };

        var filtered = false;
        foreach (var extension in extensions)
        {
            picker.FileTypeFilter.Add("." + extension.TrimStart('.'));
            filtered = true;
        }

        if (!filtered)
            picker.FileTypeFilter.Add("*");

        FileSaver.InitializeWithWindow(picker);

        return await picker.PickSingleFileAsync();
    }

    private async Task<IReadOnlyList<string>> LoadVideoTypesAsync()
    {
        if (_videoTypes.Count == 0)
        {
            try
            {
                _videoTypes = await AppServices.Api.GetVideoTypesAsync() ?? [];
            }
            catch (Exception)
            {
                _videoTypes = [];
            }
        }

        return _videoTypes.Select(type => type.Value).ToList();
    }

    private Task UploadIntoFolderAsync(Guid folderId, string name, string path)
        => RunUploadAsync(
            async progress =>
            {
                var uploaded = await AppServices.Api.UploadFileAsync(name, path, progress);

                // Файл создаётся отдельно, поэтому добавляем его в текущую папку;
                // PATCH принимает полный набор файлов папки.
                var folder = await AppServices.Api.GetFolderAsync(folderId);
                var fileIds = (folder.Files ?? []).Select(file => file.Id).Append(uploaded.Id).ToList();
                await AppServices.Api.UpdateFolderAsync(folderId, fileIds: fileIds);
            },
            Loc.Current.FileUploadFailed);

    private Task UploadVideoAsync(Guid folderId, string name, string path)
        => RunUploadAsync(
            async progress =>
            {
                var video = await AppServices.Api.UploadVideoAsync(name, path, folderId, progress);
                ShowSuccess(Loc.Current.VideoUploaded(video.Name));
            },
            Loc.Current.VideoUploadFailed);

    private async Task RunUploadAsync(Func<IProgress<double>, Task> uploadAction, Func<string, string> formatError)
    {
        SetUploading(true);
        ErrorBar.IsOpen = false;

        try
        {
            var progress = new Progress<double>(value => UploadProgress.Value = value * 100);
            await uploadAction(progress);
            await ReloadCurrentAsync();
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(formatError(ex.Message));
        }
        finally
        {
            SetUploading(false);
        }
    }

    private void SetUploading(bool uploading)
    {
        AddButton.IsEnabled = !uploading;
        UploadProgress.Visibility = uploading ? Visibility.Visible : Visibility.Collapsed;
        if (uploading)
            UploadProgress.Value = 0;
    }

    // --- Диалоги и вспомогательное ---------------------------------------

    private async Task<string?> PromptNameAsync(string title, string current)
    {
        var strings = Loc.Current;

        var box = new TextBox
        {
            Text = current,
            Header = strings.NewName,
            SelectionStart = 0,
        };

        var dialog = new ContentDialog
        {
            Title = title,
            Content = box,
            PrimaryButtonText = strings.Save,
            CloseButtonText = strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        var value = box.Text.Trim();
        return value.Length == 0 ? null : value;
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryText,
            CloseButtonText = Loc.Current.Cancel,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async Task RunAsync(Func<Task> action)
    {
        ErrorBar.IsOpen = false;

        try
        {
            await action();
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorBar.Severity = InfoBarSeverity.Error;
        ErrorBar.Title = Loc.Current.Error;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }

    private void ShowSuccess(string title, string? message = null)
    {
        ErrorBar.Severity = InfoBarSeverity.Success;
        ErrorBar.Title = title;
        ErrorBar.Message = message ?? string.Empty;
        ErrorBar.IsOpen = true;
    }
}
