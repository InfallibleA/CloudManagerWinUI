using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;

namespace CloudManagerWinUIApp.Pages;

/// <summary>Элемент сетки файлового менеджера: папка или файл.</summary>
public sealed class ExplorerItem
{
    private ExplorerItem(Guid id, string name, bool isFolder)
    {
        Id = id;
        Name = name;
        IsFolder = isFolder;
    }

    public Guid Id { get; }

    public string Name { get; }

    public bool IsFolder { get; }

    public string Glyph => ExplorerIcons.GlyphFor(IsFolder);

    public SolidColorBrush IconBrush => ExplorerIcons.BrushFor(IsFolder);

    public string AutomationName => IsFolder
        ? Loc.Current.FolderAutomation(Name)
        : Loc.Current.FileAutomation(Name);

    public static ExplorerItem FromFolder(FolderShortDto dto) => new(dto.Id, dto.Name, isFolder: true);

    public static ExplorerItem FromFile(FileShortDto dto) => new(dto.Id, dto.Name, isFolder: false);
}

/// <summary>Сегмент пути в «хлебных крошках».</summary>
public sealed class FolderCrumb(string name, Guid folderId)
{
    public string Name { get; } = name;

    public Guid FolderId { get; } = folderId;

    public override string ToString() => Name;
}

/// <summary>
/// Файловый менеджер пользователя: сетка папок и файлов текущей папки.
/// Показываются только папки текущего пользователя (фильтр <c>creator_id</c>)
/// и файлы внутри его папок.
/// </summary>
public sealed partial class FilesPage : Page, IFrameBackHandler
{
    private const int PageSize = 100;

    private readonly List<FolderCrumb> _path = [];
    private IReadOnlyList<FileTypeDto> _types = [];

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

        if (AppServices.Auth.IsAuthenticated)
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
            ItemsGrid.Items.Add(ExplorerItem.FromFile(file));

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

    // --- Навигация -------------------------------------------------------

    private async void ItemsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ExplorerItem { IsFolder: true } folder)
            return;

        _path.Add(new FolderCrumb(folder.Name, folder.Id));
        await OpenFolderAsync(folder.Id);
    }

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
        else
        {
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

    private async Task RenameFileAsync(ExplorerItem item)
    {
        var name = await PromptNameAsync(Loc.Current.RenameFile, item.Name);
        if (name is null || name == item.Name)
            return;

        await RunAsync(async () =>
        {
            await AppServices.Api.UpdateFileAsync(item.Id, name);
            await ReloadCurrentAsync();
        });
    }

    private async Task RenameFolderAsync(ExplorerItem item)
    {
        var name = await PromptNameAsync(Loc.Current.RenameFolder, item.Name);
        if (name is null || name == item.Name)
            return;

        await RunAsync(async () =>
        {
            await AppServices.Api.UpdateFolderAsync(item.Id, name: name);
            await ReloadCurrentAsync();
        });
    }

    private async Task DeleteFileAsync(ExplorerItem item)
    {
        var strings = Loc.Current;
        var confirmed = await ConfirmAsync(
            strings.DeleteFileTitle,
            strings.DeleteFileMessage(item.Name),
            strings.Delete);

        if (!confirmed)
            return;

        await RunAsync(async () =>
        {
            await AppServices.Api.DeleteFileAsync(item.Id);
            await ReloadCurrentAsync();
        });
    }

    private async Task DeleteFolderAsync(ExplorerItem item)
    {
        var strings = Loc.Current;
        var confirmed = await ConfirmAsync(
            strings.DeleteFolderTitle,
            strings.DeleteFolderMessage(item.Name),
            strings.Delete);

        if (!confirmed)
            return;

        await RunAsync(async () =>
        {
            await AppServices.Api.DeleteFolderAsync(item.Id);
            await ReloadCurrentAsync();
        });
    }

    private async Task DownloadFileAsync(ExplorerItem item)
    {
        await RunAsync(async () =>
        {
            var path = await FileSaver.DownloadAsync(item.Id, item.Name);
            if (path is not null)
            {
                ErrorBar.Severity = InfoBarSeverity.Success;
                ErrorBar.Title = Loc.Current.FileSaved;
                ErrorBar.Message = path;
                ErrorBar.IsOpen = true;
            }
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
            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                ViewMode = PickerViewMode.List,
            };

            if (_types.Count > 0)
            {
                foreach (var type in _types)
                    picker.FileTypeFilter.Add("." + type.Value);
            }
            else
            {
                picker.FileTypeFilter.Add("*");
            }

            FileSaver.InitializeWithWindow(picker);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            await UploadIntoFolderAsync(folderId.Value, file.Name, file.Path);
        }
        catch (Exception ex)
        {
            ShowError(Loc.Current.FilePickFailed(ex.Message));
        }
    }

    private async Task UploadIntoFolderAsync(Guid folderId, string name, string path)
    {
        AddButton.IsEnabled = false;
        UploadProgress.Visibility = Visibility.Visible;
        UploadProgress.Value = 0;
        ErrorBar.IsOpen = false;

        try
        {
            var progress = new Progress<double>(value => UploadProgress.Value = value * 100);
            var uploaded = await AppServices.Api.UploadFileAsync(name, path, progress);

            // Файл создаётся отдельно, поэтому добавляем его в текущую папку;
            // PATCH принимает полный набор файлов папки.
            var folder = await AppServices.Api.GetFolderAsync(folderId);
            var fileIds = (folder.Files ?? []).Select(file => file.Id).Append(uploaded.Id).ToList();
            await AppServices.Api.UpdateFolderAsync(folderId, fileIds: fileIds);

            await ReloadCurrentAsync();
        }
        catch (ApiException ex)
        {
            ShowError(ex.Message);
        }
        catch (Exception ex)
        {
            ShowError(Loc.Current.FileUploadFailed(ex.Message));
        }
        finally
        {
            AddButton.IsEnabled = true;
            UploadProgress.Visibility = Visibility.Collapsed;
        }
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
}
