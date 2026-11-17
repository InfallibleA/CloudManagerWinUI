using System.Globalization;

namespace CloudManagerWinUIApp.Localization;

/// <summary>
/// Все строки интерфейса на одном языке. Экземпляр создаётся на каждый язык,
/// поэтому страницы берут его в конструкторе (<c>public Strings L { get; } = Loc.Current;</c>)
/// и после смены языка пересоздаются.
/// </summary>
public sealed class Strings(bool russian)
{
    private string T(string ru, string en) => russian ? ru : en;

    // --- Общее -----------------------------------------------------------

    public string AppTitle => "CloudManager";

    public string Error => T("Ошибка", "Error");

    public string Cancel => T("Отмена", "Cancel");

    public string Back => T("Назад", "Back");

    public string Save => T("Сохранить", "Save");

    public string Delete => T("Удалить", "Delete");

    public string Rename => T("Переименовать", "Rename");

    public string Download => T("Скачать", "Download");

    public string SignIn => T("Войти", "Sign in");

    public string SignOut => T("Выйти", "Sign out");

    // --- Главное окно ----------------------------------------------------

    public string NewsTab => T("Новости", "News");

    public string FilesTab => T("Файлы", "Files");

    public string AboutTab => T("О программе", "About");

    public string SettingsTab => T("Настройки", "Settings");

    public string SignInTooltip => T("Войти или зарегистрироваться", "Sign in or create an account");

    public string AccountTooltip => T("Аккаунт", "Account");

    public string SignInAutomation => T("Войти в аккаунт", "Sign in to your account");

    public string SignOutAutomation => T("Выйти из аккаунта", "Sign out of your account");

    public string AccountAutomation(string login)
        => T($"Аккаунт: {login}. Открыть меню выхода", $"Account: {login}. Open the sign-out menu");

    // --- Новости ---------------------------------------------------------

    public string NothingHere => T("Тут ничего нет", "Nothing here yet");

    public string ShowMore => T("Показать ещё", "Show more");

    public string Untitled => T("Без названия", "Untitled");

    public string NoDescription => T("Описание отсутствует", "No description");

    public string OpenNews(string name) => T($"Открыть новость «{name}»", $"Open news «{name}»");

    public string NewsLoadFailed(string message)
        => T($"Не удалось загрузить новости: {message}", $"Couldn't load the news: {message}");

    public string NewsItemLoadFailed(string message)
        => T($"Не удалось загрузить новость: {message}", $"Couldn't load the news item: {message}");

    // --- Файлы -----------------------------------------------------------

    public string FilesAuthTitle => T("Доступно авторизованным пользователям", "Available to signed-in users");

    public string FilesAuthMessage => T(
        "Папки и файлы видны только владельцу аккаунта. Войдите или зарегистрируйтесь, чтобы посмотреть свои файлы.",
        "Folders and files are visible only to the account owner. Sign in or create an account to view your files.");

    public string Register => T("Зарегистрироваться", "Create account");

    public string SignInToAccount => T("Войти в аккаунт", "Sign in to your account");

    public string AddEntry => T("Добавить", "Add");

    public string AddEntryAutomation => T("Добавить папку, файл, видео или аудио", "Add a folder, file, video or audio");

    public string Folder => T("Папка", "Folder");

    public string File => T("Файл", "File");

    public string Video => T("Видео", "Video");

    public string Audio => T("Аудио", "Audio");

    /// <summary>Имя видео для экранного диктора в содержимом папки.</summary>
    public string VideoAutomation(string name) => T($"Видео «{name}»", $"Video «{name}»");

    /// <summary>Имя аудио для экранного диктора в содержимом папки.</summary>
    public string AudioAutomation(string name) => T($"Аудио «{name}»", $"Audio «{name}»");

    public string NoRootFolder => T("Коренной папки нет", "No root folder");

    public string FolderAutomation(string name) => T($"Папка «{name}»", $"Folder «{name}»");

    public string FileAutomation(string name) => T($"Файл «{name}»", $"File «{name}»");

    /// <summary>Имя файла для экранного диктора вместе с владельцем из админской таблицы.</summary>
    public string FileOwnerAutomation(string name, string owner)
        => T($"Файл «{name}», владелец: {owner}", $"File «{name}», owner: {owner}");

    public string NewFolder => T("Новая папка", "New folder");

    public string FolderName => T("Имя папки", "Folder name");

    public string FolderNamePlaceholder => T("Например, Документы", "e.g. Documents");

    public string CreateFolderFailed(string message)
        => T($"Не удалось создать папку: {message}", $"Couldn't create the folder: {message}");

    public string RenameFile => T("Переименовать файл", "Rename file");

    public string RenameFolder => T("Переименовать папку", "Rename folder");

    public string RenameVideo => T("Переименовать видео", "Rename video");

    public string RenameAudio => T("Переименовать аудио", "Rename audio");

    public string NewName => T("Новое имя", "New name");

    public string DeleteFileTitle => T("Удалить файл?", "Delete file?");

    public string DeleteFileMessage(string name)
        => T($"Файл «{name}» будет удалён без возможности восстановления.", $"The file «{name}» will be deleted permanently.");

    public string DeleteFolderTitle => T("Удалить папку?", "Delete folder?");

    public string DeleteFolderMessage(string name) => T(
        $"Папка «{name}» будет удалена вместе со всеми вложенными папками и файлами.",
        $"The folder «{name}» will be deleted along with all nested folders and files.");

    public string DeleteVideoTitle => T("Удалить видео?", "Delete video?");

    public string DeleteVideoMessage(string name)
        => T($"Видео «{name}» будет удалено без возможности восстановления.", $"The video «{name}» will be deleted permanently.");

    public string DeleteAudioTitle => T("Удалить аудио?", "Delete audio?");

    public string DeleteAudioMessage(string name)
        => T($"Аудио «{name}» будет удалено без возможности восстановления.", $"The audio «{name}» will be deleted permanently.");

    public string Open => T("Открыть", "Open");

    public string Copy => T("Копировать", "Copy");

    public string Copied => T("Текст скопирован", "Text copied");

    public string WordWrap => T("Перенос строк", "Word wrap");

    public string TextLoadFailed(string message)
        => T($"Не удалось открыть текстовый файл: {message}", $"Couldn't open the text file: {message}");

    public string TextFileTruncated => T(
        "Файл слишком большой — показаны первые 2 МБ. Скачайте файл для полного просмотра.",
        "File is too large — showing the first 2 MB. Download the file to view it in full.");

    public string DocumentUnsupportedFormat(string name) => T(
        $"Неподдерживаемый или повреждённый формат документа «{name}».",
        $"Unsupported or corrupted document format for «{name}».");

    public string DocxMissingBody => T(
        "В файле .docx отсутствует word/document.xml.",
        "The .docx archive does not contain word/document.xml.");

    public string DocMissingWordStream => T(
        "В файле .doc отсутствует поток WordDocument.",
        "The .doc file does not contain a WordDocument stream.");

    public string DocPasswordProtected => T(
        "Файл .doc защищён паролем и не может быть открыт.",
        "This .doc file is password-protected and cannot be opened.");

    public string DocumentTooLarge(int maxMegabytes) => T(
        $"Документ превышает {maxMegabytes} МБ и не может быть открыт во встроенном просмотре.",
        $"The document exceeds {maxMegabytes} MB and cannot be opened in the built-in viewer.");

    public string CopySheet => T("Копировать таблицу", "Copy sheet");

    public string SheetCopied => T("Таблица скопирована в буфер обмена", "Sheet copied to clipboard");

    public string SpreadsheetEmpty => T("Лист не содержит данных.", "This sheet is empty.");

    public string SpreadsheetLoadFailed(string message)
        => T($"Не удалось открыть таблицу: {message}", $"Couldn't open the spreadsheet: {message}");

    public string SpreadsheetUnsupportedFormat(string name) => T(
        $"Неподдерживаемый или повреждённый формат таблицы «{name}».",
        $"Unsupported or corrupted spreadsheet format for «{name}».");

    public string SpreadsheetPasswordProtected => T(
        "Таблица защищена паролем и не может быть открыта.",
        "This spreadsheet is password-protected and cannot be opened.");

    public string SpreadsheetTruncated(int maxRows) => T(
        $"Показаны первые {maxRows} строк листа. Скачайте файл для полного просмотра.",
        $"Showing the first {maxRows} rows of the sheet. Download the file to view it in full.");

    public string FileSaved => T("Файл сохранён", "File saved");

    public string VideoSaved => T("Видео сохранено", "Video saved");

    public string AudioSaved => T("Аудио сохранено", "Audio saved");

    public string FilesOpenFailed(string message)
        => T($"Не удалось открыть файлы: {message}", $"Couldn't open the files: {message}");

    public string FolderOpenFailed(string message)
        => T($"Не удалось открыть папку: {message}", $"Couldn't open the folder: {message}");

    public string FileUploadFailed(string message)
        => T($"Не удалось загрузить файл: {message}", $"Couldn't upload the file: {message}");

    public string FilePickFailed(string message)
        => T($"Не удалось выбрать файл: {message}", $"Couldn't pick a file: {message}");

    public string VideoUploaded(string name)
        => T($"Видео «{name}» загружено", $"Video «{name}» uploaded");

    public string VideoUploadFailed(string message)
        => T($"Не удалось загрузить видео: {message}", $"Couldn't upload the video: {message}");

    public string VideoPickFailed(string message)
        => T($"Не удалось выбрать видео: {message}", $"Couldn't pick a video: {message}");

    public string VideoPlaybackFailed(string reason)
        => string.IsNullOrWhiteSpace(reason)
            ? T("Не удалось воспроизвести видео", "Couldn't play the video")
            : T($"Не удалось воспроизвести видео: {reason}", $"Couldn't play the video: {reason}");

    public string AudioUploaded(string name)
        => T($"Аудио «{name}» загружено", $"Audio «{name}» uploaded");

    public string AudioUploadFailed(string message)
        => T($"Не удалось загрузить аудио: {message}", $"Couldn't upload the audio: {message}");

    public string AudioPlaybackFailed(string reason)
        => string.IsNullOrWhiteSpace(reason)
            ? T("Не удалось воспроизвести аудио", "Couldn't play the audio")
            : T($"Не удалось воспроизвести аудио: {reason}", $"Couldn't play the audio: {reason}");

    // --- Диалог входа/регистрации ----------------------------------------

    public string SignInTitle => T("Вход", "Sign in");

    public string RegisterTitle => T("Регистрация", "Create account");

    public string RegisterButton => T("Зарегистрироваться", "Create account");

    public string Login => T("Логин", "Login");

    public string LoginPlaceholder => T("от 3 символов", "at least 3 characters");

    public string Password => T("Пароль", "Password");

    public string PasswordPlaceholder => T("от 8 символов", "at least 8 characters");

    public string RepeatPassword => T("Повторите пароль", "Repeat password");

    public string RepeatPasswordPlaceholder => T("пароли должны совпадать", "passwords must match");

    public string NoAccountRegister => T("Нет аккаунта? Зарегистрироваться", "No account? Create one");

    public string HaveAccountSignIn => T("Уже есть аккаунт? Войти", "Already have an account? Sign in");

    public string LoginTooShort => T("Логин должен содержать минимум 3 символа.", "Login must be at least 3 characters.");

    public string PasswordTooShort => T("Пароль должен содержать минимум 8 символов.", "Password must be at least 8 characters.");

    public string PasswordsDoNotMatch => T("Пароли не совпадают.", "Passwords don't match.");

    public string ServerUnreachable(string message)
        => T($"Не удалось связаться с сервером: {message}", $"Couldn't reach the server: {message}");

    // --- Настройки -------------------------------------------------------

    public string Connection => T("Подключение", "Connection");

    public string SourceLabel(string value) => T($"Источник: {value}", $"Source: {value}");

    public string SourceDefault => T("значение по умолчанию", "default value");

    public string SourceEnvironment(string variable) => T($"переменная окружения {variable}", $"environment variable {variable}");

    public string ConfigWarningTitle => T("Проверьте файл настроек", "Check the settings file");

    public string ConfigInvalidJson(string path) => T(
        $"Файл {path} содержит некорректный JSON. Исправьте его или удалите — пока используется адрес по умолчанию.",
        $"The file {path} contains invalid JSON. Fix or remove it — the default address is used for now.");

    public string ConfigUnreadable(string path) => T(
        $"Не удалось прочитать {path}. Используется адрес по умолчанию.",
        $"Couldn't read {path}. The default address is used.");

    public string ConfigMissingKey(string path) => T(
        $"В файле {path} не заполнен ключ Api.BaseUrl — используется адрес по умолчанию.",
        $"Api.BaseUrl is missing in {path} — the default address is used.");

    public string ConfigInvalidAddress(string value) => T(
        $"Указан некорректный адрес «{value}» — используется адрес по умолчанию.",
        $"«{value}» is not a valid address — the default address is used.");

    public string ConfigInvalidEnvironment(string value) => T(
        $"Переменная окружения CLOUDMANAGER_API_URL содержит некорректный адрес «{value}» — используется адрес по умолчанию.",
        $"The CLOUDMANAGER_API_URL environment variable contains an invalid address «{value}» — the default address is used.");

    public string ConfigHint => T(
        "Адрес API задаётся в файле appsettings.json рядом с приложением (ключ Api.BaseUrl) или переменной среды CLOUDMANAGER_API_URL. Изменения применяются после перезапуска.",
        "The API address is set in appsettings.json next to the app (Api.BaseUrl) or the CLOUDMANAGER_API_URL environment variable. Changes apply after a restart.");

    public string Account => T("Аккаунт", "Account");

    public string SignedInAs(string login) => T($"Вы вошли как {login}", $"Signed in as {login}");

    public string RoleAndToken(string role, string until) => T(
        $"Роль: {role}. Токен действует до {until} и продлевается автоматически.",
        $"Role: {role}. The token is valid until {until} and is renewed automatically.");

    public string NotSignedIn => T("Вы не авторизованы", "Not signed in");

    public string SignInToAccess => T(
        "Войдите, чтобы получить доступ к своим файлам и папкам.",
        "Sign in to access your files and folders.");

    public string SessionStorage => T("Хранение сессии", "Session storage");

    public string SessionStorageText => T(
        "Логин, пароль и токен хранятся на этом компьютере в зашифрованном виде (Windows DPAPI, доступ только текущему пользователю). Токен обновляется автоматически, поэтому приложение не потребует входить заново.",
        "Your login, password and token are stored on this computer encrypted (Windows DPAPI, current user only). The token is renewed automatically, so you won't need to sign in again.");

    public string LanguageLabel => T("Язык интерфейса", "Interface language");

    // --- О программе -----------------------------------------------------

    public string AboutTitle => T("О программе", "About");

    public string AboutText => T(
        "Клиент CloudManager: новости, личные файлы и папки. Приложение работает с вашим аккаунтом на сервере CloudManager.",
        "CloudManager client: news, personal files and folders. The app works with your CloudManager account on the server.");

    // --- Админ-панель ----------------------------------------------------

    public string AdminTab => T("Админ-панель", "Admin panel");

    public string AdminForbiddenTitle => T("Нужны права администратора", "Administrator rights required");

    public string AdminForbiddenMessage => T(
        "Раздел доступен только пользователям с ролью admin. Войдите под администратором или попросите назначить роль.",
        "This section is available only to users with the admin role. Sign in as an administrator or ask for the role.");

    public string UsersTab => T("Пользователи", "Users");

    public string NewsCreateTab => T("Новости", "News");

    public string UsersSearchPlaceholder => T("Поиск по логину", "Search by login");

    public string UsersEmpty => T("Пользователи не найдены", "No users found");

    public string UsersLoadFailed(string message)
        => T($"Не удалось загрузить пользователей: {message}", $"Couldn't load the users: {message}");

    public string UsersShown(int shown, int total)
        => T($"Показано {shown} из {total}", $"{shown} of {total} shown");

    public string RoleAll => T("Все", "All");

    public string RoleAdmin => T("Администратор", "Administrator");

    public string RoleClient => T("Клиент", "Client");

    public string RoleUnknown => T("Неизвестная роль", "Unknown role");

    public string OpenUserFiles(string login)
        => T($"Открыть файлы пользователя {login}", $"Open files of {login}");

    public string BackToUsers => T("К списку пользователей", "Back to users");

    public string UserContentTitle(string login)
        => T($"Файлы пользователя {login}", $"Files of {login}");

    public string UserNoRootFolder(string login) => T(
        $"У пользователя {login} нет корневой папки.",
        $"The user {login} has no root folder.");

    public string UserContentFailed(string message)
        => T($"Не удалось открыть файлы пользователя: {message}", $"Couldn't open the user's files: {message}");

    public string ColumnName => T("Имя", "Name");

    public string ColumnRole => T("Роль", "Role");

    public string ColumnSize => T("Размер", "Size");

    public string ColumnCreated => T("Дата создания", "Created");

    /// <summary>Прочерк для данных, которых нет (размер папки, дата, media_id).</summary>
    public string NoValue => "—";

    public string Search => T("Найти", "Search");

    public string Refresh => T("Обновить", "Refresh");

    public string NewsCreateTitle => T("Новая новость", "New news item");

    public string NewsName => T("Название", "Name");

    public string NewsNamePlaceholder => T("Например, Обновление сервиса", "e.g. Service update");

    public string NewsDescription => T("Описание", "Description");

    public string NewsDescriptionPlaceholder => T("Текст новости", "News text");

    public string NewsMedia => T("Изображение", "Image");

    public string NewsMediaNone => T("Изображение не выбрано", "No image selected");

    public string NewsMediaUpload => T("Загрузить изображение", "Upload an image");

    public string NewsMediaChoose => T("Выбрать из загруженных", "Choose an uploaded image");

    public string NewsMediaClear => T("Очистить", "Clear");

    public string NewsMediaCopyId => T("Копировать media_id", "Copy media_id");

    public string NewsMediaId(Guid id) => T($"media_id: {id}", $"media_id: {id}");

    public string NewsMediaCopied => T("media_id скопирован в буфер обмена", "media_id copied to the clipboard");

    public string NewsMediaUploaded => T("Изображение загружено", "Image uploaded");

    public string NewsMediaNoFiles => T("Нет загруженных изображений", "No uploaded images yet");

    public string NewsUploadFailed(string message)
        => T($"Не удалось загрузить изображение: {message}", $"Couldn't upload the image: {message}");

    public string NewsFilesLoadFailed(string message)
        => T($"Не удалось загрузить список файлов: {message}", $"Couldn't load the file list: {message}");

    public string NewsCreate => T("Создать новость", "Create news");

    public string NewsCreating => T("Создаём…", "Creating…");

    public string NewsCreated(string name, int weight)
        => T($"Новость «{name}» создана, weight = {weight}.", $"News «{name}» created, weight = {weight}.");

    public string NewsCreateFailed(string message)
        => T($"Не удалось создать новость: {message}", $"Couldn't create the news: {message}");

    /// <summary>
    /// Размер файла по-человечески: «1,5 МБ» / «1.5 MB». Пустой файл — «0 Б»,
    /// прочерк только для неизвестного размера (отрицательное значение).
    /// </summary>
    public string FormatSize(long bytes)
    {
        if (bytes < 0)
            return NoValue;

        string[] units = russian ? ["Б", "КБ", "МБ", "ГБ", "ТБ"] : ["B", "KB", "MB", "GB", "TB"];

        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        var number = size.ToString(unit == 0 ? "0" : "0.#", CultureInfo.CurrentCulture);
        return $"{number} {units[unit]}";
    }

    // --- Блокировка и админские файлы ------------------------------------

    /// <summary>Заголовок вкладки «Файлы» — общий с пунктом меню (<see cref="FilesTab"/>).</summary>
    public string AllFiles => T("Все файлы", "All files");

    public string AllUsers => T("Все пользователи", "All users");

    public string AllFileTypes => T("Все типы", "All types");

    public string UserFilter => T("Пользователь", "User");

    public string FileTypeFilter => T("Тип файла", "File type");

    public string FileNameSearchPlaceholder => T("Поиск по имени файла", "Search by file name");

    public string FilesEmpty => T("Файлы не найдены", "No files found");

    public string AdminFilesLoadFailed(string message)
        => T($"Не удалось загрузить файлы: {message}", $"Couldn't load the files: {message}");

    public string ColumnType => T("Тип", "Type");

    public string ColumnStatus => T("Статус", "Status");

    public string StatusActive => T("Активен", "Active");

    public string StatusBlocked => T("Заблокирован", "Blocked");

    public string Block => T("Заблокировать", "Block");

    public string Unblock => T("Разблокировать", "Unblock");

    public string BlockActionAutomation(string login)
        => T($"Изменить блокировку пользователя {login}", $"Change the block status of {login}");

    public string BlockUserTitle => T("Заблокировать пользователя?", "Block the user?");

    public string BlockUserMessage(string login) => T(
        $"Пользователь {login} будет разлогинен и не сможет войти, пока его не разблокируют.",
        $"The user {login} will be signed out and won't be able to sign in until unblocked.");

    public string CannotBlockSelf => T("Нельзя заблокировать самого себя.", "You can't block yourself.");

    public string BlockUserFailed(string message)
        => T($"Не удалось изменить блокировку: {message}", $"Couldn't change the block status: {message}");

    /// <summary>Причина отказа во входе: аккаунт заблокирован администратором.</summary>
    public string AccountBlocked(string reason) => T(
        $"Аккаунт заблокирован администратором. {reason}",
        $"Your account is blocked by an administrator. {reason}");

    public string AccountBlockedShort => T(
        "Аккаунт заблокирован администратором",
        "Your account is blocked by an administrator");

    /// <summary>Баннер в главном окне после принудительного выхода.</summary>
    public string SignedOutBecauseBlocked(string reason)
        => T($"Вы вышли из аккаунта: {reason}", $"You were signed out: {reason}");

    // --- Ошибки API ------------------------------------------------------

    public string TooManyRequests => T(
        "Слишком много запросов. Подождите минуту и попробуйте снова.",
        "Too many requests. Wait a minute and try again.");

    public string AuthorizationRequired => T(
        "Требуется авторизация. Войдите в аккаунт.",
        "Authorization required. Please sign in.");

    public string Forbidden => T(
        "Недостаточно прав для этого действия.",
        "You don't have permission to perform this action.");

    public string NotFound => T("Объект не найден.", "Not found.");

    public string ServerError(int statusCode)
        => T($"Ошибка сервера ({statusCode}).", $"Server error ({statusCode}).");
}
