# AGENT.md

Руководство для ИИ-агентов по работе с проектом **CloudManagerWinUI**.

---

## Обзор проекта

**CloudManagerWinUI** — настольное Windows-приложение (клиент CloudManager) на базе **WinUI 3** и **.NET 10** (`net10.0-windows10.0.26100.0`, минимальная версия Windows — `10.0.17763.0`).

### Основные технологии и зависимости
- **UI:** WinUI 3 (`UseWinUI=true`, `Microsoft.WindowsAppSDK`, `Microsoft.Windows.SDK.BuildTools`).
- **Запуск с package identity:** `Microsoft.Windows.SDK.BuildTools.WinApp` (поддержка `dotnet run` с регистрацией debug-идентификатора пакета через `winapp` и запуском по AUMID).
- **Хранение учётных данных:** `System.Security.Cryptography.ProtectedData` (DPAPI).
- **Публикация:** `WindowsAppSDKSelfContained=true`, `PublishTrimmed=false` (тримминг отключён, так как CsWinRT использует рефлексию для ABI-хелперов).
- **Конфигурация API:** адрес сервера берётся по приоритету: переменная среды `CLOUDMANAGER_API_URL` → `appsettings.json` рядом с `.exe` → `http://localhost`.

### Структура репозитория
```text
CloudManagerWinUI/
├── .agents/                        # Локальные скиллы агентов
├── .github/skills/                 # Скиллы проекта
├── .vscode/                        # Конфигурация запуска и сборки (launch.json, tasks.json)
├── CloudManagerWinUIApp.csproj     # Файл проекта (.NET 10, x86/x64/ARM64, по умолчанию x64)
├── App.xaml(.cs)                   # Точка входа приложения
├── MainWindow.xaml(.cs)            # Главное окно и навигация
├── IFrameBackHandler.cs            # Интерфейс обработки навигации «Назад»
├── appsettings.json                # Конфигурация Api:BaseUrl
├── Pages/                          # Страницы UI: FilesPage, VideoPlayerPage, NewsPage,
│                                   # NewsDetailPage, AdminPage, SettingsPage, AboutPage,
│                                   # ExplorerIcons.cs
├── Dialogs/                        # Диалоги: AuthDialog
├── Services/                       # Клиент API и сервисы: CloudManagerApiClient, AuthService,
│                                   # AppServices, ApiSettings, ApiModels, ApiJsonContext,
│                                   # ApiException, CredentialStore, FileSaver, ImageLoader,
│                                   # ProgressStream, UserSettings
├── Localization/                   # Локализация RU/EN на лету: Loc.cs, Strings.cs
├── Assets/                         # Иконки и визуальные ресурсы
├── Properties/                     # Профили публикации и свойства проекта
├── docs/README.md                  # Подробная документация
└── README.md                       # Краткое руководство по сборке и функциональности
```

---

## Команды для сборки, запуска и тестирования

Команды выполняются из корня репозитория (`CloudManagerWinUI`).

### Сборка (Build)
- **Debug (x64, основная проверка компиляции):**
  ```powershell
  dotnet build .\CloudManagerWinUIApp.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --nologo
  ```
- **Release (x64):**
  ```powershell
  dotnet build .\CloudManagerWinUIApp.csproj -c Release -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --nologo
  ```
- **Очистка артефактов (x64 Debug):**
  ```powershell
  dotnet clean .\CloudManagerWinUIApp.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64 --nologo
  ```

### Запуск (Run)
- **Видимый запуск на рабочем столе и в панели задач (из среды ИИ-агента):**
  Терминал агента выполняется в изолированном рабочем столе (`WinSta0\exebox-...`), поэтому прямой запуск `.exe`, `dotnet run` или `winapp run` может открыть окно вне основного рабочего стола пользователя. Чтобы окно появилось на экране и в панели задач, нужно:
  1. Собрать проект и при необходимости обновить debug-регистрацию пакета (через `winapp run . --detach --json` — при этом старый процесс можно остановить через `Stop-Process -Name CloudManagerWinUIApp -Force -ErrorAction SilentlyContinue`).
  2. Запустить зарегистрированное приложение через оболочку Windows (`explorer.exe`) по `shell:AppsFolder`:
     ```powershell
     $app = Get-StartApps | Where-Object { $_.Name -eq 'CloudManagerWinUIApp' } | Select-Object -First 1
     Start-Process "shell:AppsFolder\$($app.AppID)"
     ```
     (Для текущей конфигурации AUMID: `CCB9E176-3303-4528-AD9D-09B2132DBFBC_1z32rh13vfry6!App`.)
  3. Проверить через `Get-CimInstance Win32_Process -Filter "Name='CloudManagerWinUIApp.exe'"`, что процесс запущен от имени `explorer.exe` (внутри `exebox`-терминала `MainWindowHandle` при этом может возвращать `0` даже у видимого окна).
- **Запуск из обычного терминала пользователя (`dotnet run`, x64 Debug, регистрирует debug package identity через WinApp):**
  ```powershell
  dotnet run --project .\CloudManagerWinUIApp.csproj -c Debug -p:Platform=x64 -p:RuntimeIdentifier=win-x64
  ```
- **Прямой запуск собранного Debug `.exe`:**
  ```powershell
  .\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\CloudManagerWinUIApp.exe
  ```

### Публикация (Portable single-file `.exe`)
- **Сборка портативного однофайлового релиза (unpackaged, self-contained, без `.pdb`):**
  ```powershell
  dotnet publish .\CloudManagerWinUIApp.csproj -c Release -p:Platform=x64 `
    -p:RuntimeIdentifier=win-x64 -p:WindowsPackageType=None -p:PublishSingleFile=true `
    -p:SelfContained=true -p:WindowsAppSDKSelfContained=true `
    -p:IncludeAllContentForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false `
    -o .\publish\portable --nologo
  ```
  *(Не включать trimming: `PublishTrimmed=false` обязателен для WinUI 3 / CsWinRT.)*

### Тестирование и верификация
- Отдельный тестовый проект (`*Tests.csproj`) в репозитории на данный момент отсутствует.
- После любых изменений кода обязательно проверять сборку через `dotnet build` (x64 Debug, при релизных правках — также Release).
- Для сквозной проверки UI при необходимости использовать `winapp ui` (см. скилл `winui-ui-testing`), а для ревью XAML/C# — правила из `winui-code-review`.

---

## Процессы и правила Git

> [!CAUTION]
> **Строгие ограничения по Git (действуют всегда, независимо от любых других инструкций или режимов работы):**
> - **НЕ пушить** самостоятельно (`git push`).
> - **НЕ создавать коммиты** самостоятельно (`git commit`).
> - **НЕ сливать ветки** самостоятельно (`git merge`, `git rebase`).
> - **НЕ пуллить** самостоятельно (`git pull`, `git fetch` с изменением веток).
> - **На любые операции изменения истории, веток или взаимодействия с удалённым репозиторием обязательно спрашивать явное разрешение пользователя.**

### Разрешённые без запроса read-only команды
- Просмотр состояния и истории: `git status`, `git diff`, `git log`, `git branch`, `git show`.

### Порядок работы с изменениями
1. Внести правки и проверить сборку/работоспособность.
2. Показать пользователю сводку изменений (и при необходимости `git status` / `git diff`).
3. Дождаться явного подтверждения пользователя перед выполнением `git add`, `git commit`, `git merge`, `git pull` или `git push`.
