using System.Globalization;
using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace CloudManagerWinUIApp.Pages;

/// <summary>Строка таблицы пользователей админ-панели.</summary>
public sealed class AdminUserRow(UserResponse dto)
{
    public Guid Id { get; } = dto.Id;

    public string Login { get; } = dto.Login;

    public bool IsAdmin { get; } = string.Equals(dto.Role, "admin", StringComparison.OrdinalIgnoreCase);

    /// <summary>Аккаунт заблокирован администратором (<c>is_blocked</c> в ответе API).</summary>
    public bool IsBlocked { get; } = dto.IsBlocked;

    /// <summary>Значок роли: admin — «щит», client — «человек».</summary>
    public string Glyph => IsAdmin ? "\uE7EF" : "\uE77B";

    /// <summary>Роль словами текущего языка интерфейса.</summary>
    public string RoleText => dto.Role.ToLowerInvariant() switch
    {
        "admin" => Loc.Current.RoleAdmin,
        "client" => Loc.Current.RoleClient,
        _ => Loc.Current.RoleUnknown,
    };

    /// <summary>Статус блокировки словами текущего языка интерфейса.</summary>
    public string StatusText => IsBlocked ? Loc.Current.StatusBlocked : Loc.Current.StatusActive;

    /// <summary>Замок у заблокированного; у активного значка нет.</summary>
    public Visibility LockVisibility => IsBlocked ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Текст пункта/подсказки действия по текущему статусу.</summary>
    public string BlockActionText => IsBlocked ? Loc.Current.Unblock : Loc.Current.Block;

    /// <summary>Значок действия: закрытый замок — заблокировать, открытый — разблокировать.</summary>
    public string BlockGlyph => IsBlocked ? "\uE785" : "\uE72E";

    /// <summary>Имя кнопки блокировки для средств доступности (экранный диктор).</summary>
    public string BlockAutomation => Loc.Current.BlockActionAutomation(Login);

    public string CreatedText { get; } = dto.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>Имя для средств доступности (экранный диктор).</summary>
    public string AutomationName => Loc.Current.OpenUserFiles(Login);
}

/// <summary>
/// Строка вкладки «Файлы»: файл любого пользователя (значок — имя — пользователь —
/// тип — размер — дата создания). Значок и цвет берутся из <see cref="ExplorerIcons"/> —
/// те же, что у обычного файлового менеджера. Действий над файлом во вкладке нет,
/// поэтому <c>id</c> строке не нужен.
/// </summary>
public sealed class AdminFileRow(FileDto dto)
{
    public string Name { get; } = dto.Name;

    /// <summary>
    /// Владелец для колонки «Пользователь»: <c>creator_login</c>, при его отсутствии
    /// <c>creator_id</c>, а если нет и его — прочерк.
    /// </summary>
    public string OwnerText { get; } = string.IsNullOrWhiteSpace(dto.CreatorLogin)
        ? dto.CreatorId?.ToString() ?? Loc.Current.NoValue
        : dto.CreatorLogin;

    public string Glyph => ExplorerIcons.FileGlyph;

    public SolidColorBrush IconBrush => ExplorerIcons.BrushFor(ExplorerItemKind.File);

    /// <summary>Тип файла так, как его называет сервер (<c>value</c>).</summary>
    public string TypeText { get; } = dto.Type;

    public string SizeText { get; } = Loc.Current.FormatSize(dto.Size);

    /// <summary>
    /// Дата создания в локальном времени. В <c>FileDto</c> поле не помечено
    /// необязательным, поэтому отсутствующую дату видно как <c>default</c> —
    /// для неё, как и в остальных таблицах, показываем прочерк.
    /// </summary>
    public string CreatedText { get; } = dto.CreatedAt == default
        ? Loc.Current.NoValue
        : dto.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);

    /// <summary>Имя для средств доступности (экранный диктор): файл и его владелец.</summary>
    public string AutomationName => Loc.Current.FileOwnerAutomation(Name, OwnerText);
}

/// <summary>
/// Строка таблицы содержимого чужой папки (значок — имя — размер — дата создания).
/// Значки и цвета берутся из <see cref="ExplorerIcons"/> — те же, что у обычного
/// файлового менеджера (<see cref="ExplorerItem"/>).
/// </summary>
public sealed class AdminExplorerRow
{
    private AdminExplorerRow(Guid id, string name, bool isFolder, string sizeText, string createdText)
    {
        Id = id;
        Name = name;
        IsFolder = isFolder;
        SizeText = sizeText;
        CreatedText = createdText;
    }

    public Guid Id { get; }

    public string Name { get; }

    public bool IsFolder { get; }

    public string Glyph => ExplorerIcons.GlyphFor(IsFolder ? ExplorerItemKind.Folder : ExplorerItemKind.File);

    public SolidColorBrush IconBrush => ExplorerIcons.BrushFor(IsFolder ? ExplorerItemKind.Folder : ExplorerItemKind.File);

    /// <summary>Размер файла; у папки размера нет — прочерк.</summary>
    public string SizeText { get; }

    /// <summary>Дата создания; бекенд отдаёт её не для всех записей — тогда прочерк.</summary>
    public string CreatedText { get; }

    /// <summary>Имя для средств доступности (экранный диктор).</summary>
    public string AutomationName => IsFolder
        ? Loc.Current.FolderAutomation(Name)
        : Loc.Current.FileAutomation(Name);

    public static AdminExplorerRow FromFolder(FolderShortDto dto) => new(
        dto.Id,
        dto.Name,
        isFolder: true,
        sizeText: Loc.Current.NoValue,
        createdText: FormatDate(dto.CreatedAt));

    public static AdminExplorerRow FromFile(FileShortDto dto) => new(
        dto.Id,
        dto.Name,
        isFolder: false,
        sizeText: Loc.Current.FormatSize(dto.Size),
        createdText: FormatDate(dto.CreatedAt));

    private static string FormatDate(DateTimeOffset? value)
        => value is null
            ? Loc.Current.NoValue
            : value.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
}

/// <summary>Вариант выбора уже загруженного изображения (элемент ComboBox).</summary>
public sealed class AdminMediaOption(FileDto dto)
{
    public Guid Id { get; } = dto.Id;

    public string Text { get; } = $"{dto.Name} ({Loc.Current.FormatSize(dto.Size)})";
}

/// <summary>
/// Админ-панель: список пользователей с просмотром их папок и файлов и создание
/// новостей с изображением (<c>media_id</c>). Страница доступна только роли admin;
/// при выходе или потере роли интерфейс переключается в состояние «нужны права».
/// </summary>
public sealed partial class AdminPage : Page
{
    private const int UsersPageSize = 25;
    private const int UserFolderPageSize = 100;
    private const int MediaFilesPageSize = 100;

    /// <summary>Размер страницы вкладки «Файлы» (сервер разрешает 1..100).</summary>
    private const int AdminFilesPageSize = 50;

    /// <summary>Размер страницы списка пользователей для фильтра (сервер разрешает до 100).</summary>
    private const int FileUsersPageSize = 100;

    /// <summary>
    /// Предел аккаунтов, подгружаемых в фильтр «Пользователь», — 5 страниц по 100.
    /// При большем числе пользователей список остаётся неполным, чтобы открытие
    /// вкладки не превращалось в долгую серию запросов.
    /// </summary>
    private const int FileUsersMaxAccounts = 500;

    /// <summary>Типовые типы изображений, если справочник <c>api/files/types</c> недоступен.</summary>
    private static readonly string[] FallbackImageTypes = ["png", "jpg", "jpeg", "gif", "webp", "avif"];

    private readonly List<FolderCrumb> _path = [];
    private readonly List<AdminUserRow> _users = [];
    private readonly List<AdminFileRow> _allFiles = [];

    private IReadOnlyList<FileTypeDto> _imageTypes = [];
    private AdminUserRow? _openedUser;
    private string? _roleFilter;
    private Guid? _mediaId;
    private string _activeSearch = string.Empty;
    private string? _activeRole;
    private bool _changingRole;
    private bool _uploadingMedia;
    private bool _creatingNews;
    private bool _blocking;
    private int _loadedPages;

    // Фильтры вкладки «Файлы»: выбранные в интерфейсе и «зафиксированные» на время серии
    // запросов, чтобы «Показать ещё» продолжало ту выборку, которая на экране.
    private Guid? _fileCreator;
    private string? _fileType;
    private Guid? _activeFileCreator;
    private string? _activeFileLogin;
    private string? _activeFileType;
    private string _activeFileName = string.Empty;
    private int _filesLoadedPages;
    private bool _filesLoaded;
    private bool _filesFiltersLoaded;
    private bool _changingFileFilters;

    /// <summary>Поколение запроса пользователей: применяется ответ только последнего.</summary>
    private int _usersRequest;

    /// <summary>Поколение запроса содержимого папки (защита от гонки при быстрых кликах).</summary>
    private int _explorerRequest;

    /// <summary>Поколение запроса вкладки «Файлы»: применяется ответ только последнего.</summary>
    private int _filesRequest;

    public AdminPage()
    {
        InitializeComponent();
        InitializeRoleFilter();
        ResetFileFilters();
    }

    /// <summary>Строки текущего языка (страница пересоздаётся при смене языка).</summary>
    public Strings L { get; } = Loc.Current;

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        AppServices.Auth.StateChanged += OnAuthStateChanged;
        UpdateAuthState();

        // Запросы к API — только для админа: иначе первый же 401 разлогинит пользователя.
        if (AppServices.Auth.IsAdmin)
        {
            _ = LoadUsersPageAsync(reset: true);
            _ = LoadImageTypesAsync();
        }
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
        var wasAdmin = AdminRoot.Visibility == Visibility.Visible;
        UpdateAuthState();

        if (!AppServices.Auth.IsAdmin)
        {
            ClearAll();
            return;
        }

        if (!wasAdmin)
        {
            _ = LoadUsersPageAsync(reset: true);

            // Если открыта вкладка «Файлы», после входа ей тоже нужны данные:
            // SelectionChanged повторно не сработает.
            if (AdminTabs.SelectedItem is TabViewItem { Tag: "files" })
                _ = ShowFilesTabAsync();
        }
    }

    private void UpdateAuthState()
    {
        var isAdmin = AppServices.Auth.IsAdmin;

        AdminRoot.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        ForbiddenPanel.Visibility = isAdmin ? Visibility.Collapsed : Visibility.Visible;

        // Вошедшему пользователю без прав кнопка входа не поможет.
        SignInButton.Visibility = AppServices.Auth.IsAuthenticated ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearAll()
    {
        // Летящие ответы больше не должны применяться к пустому экрану.
        _usersRequest++;
        _explorerRequest++;
        _filesRequest++;

        _users.Clear();
        _loadedPages = 0;
        _activeSearch = string.Empty;
        _openedUser = null;
        UsersList.Items.Clear();
        UsersCounter.Text = string.Empty;
        MoreUsersButton.Visibility = Visibility.Collapsed;
        UsersEmptyText.Visibility = Visibility.Collapsed;
        UsersListPanel.Visibility = Visibility.Visible;
        ExplorerPanel.Visibility = Visibility.Collapsed;
        ResetExplorer();

        // Вкладка «Файлы»: при следующем показе данные и справочники фильтров
        // загружаются заново (у нового аккаунта может быть другая роль и другой доступ).
        _allFiles.Clear();
        _filesLoadedPages = 0;
        _filesLoaded = false;
        _filesFiltersLoaded = false;
        FilesList.Items.Clear();
        FilesCounter.Text = string.Empty;
        MoreFilesButton.Visibility = Visibility.Collapsed;
        FilesEmptyText.Visibility = Visibility.Collapsed;
        FilesScopeTitle.Text = L.AllFiles;
        ResetFileFilters();

        MediaFilesCombo.ItemsSource = null;
        MediaFilesCombo.Visibility = Visibility.Collapsed;
        ResetMediaSelection();

        ErrorBar.IsOpen = false;
        SetBusy(false);
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
        => await Dialogs.AuthDialog.ShowAsync(Dialogs.AuthMode.Login);

    // --- Вкладка «Пользователи»: список -----------------------------------

    private void InitializeRoleFilter()
    {
        _changingRole = true;
        try
        {
            RoleFilter.Items.Clear();
            RoleFilter.Items.Add(new ComboBoxItem { Content = L.RoleAll, Tag = null });
            RoleFilter.Items.Add(new ComboBoxItem { Content = L.RoleAdmin, Tag = "admin" });
            RoleFilter.Items.Add(new ComboBoxItem { Content = L.RoleClient, Tag = "client" });
            RoleFilter.SelectedIndex = 0;
        }
        finally
        {
            _changingRole = false;
        }
    }

    /// <summary>
    /// Догружает страницу пользователей; <paramref name="reset"/> — начать с первой.
    /// Фильтр запоминается на время серии запросов, а ответ применяется только если
    /// его поколение последнее: новый поиск выигрывает у летящего запроса.
    /// </summary>
    private async Task LoadUsersPageAsync(bool reset)
    {
        if (!AppServices.Auth.IsAdmin)
            return;

        var generation = ++_usersRequest;

        if (reset)
        {
            // «Показать ещё» должно продолжать ту выборку, которая показана на экране.
            _activeSearch = UsersSearchBox.Text.Trim();
            _activeRole = _roleFilter;
            _loadedPages = 0;
            _users.Clear();
            UsersList.Items.Clear();
            UsersEmptyText.Visibility = Visibility.Collapsed;
            MoreUsersButton.Visibility = Visibility.Collapsed;
        }

        SetBusy(true);

        try
        {
            var page = _loadedPages + 1;
            var response = await AppServices.Api.GetUsersAsync(
                page,
                UsersPageSize,
                string.IsNullOrEmpty(_activeSearch) ? null : _activeSearch,
                _activeRole);

            // Ответ устарел (новый поиск/фильтр) или пользователь вышел из аккаунта.
            if (generation != _usersRequest || !AppServices.Auth.IsAdmin)
                return;

            foreach (var user in response.Items)
            {
                var row = new AdminUserRow(user);
                _users.Add(row);
                UsersList.Items.Add(row);
            }

            _loadedPages = page;
            UpdateUsersFooter(response.Count);
            UsersEmptyText.Visibility = _users.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ErrorBar.IsOpen = false;
        }
        catch (ApiException ex)
        {
            if (generation == _usersRequest)
                ShowError(DescribeApiError(ex));
        }
        catch (Exception ex)
        {
            if (generation == _usersRequest)
                ShowError(L.UsersLoadFailed(ex.Message));
        }
        finally
        {
            if (generation == _usersRequest)
                SetBusy(false);
        }
    }

    private void UpdateUsersFooter(int total)
    {
        UsersCounter.Text = L.UsersShown(_users.Count, total);
        MoreUsersButton.Visibility = _users.Count < total ? Visibility.Visible : Visibility.Collapsed;
    }

    private Task SearchUsersAsync()
    {
        UsersEmptyText.Visibility = Visibility.Collapsed;
        return LoadUsersPageAsync(reset: true);
    }

    private async void Search_Click(object sender, RoutedEventArgs e) => await SearchUsersAsync();

    private async void UsersSearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
            return;

        e.Handled = true;
        await SearchUsersAsync();
    }

    private async void RoleFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingRole)
            return;

        _roleFilter = (RoleFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        UsersEmptyText.Visibility = Visibility.Collapsed;
        await LoadUsersPageAsync(reset: true);
    }

    private async void MoreUsers_Click(object sender, RoutedEventArgs e)
        => await LoadUsersPageAsync(reset: false);

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (UsersListPanel.Visibility == Visibility.Visible)
        {
            await LoadUsersPageAsync(reset: true);
        }
        else if (_path.Count > 0)
        {
            await OpenFolderAsync(_path[^1].FolderId);
        }
        else if (_openedUser is not null)
        {
            // Корневой папки не было — пробуем найти её снова.
            await OpenUserAsync(_openedUser);
        }
    }

    private async void UsersList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is AdminUserRow row)
            await OpenUserAsync(row);
    }

    private async void OpenUser_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AdminUserRow row)
            await OpenUserAsync(row);
    }

    // --- Вкладка «Пользователи»: блокировка --------------------------------

    private async void ToggleBlock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AdminUserRow row)
            await ToggleBlockAsync(row);
    }

    /// <summary>
    /// Блокирует или разблокирует пользователя. Ответ ручки — карточка пользователя,
    /// поэтому строка обновляется ею, а список не перезагружается: прокрутка и фильтр
    /// остаются на месте. Повторная блокировка идемпотентна и тоже даёт <c>200</c>.
    /// </summary>
    private async Task ToggleBlockAsync(AdminUserRow row)
    {
        if (!AppServices.Auth.IsAdmin || _blocking)
            return;

        // Себя блокировать нельзя: сервер ответил бы 403, но запрос лучше не отправлять.
        if (row.Id == AppServices.Auth.User?.Id)
        {
            ShowError(L.CannotBlockSelf);
            return;
        }

        var blocking = !row.IsBlocked;
        if (blocking && !await ConfirmAsync(L.BlockUserTitle, L.BlockUserMessage(row.Login), L.Block))
            return;

        _blocking = true;
        SetBusy(true);
        ErrorBar.IsOpen = false;

        try
        {
            var updated = blocking
                ? await AppServices.Api.BlockUserAsync(row.Id)
                : await AppServices.Api.UnblockUserAsync(row.Id);

            // Пока шёл запрос, пользователь мог выйти — тогда обновлять уже нечего.
            if (!AppServices.Auth.IsAdmin)
                return;

            ApplyUserUpdate(updated);
        }
        catch (ApiException ex)
        {
            ShowError(DescribeApiError(ex));
        }
        catch (Exception ex)
        {
            ShowError(L.BlockUserFailed(ex.Message));
        }
        finally
        {
            _blocking = false;
            SetBusy(false);
        }
    }

    /// <summary>
    /// Заменяет строку таблицы ответом сервера. Список пользователей не перезагружается,
    /// поэтому выделение, прокрутка и фильтр поиска сохраняются.
    /// </summary>
    private void ApplyUserUpdate(UserResponse updated)
    {
        var index = _users.FindIndex(user => user.Id == updated.Id);
        if (index < 0)
            return;

        var row = new AdminUserRow(updated);
        _users[index] = row;
        UsersList.Items[index] = row;
    }

    // --- Вкладка «Пользователи»: содержимое чужой папки -------------------

    /// <summary>
    /// Открывает корневую папку выбранного пользователя. Корневой папки может не быть
    /// (аккаунты, созданные до её появления) — тогда показывается сообщение.
    /// Быстрый повторный клик по другому пользователю отменяет предыдущий запрос.
    /// </summary>
    private async Task OpenUserAsync(AdminUserRow user)
    {
        if (!AppServices.Auth.IsAdmin)
            return;

        _openedUser = user;
        UsersListPanel.Visibility = Visibility.Collapsed;
        ExplorerPanel.Visibility = Visibility.Visible;
        ResetExplorer();
        ExplorerTitle.Text = L.UserContentTitle(user.Login);
        AutomationProperties.SetName(ExplorerList, L.UserContentTitle(user.Login));

        var generation = ++_explorerRequest;
        SetBusy(true);
        ErrorBar.IsOpen = false;

        try
        {
            var roots = await AppServices.Api.GetFoldersAsync(
                pageSize: UserFolderPageSize,
                rootOnly: true,
                creatorId: user.Id);

            if (generation != _explorerRequest || !AppServices.Auth.IsAdmin)
                return;

            var root = roots.Items.FirstOrDefault(folder => folder.IsRoot) ?? roots.Items.FirstOrDefault();
            if (root is null)
            {
                ExplorerNoRootText.Text = L.UserNoRootFolder(user.Login);
                ExplorerNoRootText.Visibility = Visibility.Visible;
                return;
            }

            await LoadFolderAsync(
                root.Id,
                generation,
                push: new FolderCrumb(root.Name, root.Id));
        }
        catch (ApiException ex)
        {
            if (generation == _explorerRequest)
                ShowError(DescribeApiError(ex));
        }
        catch (Exception ex)
        {
            if (generation == _explorerRequest)
                ShowError(L.UserContentFailed(ex.Message));
        }
        finally
        {
            if (generation == _explorerRequest)
                SetBusy(false);
        }
    }

    /// <summary>
    /// Открывает папку. Крошка пути добавляется (или срезается) только после успешной
    /// загрузки, поэтому breadcrumb и список не расходятся при 403/404.
    /// </summary>
    private async Task OpenFolderAsync(Guid folderId, FolderCrumb? push = null, int? truncateTo = null)
    {
        var generation = ++_explorerRequest;
        SetBusy(true);
        ErrorBar.IsOpen = false;

        try
        {
            await LoadFolderAsync(folderId, generation, push, truncateTo);
        }
        catch (ApiException ex)
        {
            if (generation == _explorerRequest)
                ShowError(DescribeApiError(ex));
        }
        catch (Exception ex)
        {
            if (generation == _explorerRequest)
                ShowError(L.FolderOpenFailed(ex.Message));
        }
        finally
        {
            if (generation == _explorerRequest)
                SetBusy(false);
        }
    }

    /// <summary>Загружает содержимое папки и применяет его, только если запрос ещё актуален.</summary>
    private async Task LoadFolderAsync(
        Guid folderId,
        int generation,
        FolderCrumb? push = null,
        int? truncateTo = null)
    {
        var folder = await AppServices.Api.GetFolderAsync(folderId);

        if (generation != _explorerRequest)
            return;

        if (truncateTo is int index && index >= 0 && index < _path.Count)
            _path.RemoveRange(index + 1, _path.Count - index - 1);

        if (push is not null)
            _path.Add(push);

        ExplorerList.Items.Clear();
        foreach (var child in folder.Children ?? [])
            ExplorerList.Items.Add(AdminExplorerRow.FromFolder(child));
        foreach (var file in folder.Files ?? [])
            ExplorerList.Items.Add(AdminExplorerRow.FromFile(file));

        ExplorerBreadcrumb.ItemsSource = _path.ToList();
        ExplorerEmptyText.Visibility = ExplorerList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ExplorerNoRootText.Visibility = Visibility.Collapsed;
    }

    private void ResetExplorer()
    {
        _path.Clear();
        ExplorerList.Items.Clear();
        ExplorerBreadcrumb.ItemsSource = Array.Empty<FolderCrumb>();
        ExplorerEmptyText.Visibility = Visibility.Collapsed;
        ExplorerNoRootText.Visibility = Visibility.Collapsed;
    }

    /// <summary>Возврат к списку пользователей: незавершённый переход в папку отменяется.</summary>
    private void BackToUsers_Click(object sender, RoutedEventArgs e)
    {
        // Ответ брошенного запроса не должен ни наполнять скрытую панель/крошки,
        // ни показывать ошибку поверх списка; busy снимаем сами (finally его уже не тронет).
        _explorerRequest++;
        SetBusy(false);

        _openedUser = null;
        ExplorerPanel.Visibility = Visibility.Collapsed;
        UsersListPanel.Visibility = Visibility.Visible;
        ResetExplorer();
        ExplorerTitle.Text = string.Empty;
        ErrorBar.IsOpen = false;
    }

    private async void ExplorerList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not AdminExplorerRow { IsFolder: true } folder)
            return;

        await OpenFolderAsync(folder.Id, push: new FolderCrumb(folder.Name, folder.Id));
    }

    private async void ExplorerBreadcrumb_ItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        if (args.Item is not FolderCrumb crumb)
            return;

        var index = _path.FindIndex(item => item.FolderId == crumb.FolderId);
        if (index < 0)
            return;

        await OpenFolderAsync(crumb.FolderId, truncateTo: index);
    }

    // --- Вкладка «Файлы»: все файлы с фильтрами ----------------------------

    /// <summary>
    /// Вкладка «Файлы» загружает данные при первом показе: открытие админ-панели не
    /// должно тратить запросы на скрытую вкладку (у сервера есть лимит на запросы).
    /// </summary>
    private async void AdminTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Событие приходит и во время создания XAML, до готовности панелей.
        if (AdminTabs?.SelectedItem is not TabViewItem { Tag: "files" })
            return;

        await ShowFilesTabAsync();
    }

    private async Task ShowFilesTabAsync()
    {
        if (!AppServices.Auth.IsAdmin)
            return;

        await LoadFilesFiltersAsync();

        if (_filesLoaded || !AppServices.Auth.IsAdmin)
            return;

        _filesLoaded = true;
        await LoadAllFilesPageAsync(reset: true);
    }

    /// <summary>
    /// Справочники фильтров: типы файлов (<c>GET api/files/types</c>) и пользователи
    /// страницами по 100 (<c>GET api/auth/users?page=N&amp;page_size=100</c>), пока не
    /// набрано <c>count</c> или <see cref="FileUsersMaxAccounts"/> аккаунтов. Ошибки не
    /// критичны: уже загруженные данные остаются в ComboBox, InfoBar не открываем,
    /// а неполную загрузку повторим при следующем показе вкладки.
    /// </summary>
    private async Task LoadFilesFiltersAsync()
    {
        if (_filesFiltersLoaded || !AppServices.Auth.IsAdmin)
            return;

        _filesFiltersLoaded = true;

        var types = Array.Empty<FileTypeDto>();
        var typesLoaded = false;
        try
        {
            // Пустое тело ответа даёт null через SendJsonAsync: справочник тогда
            // остаётся пустым, а не роняет вызывающий async void-обработчик.
            var loadedTypes = await AppServices.Api.GetFileTypesAsync();
            if (loadedTypes is not null)
            {
                types = loadedTypes;
                typesLoaded = true;
            }
        }
        catch (ApiException)
        {
            // Без справочника остаётся вариант «Все типы».
        }
        catch (Exception)
        {
            // Сеть или таймаут: так же оставляем «Все типы».
        }

        if (!AppServices.Auth.IsAdmin)
            return;

        var users = new List<UserResponse>();
        var userIds = new HashSet<Guid>();
        var usersLoaded = false;
        var maxPages = (FileUsersMaxAccounts + FileUsersPageSize - 1) / FileUsersPageSize;
        try
        {
            for (var page = 1; page <= maxPages && users.Count < FileUsersMaxAccounts; page++)
            {
                var response = await AppServices.Api.GetUsersAsync(page, FileUsersPageSize);

                if (!AppServices.Auth.IsAdmin)
                    return;

                foreach (var user in response.Items)
                {
                    if (users.Count >= FileUsersMaxAccounts)
                        break;

                    // Offset-пагинация может сдвинуть записи между запросами:
                    // дубликаты пропускаем, порядок сервера сохраняем.
                    if (userIds.Add(user.Id))
                        users.Add(user);
                }

                // Сервер отдал всех, либо достигнут предел числа аккаунтов.
                if (users.Count >= response.Count || users.Count >= FileUsersMaxAccounts)
                {
                    usersLoaded = true;
                    break;
                }

                // Пустая страница при непустом count: дальше листать нечего.
                if (response.Items.Count == 0)
                    break;
            }
        }
        catch (ApiException)
        {
            // Первые страницы уже загружены — показываем их без модальной ошибки.
        }
        catch (Exception)
        {
            // Сеть или таймаут: используем то, что успело загрузиться.
        }

        if (!AppServices.Auth.IsAdmin)
            return;

        if (typesLoaded)
        {
            // Если выбранный тип не попал в новую страницу справочника, не перестраиваем
            // ComboBox: иначе он покажет «Все типы», а таблица останется с прежним фильтром.
            var selectedType = (FileTypeFilter.SelectedItem as ComboBoxItem)?.Tag as string;
            if (selectedType is null ||
                types.Any(type => string.Equals(type.Value, selectedType, StringComparison.Ordinal)))
            {
                FillFileTypeFilter(types);
            }
        }

        // Если первая страница не загрузилась, не трогаем ComboBox:
        // в нём остаётся «Все пользователи» из ResetFileFilters.
        if (users.Count > 0 || usersLoaded)
        {
            // Выбранного пользователя может не быть в частично загруженном списке:
            // сохраняем старые элементы, чтобы ComboBox и фильтр таблицы не разошлись.
            var selectedUser = (FileUserFilter.SelectedItem as ComboBoxItem)?.Tag as Guid?;
            if (selectedUser is not Guid id || users.Any(user => user.Id == id))
                FillFileUserFilter(users);
        }

        if (!typesLoaded || !usersLoaded)
            _filesFiltersLoaded = false;
    }

    /// <summary>«Все типы» + справочник; показываем <c>value</c> без преобразований.</summary>
    private void FillFileTypeFilter(IReadOnlyList<FileTypeDto> types)
    {
        var selected = (FileTypeFilter.SelectedItem as ComboBoxItem)?.Tag as string;

        _changingFileFilters = true;
        try
        {
            FileTypeFilter.Items.Clear();
            FileTypeFilter.Items.Add(new ComboBoxItem { Content = L.AllFileTypes, Tag = null });
            foreach (var type in types)
                FileTypeFilter.Items.Add(new ComboBoxItem { Content = type.Value, Tag = type.Value });

            // При повторной загрузке сохраняем выбранный тип, если он ещё существует.
            var selectedIndex = 0;
            if (selected is not null)
            {
                for (var index = 1; index < FileTypeFilter.Items.Count; index++)
                {
                    if (FileTypeFilter.Items[index] is ComboBoxItem { Tag: string value } &&
                        string.Equals(value, selected, StringComparison.Ordinal))
                    {
                        selectedIndex = index;
                        break;
                    }
                }
            }

            FileTypeFilter.SelectedIndex = selectedIndex;
        }
        finally
        {
            _changingFileFilters = false;
        }
    }

    /// <summary>
    /// «Все пользователи» + загруженный список (до <see cref="FileUsersMaxAccounts"/>).
    /// Фильтр уходит в запрос как <c>creator_id</c>; логин хранится рядом для подписи
    /// секции, а владелец каждой строки приходит в <see cref="FileDto.CreatorLogin"/>.
    /// </summary>
    private void FillFileUserFilter(IReadOnlyList<UserResponse> users)
    {
        var selected = (FileUserFilter.SelectedItem as ComboBoxItem)?.Tag as Guid?;

        _changingFileFilters = true;
        try
        {
            FileUserFilter.Items.Clear();
            FileUserFilter.Items.Add(new ComboBoxItem { Content = L.AllUsers, Tag = null });
            foreach (var user in users)
                FileUserFilter.Items.Add(new ComboBoxItem { Content = user.Login, Tag = user.Id });

            // При повторной загрузке сохраняем выбранного пользователя, если он ещё есть.
            var selectedIndex = 0;
            if (selected is Guid id)
            {
                for (var index = 1; index < FileUserFilter.Items.Count; index++)
                {
                    if (FileUserFilter.Items[index] is ComboBoxItem { Tag: Guid value } && value == id)
                    {
                        selectedIndex = index;
                        break;
                    }
                }
            }

            FileUserFilter.SelectedIndex = selectedIndex;
        }
        finally
        {
            _changingFileFilters = false;
        }
    }

    /// <summary>Сбрасывает фильтры в «Все …» (конструктор и выход из аккаунта).</summary>
    private void ResetFileFilters()
    {
        _changingFileFilters = true;
        try
        {
            _fileCreator = null;
            _fileType = null;
            _activeFileCreator = null;
            _activeFileLogin = null;
            _activeFileType = null;
            _activeFileName = string.Empty;

            FilesNameBox.Text = string.Empty;

            FileUserFilter.Items.Clear();
            FileUserFilter.Items.Add(new ComboBoxItem { Content = L.AllUsers, Tag = null });
            FileUserFilter.SelectedIndex = 0;

            FileTypeFilter.Items.Clear();
            FileTypeFilter.Items.Add(new ComboBoxItem { Content = L.AllFileTypes, Tag = null });
            FileTypeFilter.SelectedIndex = 0;
        }
        finally
        {
            _changingFileFilters = false;
        }
    }

    private async void FileUserFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingFileFilters)
            return;

        _fileCreator = (FileUserFilter.SelectedItem as ComboBoxItem)?.Tag as Guid?;
        await LoadAllFilesPageAsync(reset: true);
    }

    private async void FileTypeFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingFileFilters)
            return;

        _fileType = (FileTypeFilter.SelectedItem as ComboBoxItem)?.Tag as string;
        await LoadAllFilesPageAsync(reset: true);
    }

    private async void FilesNameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
            return;

        e.Handled = true;
        await LoadAllFilesPageAsync(reset: true);
    }

    private async void FilesRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (!_filesLoaded)
        {
            await ShowFilesTabAsync();
            return;
        }

        // Частично загруженный справочник фильтра тоже можно восстановить
        // этой кнопкой: при _filesFiltersLoaded == true вызов ничего не делает.
        await LoadFilesFiltersAsync();
        await LoadAllFilesPageAsync(reset: true);
    }

    private async void MoreFiles_Click(object sender, RoutedEventArgs e)
        => await LoadAllFilesPageAsync(reset: false);

    /// <summary>
    /// Догружает страницу файлов; <paramref name="reset"/> — начать с первой.
    /// Фильтры запоминаются на время серии запросов, ответ применяется только если его
    /// поколение последнее: новый фильтр выигрывает у летящего запроса.
    /// </summary>
    private async Task LoadAllFilesPageAsync(bool reset)
    {
        if (!AppServices.Auth.IsAdmin)
            return;

        var generation = ++_filesRequest;

        if (reset)
        {
            _activeFileCreator = _fileCreator;
            _activeFileLogin = (FileUserFilter.SelectedItem as ComboBoxItem)?.Content as string;
            _activeFileType = _fileType;
            _activeFileName = FilesNameBox.Text.Trim();
            _filesLoadedPages = 0;
            _allFiles.Clear();
            FilesList.Items.Clear();
            FilesEmptyText.Visibility = Visibility.Collapsed;
            MoreFilesButton.Visibility = Visibility.Collapsed;
            UpdateFilesScopeTitle();
        }

        SetBusy(true);

        try
        {
            var page = _filesLoadedPages + 1;
            var response = await AppServices.Api.GetFilesAsync(
                page,
                AdminFilesPageSize,
                string.IsNullOrEmpty(_activeFileName) ? null : _activeFileName,
                _activeFileType is null ? null : [_activeFileType],
                _activeFileCreator);

            // Ответ устарел (сменился фильтр) или пользователь вышел из аккаунта.
            if (generation != _filesRequest || !AppServices.Auth.IsAdmin)
                return;

            foreach (var file in response.Items)
            {
                var row = new AdminFileRow(file);
                _allFiles.Add(row);
                FilesList.Items.Add(row);
            }

            _filesLoadedPages = page;
            UpdateFilesFooter(response.Count);
            FilesEmptyText.Visibility = _allFiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ErrorBar.IsOpen = false;
        }
        catch (ApiException ex)
        {
            if (generation == _filesRequest)
                ShowError(DescribeApiError(ex));
        }
        catch (Exception ex)
        {
            if (generation == _filesRequest)
                ShowError(L.AdminFilesLoadFailed(ex.Message));
        }
        finally
        {
            if (generation == _filesRequest)
                SetBusy(false);
        }
    }

    private void UpdateFilesFooter(int total)
    {
        FilesCounter.Text = L.UsersShown(_allFiles.Count, total);
        MoreFilesButton.Visibility = _allFiles.Count < total ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Подпись секции: при фильтре по пользователю — «Файлы пользователя login»,
    /// без фильтра — «Все файлы». Владелец каждой строки дополнительно показан
    /// в колонке «Пользователь».
    /// </summary>
    private void UpdateFilesScopeTitle()
        => FilesScopeTitle.Text = _activeFileCreator is null || string.IsNullOrEmpty(_activeFileLogin)
            ? L.AllFiles
            : L.UserContentTitle(_activeFileLogin);

    // --- Вкладка «Новости»: изображение (media_id) ------------------------

    private async Task LoadImageTypesAsync()
    {
        if (_imageTypes.Count > 0)
            return;

        try
        {
            var types = await AppServices.Api.GetFileTypesAsync();
            _imageTypes = types is null
                ? []
                : types.Where(type => type.IsImage).ToList();
        }
        catch (Exception)
        {
            // Справочник типов не критичен: ниже есть фильтры по умолчанию.
            _imageTypes = [];
        }
    }

    /// <summary>Расширения для <see cref="FileOpenPicker"/>; без справочника — типовые.</summary>
    private IReadOnlyList<string> ImagePickerExtensions => _imageTypes.Count > 0
        ? _imageTypes.Select(type => "." + type.Value).ToList()
        : FallbackImageTypes.Select(type => "." + type).ToList();

    /// <summary>Относится ли тип файла к изображениям (фильтр на клиенте).</summary>
    private bool IsImageType(string type)
    {
        var values = _imageTypes.Count > 0
            ? _imageTypes.Where(item => item.IsImage).Select(item => item.Value)
            : FallbackImageTypes;

        return values.Any(value => string.Equals(value, type, StringComparison.OrdinalIgnoreCase));
    }

    private async void UploadMedia_Click(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Auth.IsAdmin || _uploadingMedia)
            return;

        try
        {
            await LoadImageTypesAsync();

            var picker = new FileOpenPicker
            {
                SuggestedStartLocation = PickerLocationId.PicturesLibrary,
                ViewMode = PickerViewMode.Thumbnail,
            };

            foreach (var extension in ImagePickerExtensions)
                picker.FileTypeFilter.Add(extension);

            FileSaver.InitializeWithWindow(picker);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            await UploadMediaAsync(file.Name, file.Path);
        }
        catch (Exception ex)
        {
            ShowNewsMessage(InfoBarSeverity.Error, L.NewsUploadFailed(ex.Message));
        }
    }

    /// <summary>Загружает выбранный файл на сервер и подставляет его id как <c>media_id</c>.</summary>
    private async Task UploadMediaAsync(string name, string path)
    {
        _uploadingMedia = true;
        SetMediaButtonsEnabled(false);
        UploadProgress.Visibility = Visibility.Visible;
        UploadProgress.Value = 0;
        NewsInfoBar.IsOpen = false;
        SetBusy(true);
        UpdateCreateButtonState();

        try
        {
            var progress = new Progress<double>(value => UploadProgress.Value = value * 100);
            var uploaded = await AppServices.Api.UploadFileAsync(name, path, progress);

            if (!AppServices.Auth.IsAdmin)
                return;

            // Сначала снимаем выбор в списке, иначе его обработчик перебьёт новый media_id.
            MediaFilesCombo.SelectedIndex = -1;
            ApplyMedia(uploaded.Id);
            ShowNewsMessage(InfoBarSeverity.Success, L.NewsMediaId(uploaded.Id), L.NewsMediaUploaded);
        }
        catch (ApiException ex)
        {
            ShowNewsMessage(InfoBarSeverity.Error, DescribeApiError(ex));
        }
        catch (Exception ex)
        {
            ShowNewsMessage(InfoBarSeverity.Error, L.NewsUploadFailed(ex.Message));
        }
        finally
        {
            _uploadingMedia = false;
            SetMediaButtonsEnabled(true);
            UploadProgress.Visibility = Visibility.Collapsed;
            SetBusy(false);
            UpdateCreateButtonState();
        }
    }

    /// <summary>Кнопки выбора файла недоступны, пока идёт загрузка.</summary>
    private void SetMediaButtonsEnabled(bool enabled)
    {
        if (UploadMediaButton is null || ChooseMediaButton is null)
            return;

        UploadMediaButton.IsEnabled = enabled;
        ChooseMediaButton.IsEnabled = enabled;
    }

    private async void ChooseMedia_Click(object sender, RoutedEventArgs e)
    {
        if (!AppServices.Auth.IsAdmin)
            return;

        SetBusy(true);
        NewsInfoBar.IsOpen = false;

        try
        {
            await LoadImageTypesAsync();

            // Берём страницу целиком и отбираем изображения на клиенте: так список
            // не зависит от того, какие типы сервер считает картинками.
            var files = await AppServices.Api.GetFilesAsync(
                page: 1,
                pageSize: MediaFilesPageSize);

            if (!AppServices.Auth.IsAdmin)
                return;

            var options = files.Items
                .Where(dto => IsImageType(dto.Type))
                .Select(dto => new AdminMediaOption(dto))
                .ToList();
            MediaFilesCombo.ItemsSource = options;
            MediaFilesCombo.Visibility = options.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            MediaFilesCombo.SelectedItem = options.FirstOrDefault(option => option.Id == _mediaId);

            if (options.Count == 0)
                ShowNewsMessage(InfoBarSeverity.Informational, L.NewsMediaNoFiles);
        }
        catch (ApiException ex)
        {
            ShowNewsMessage(InfoBarSeverity.Error, DescribeApiError(ex));
        }
        catch (Exception ex)
        {
            ShowNewsMessage(InfoBarSeverity.Error, L.NewsFilesLoadFailed(ex.Message));
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void MediaFilesCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MediaFilesCombo.SelectedItem is AdminMediaOption option)
            ApplyMedia(option.Id);
    }

    private void ApplyMedia(Guid mediaId)
    {
        _mediaId = mediaId;
        MediaNoneText.Visibility = Visibility.Collapsed;
        MediaIdText.Text = L.NewsMediaId(mediaId);
        MediaIdText.Visibility = Visibility.Visible;
        CopyMediaIdButton.IsEnabled = true;
        ClearMediaButton.IsEnabled = true;

        // Старое превью убираем сразу: если новое не загрузится, на экране не должно
        // остаться картинки предыдущего файла.
        MediaPreview.Source = null;
        MediaPreview.Visibility = Visibility.Collapsed;

        _ = ShowMediaPreviewAsync(mediaId);
    }

    private async Task ShowMediaPreviewAsync(Guid mediaId)
    {
        var image = await ImageLoader.GetAsync(mediaId);
        if (image is null || _mediaId != mediaId)
            return;

        MediaPreview.Source = image;
        MediaPreview.Visibility = Visibility.Visible;
    }

    private void ResetMediaSelection()
    {
        _mediaId = null;
        MediaNoneText.Visibility = Visibility.Visible;
        MediaIdText.Text = string.Empty;
        MediaIdText.Visibility = Visibility.Collapsed;
        MediaPreview.Source = null;
        MediaPreview.Visibility = Visibility.Collapsed;
        CopyMediaIdButton.IsEnabled = false;
        ClearMediaButton.IsEnabled = false;
    }

    private void ClearMedia_Click(object sender, RoutedEventArgs e)
    {
        ResetMediaSelection();
        MediaFilesCombo.SelectedIndex = -1;
    }

    private void CopyMediaId_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaId is null)
            return;

        try
        {
            var package = new DataPackage();
            package.SetText(_mediaId.Value.ToString());
            Clipboard.SetContent(package);
            ShowNewsMessage(InfoBarSeverity.Success, L.NewsMediaCopied);
        }
        catch (Exception ex)
        {
            ShowNewsMessage(InfoBarSeverity.Error, ex.Message);
        }
    }

    // --- Вкладка «Новости»: создание --------------------------------------

    private void NewsNameBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateCreateButtonState();

    /// <summary>Кнопка активна только при непустом названии и вне запроса.</summary>
    private void UpdateCreateButtonState()
    {
        // TextChanged может прийти до того, как XAML создаст остальные элементы.
        if (CreateNewsButton is null || NewsNameBox is null)
            return;

        CreateNewsButton.IsEnabled =
            !_creatingNews && !_uploadingMedia && NewsNameBox.Text.Trim().Length > 0;
        CreateNewsButton.Content = _creatingNews ? L.NewsCreating : L.NewsCreate;
    }

    private async void CreateNews_Click(object sender, RoutedEventArgs e)
    {
        var name = NewsNameBox.Text.Trim();
        if (name.Length == 0 || _creatingNews || !AppServices.Auth.IsAdmin)
            return;

        _creatingNews = true;
        SetBusy(true);
        NewsInfoBar.IsOpen = false;
        UpdateCreateButtonState();

        try
        {
            var description = NewsDescriptionBox.Text.Trim();
            var created = await AppServices.Api.CreateNewsAsync(
                name,
                description.Length == 0 ? null : description,
                _mediaId);

            if (!AppServices.Auth.IsAdmin)
                return;

            var message = created.MediaId is null
                ? L.NewsCreated(created.Name, created.Weight)
                : $"{L.NewsCreated(created.Name, created.Weight)}{Environment.NewLine}{L.NewsMediaId(created.MediaId.Value)}";

            ShowNewsMessage(InfoBarSeverity.Success, message);

            // Форма очищается, чтобы новость нельзя было отправить дважды.
            NewsNameBox.Text = string.Empty;
            NewsDescriptionBox.Text = string.Empty;
            ResetMediaSelection();
            MediaFilesCombo.SelectedIndex = -1;
        }
        catch (ApiException ex)
        {
            ShowNewsMessage(InfoBarSeverity.Error, DescribeApiError(ex));
        }
        catch (Exception ex)
        {
            ShowNewsMessage(InfoBarSeverity.Error, L.NewsCreateFailed(ex.Message));
        }
        finally
        {
            _creatingNews = false;
            SetBusy(false);
            UpdateCreateButtonState();
        }
    }

    // --- Вспомогательное --------------------------------------------------

    /// <summary>
    /// 403 — это не про токен, а про роль: показываем строку про права. Исключение —
    /// 403 о блокировке аккаунта: там важнее причина, иначе пользователь прочитает
    /// «нужны права администратора».
    /// </summary>
    private string DescribeApiError(ApiException ex) => ex.IsForbidden
        ? (ex.IsBlockedUser ? ex.DetailOrMessage : L.AdminForbiddenMessage)
        : ex.Message;

    /// <summary>Подтверждение действия (тот же вид диалога, что и в файловом менеджере).</summary>
    private async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = primaryText,
            CloseButtonText = L.Cancel,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void ShowNewsMessage(InfoBarSeverity severity, string message, string? title = null)
    {
        NewsInfoBar.Severity = severity;
        NewsInfoBar.Title = title ?? string.Empty;
        NewsInfoBar.Message = message;
        NewsInfoBar.IsOpen = true;
    }

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorBar.Severity = InfoBarSeverity.Error;
        ErrorBar.Title = L.Error;
        ErrorBar.Message = message;
        ErrorBar.IsOpen = true;
    }
}
