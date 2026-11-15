# CloudManager API — инструкция для десктоп-клиента (WinUI 3)

Документ описывает всё, что нужно десктопному приложению на WinUI 3, чтобы работать
с backend'ом: базовые правила API, регистрацию и авторизацию, полный цикл работы с видео.
Все факты сверены с кодом backend'а (файлы-источники перечислены в конце).

## 1. Базовые сведения

| Параметр | Значение |
|---|---|
| Базовый URL (dev) | `http://localhost` (порт шлюза — `GATEWAY_PORT`, по умолчанию `80`) |
| Swagger UI | `GET /docs` |
| OpenAPI-схема | `GET /openapi.json` |
| Liveness | `GET /health` → `{"status": "ok", "version": "1.0"}` |
| Readiness | `GET /health/ready` → 200 / 503 (проверка БД) |
| Транспорт | HTTP/1.1, везде кроме загрузки видео — JSON `application/json`, UTF-8 |
| Формат полей | `snake_case` (`access_token`, `page_size`, `created_at`, `object_key`) |
| Идентификаторы | UUID v4 в виде строки |
| Даты | ISO 8601 с offset в UTC, например `2026-10-16T13:44:15.123456+00:00` (в C# — `DateTimeOffset`) |

Порт контейнера приложения наружу не публикуется: **всё идёт через nginx-шлюз**
(`GATEWAY_PORT`), он же проксирует файлы и видео на S3-хранилище.

CORS клиенту не нужен (это браузерное ограничение), cookies не используются —
авторизация полностью на заголовке `Authorization`.

### 1.1. Формат ошибок

Доменные ошибки приложения (всё, что приходит от логики API) — единый вид:

```json
{ "error": "UnauthorizedException", "detail": "Требуется авторизация." }
```

`error` — имя класса исключения, `detail` — строка, а иногда объект или список.

**Важное исключение:** ошибки валидации схемы (422) отдаёт FastAPI в своём формате, без `error`:

```json
{ "detail": [ { "type": "string_too_short", "loc": ["body", "password"], "msg": "String should have at least 8 characters", "input": "123" } ] }
```

Поэтому в клиенте парсите `detail` как «строку или что угодно» (в C# — `JsonElement.GetRawText()`).

Коды и типовые причины:

| Код | `error` | Когда |
|---|---|---|
| 400 | `ValidationException` | Недопустимый тип видео, пустой файл, размер больше лимита, неверный `parent_id` |
| 400 | `UniqueConstraintException` | Занятый логин при регистрации (HTTP 400, не 409) |
| 401 | `UnauthorizedException` | Нет заголовка `Authorization`, не тот префикс, истёкший или битый токен, неверный логин или пароль |
| 403 | `ForbiddenException` | Токен валиден, но роль не `admin` («Действие доступно только администратору.») |
| 404 | `ObjectNotFoundException` | Нет объекта по id или имени |
| 409 | `UniqueConstraintException` | Дубль имени видео (здесь именно 409) |
| 422 | — | Схема запроса не прошла валидацию (формат FastAPI, см. выше) |
| 429 | `TooManyRequests` | Превышен лимит частоты (его отдаёт nginx, а не приложение) |
| 500 | `InternalServerException` | Внутренняя ошибка |
| 503 | — | Только `/health/ready` при недоступной БД |

На 401 приходит заголовок `WWW-Authenticate: Bearer` — в клиенте это надёжный признак
«нужно показать окно входа».

### 1.2. Ограничение частоты (rate limiting)

Лимиты живут на nginx и считаются по IP клиента (для десктопа — по IP машины).

| Что | Лимит | Всплеск |
|---|---|---|
| `POST /api/auth/register`, `POST /api/auth/login` | `10 r/m` | 5 |
| Всё остальное (`/api/...`, включая загрузку видео и `/playback`) | `30 r/s` | 60 |
| `/cloudmanager-files/...` (presigned-ссылки на видео) | не лимитируется | — |

Ответ при превышении — `429` с телом
`{"error":"TooManyRequests","detail":"Слишком много запросов. Попробуйте позже."}`.

Практический вывод: **не делайте автоматических ретраев логина** (10 в минуту) и показывайте
пользователю понятное сообщение; поллинг списка видео держите разумным (менее 30 запросов в секунду).

## 2. Регистрация и авторизация

Роли: `admin` (управляет контентом: загрузка и удаление видео и файлов) и `client`
(может читать, загружать файлы, вести свои папки). Администраторов через API создать нельзя —
только из консоли на сервере (`docker compose run --rm api python -m app.users.cli create-admin`).

Схема потока:

```mermaid
sequenceDiagram
    participant App as WinUI 3 клиент
    participant API as CloudManager API
    App->>API: POST /api/auth/register {login, password}
    API-->>App: 201 {id, login, role: client, created_at}
    App->>API: POST /api/auth/login {login, password}
    API-->>App: 200 {access_token, token_type, expires_in}
    App->>API: GET /api/auth/me (Authorization: Bearer ...)
    API-->>App: 200 {id, login, role, created_at}
    Note over App: токен действует JWT_ACCESS_TOKEN_EXPIRE_MINUTES (по умолчанию 24 ч)
```

### 2.1. `POST /api/auth/register` — регистрация (без токена)

Запрос:

```http
POST /api/auth/register
Content-Type: application/json

{ "login": "desktop-user", "password": "s3cret-pass" }
```

| Поле | Правила |
|---|---|
| `login` | строка, 3…255 символов |
| `password` | строка, 8…128 символов |

Ответ `201`:

```json
{
  "id": "0f0a5c1e-6a63-4a4d-9c2a-1e0f4b6a1c77",
  "login": "desktop-user",
  "role": "client",
  "created_at": "2026-10-16T13:44:15.123456+00:00"
}
```

Ошибки:

* `400 UniqueConstraintException` — `"detail": "Поле login должны быть уникальны."` (логин занят);
* `422` — схема не прошла валидацию (короткий логин или пароль);
* `429` — больше 10 запросов в минуту.

Заметьте: регистрация **сразу не выдаёт токен** — после неё нужно вызвать `/login`.

### 2.2. `POST /api/auth/login` — получение токена (без токена)

```http
POST /api/auth/login
Content-Type: application/json

{ "login": "desktop-user", "password": "s3cret-pass" }
```

Ответ `200`:

```json
{ "access_token": "eyJhbGciOiJIUzI1NiIs...", "token_type": "Bearer", "expires_in": 86400 }
```

* `expires_in` — время жизни токена в секундах (`JWT_ACCESS_TOKEN_EXPIRE_MINUTES * 60`,
  по умолчанию `1440 * 60 = 86400`, то есть 24 часа);
* поле `login` в схеме входа ограничений по длине не имеет, но неверные данные дают
  `401 UnauthorizedException` с `"detail": "Неверный логин или пароль."`.

### 2.3. `GET /api/auth/me` — текущий пользователь

```http
GET /api/auth/me
Authorization: Bearer eyJhbGciOiJIUzI1NiIs...
```

Ответ `200` — тот же объект, что отдаёт регистрация (`id`, `login`, `role`, `created_at`).
Удобно вызывать при старте приложения для восстановления сессии и проверки роли.

### 2.4. `GET /api/auth/users` — список пользователей (только admin)

```http
GET /api/auth/users?login=desk&role=client&page=1&page_size=25
Authorization: ******
```

| Параметр | Правила | Назначение |
|---|---|---|
| `login` | строка до 255 | Поиск по вхождению подстроки в логин без учёта регистра |
| `role` | `admin` \| `client` | Точный фильтр по роли |
| `page` | целое ≥ 1, по умолчанию `1` | Номер страницы |
| `page_size` | целое 1…100, по умолчанию `25` | Размер страницы |

Ответ `200` — страница пользователей в порядке возрастания логина:

```json
{
  "count": 2,
  "offset": 0,
  "page": 1,
  "page_size": 25,
  "data": [
    {
      "id": "0f0a5c1e-6a63-4a4d-9c2a-1e0f4b6a1c77",
      "login": "desktop-user",
      "role": "client",
      "created_at": "2026-10-16T13:44:15.123456+00:00"
    }
  ]
}
```

Ошибки:

* `401 UnauthorizedException` — нет заголовка `Authorization` или токен невалиден;
* `403 ForbiddenException` — токен валиден, но роль не `admin`;
* `422` — неизвестная роль или неверные параметры пагинации.

### 2.5. Устройство токена и что из этого следует для клиента

* JWT, алгоритм `HS256`, секрет — серверный (`JWT_SECRET_KEY`).
* Payload: `sub` (UUID пользователя), `iat`, `exp`.
* **Refresh-токена нет** — по истечении срока нужен повторный `/login`.
* Проверка токена — только подпись и срок; отзыв токена (logout на сервере) не предусмотрен.
  «Выход» в приложении = удалить токен у себя.
* Роль берётся из БД на каждом запросе (в токене её нет), поэтому смена роли подхватывается
  сразу, а изменение или удаление пользователя делает токен невалидным (401/404).

Практика для WinUI 3:

1. Держите `access_token` в памяти и сохраняйте между запусками в
   `Windows.Security.Credentials.PasswordVault` (Credential Locker) или зашифровано через DPAPI
   (`ProtectedData.Protect`). Не пишите токен в открытый JSON-файл и не логируйте.
2. Сохраняйте рядом `expires_at = DateTimeOffset.UtcNow + expires_in` и просто не используйте
   просроченный токен — это дешевле, чем ловить 401.
3. Любой `401` (кроме самого `/login`) трактуйте как «сессия истекла»: показывайте окно входа,
   сохранив введённый пользователем контекст (выбранное видео и т. п.).
4. `403` — это не про токен, а про роль: показывайте «нужны права администратора», не разлогинивайте.

## 3. Работа с видео

Видео лежат в S3-совместимом хранилище (Garage), в БД — только метаданные.
Воспроизведение идёт **напрямую из хранилища по presigned-ссылке**, backend не проксирует
гигабайты через себя: клиент получает ссылку с ограниченным временем жизни и играет её.

```mermaid
flowchart LR
    A[Выбрать файл] --> B[POST /api/videos multipart]
    B -->|201| C[Запись в БД: id, name, object_key, type, size]
    C --> D[GET /api/videos/id/playback]
    D -->|url + expires_in| E[MediaPlayerElement играет по URL]
    C --> F[DELETE /api/videos/id]
```

### 3.1. Карта эндпоинтов

| Метод | Путь | Авторизация | Назначение |
|---|---|---|---|
| `POST` | `/api/videos` | **admin** | Загрузка: multipart `name` + `file` |
| `GET` | `/api/videos` | без токена | Список: фильтры `name`, `type`, пагинация |
| `GET` | `/api/videos/types` | без токена | Справочник типов для `accept` и фильтра |
| `GET` | `/api/videos/by-name?name=...` | без токена | Точный поиск по имени |
| `GET` | `/api/videos/{video_id}` | без токена | Видео по UUID |
| `GET` | `/api/videos/{video_id}/playback` | без токена | Presigned-ссылка на воспроизведение |
| `DELETE` | `/api/videos/{video_id}` | **admin** | Мягкое удаление записи и объекта из S3 |

Чтение открыто всем (видео играет публичная страница сайта), запись — только админу:
без токена `401`, с токеном роли `client` — `403 ForbiddenException`.

### 3.2. Модель видео

Любая ручка, возвращающая видео, отдаёт один и тот же объект:

```json
{
  "id": "9c1f5a3e-6d2b-4b1f-9a3c-5c2d4e7f8a10",
  "name": "Промо ролик 2026",
  "object_key": "videos/3f7c1d2e4b5a69788796a5b4c3d2e1f0.mp4",
  "type": "mp4",
  "size": 73400320,
  "created_at": "2026-10-16T13:44:15.123456+00:00"
}
```

* `name` — человекочитаемое имя, **уникально среди неудалённых** видео (1…255 символов);
* `object_key` — внутренний ключ объекта в S3 (`videos/<uuid4hex>.<ext>`). Клиенту он не нужен
  для воспроизведения (для этого есть `/playback`), но полезен для отладки;
* `type` — только `mp4` или `webm`;
* `size` — размер в байтах.

### 3.3. Список видео: `GET /api/videos`

Query-параметры:

| Параметр | Тип | По умолчанию | Описание |
|---|---|---|---|
| `name` | строка ≤ 255 | — | Поиск по подстроке, **без учёта регистра** (`ILIKE %name%`) |
| `type` | `mp4` / `webm` | — | Можно повторить: `?type=mp4&type=webm` (любой из перечисленных) |
| `page` | int ≥ 1 | `1` | Номер страницы |
| `page_size` | int 1…100 | `25` | Размер страницы (больше 100 → `422`) |

Сортировка — `created_at DESC` (сначала новые). Ответ:

```json
{
  "count": 42,
  "offset": 0,
  "page": 1,
  "page_size": 25,
  "data": []
}
```

`count` — общее число подходящих записей (для счётчика страниц), `offset = page_size * (page - 1)`.
При пустом результате `data` — пустой массив.

### 3.4. Справочник типов: `GET /api/videos/types`

Отдаёт список, который удобно сразу использовать для фильтра и для `accept` в диалоге выбора файла:

```json
[
  { "value": "mp4",  "content_type": "video/mp4" },
  { "value": "webm", "content_type": "video/webm" }
]
```

### 3.5. Поиск: `GET /api/videos/by-name?name=...` и `GET /api/videos/{video_id}`

* `by-name` — **точное** совпадение имени, регистрозависимо; нет — `404` c
  `"detail": "Видео с именем «X» не найдено."`;
* `{video_id}` — по UUID; нет — `404` c текстом вида `Объект Video с id <uuid> не существует.`
  (в тексте светится имя внутренней модели — не показывайте его пользователю как есть);
* некорректный UUID в пути даёт `422`.

### 3.6. Загрузка: `POST /api/videos` (только admin)

Тело — `multipart/form-data`, ровно два поля:

| Поле | Тип | Правила |
|---|---|---|
| `name` | text | 1…255 символов, уникально среди неудалённых |
| `file` | file | расширение `.mp4` или `.webm`, непустой, размер ≤ `VIDEO_MAX_SIZE_MB` (по умолчанию 2048 МБ) |

Тип видео определяется **по расширению имени файла**, а не по `Content-Type`: обязательно
передавайте имя файла с правильным расширением в `Content-Disposition` части multipart.

Ответ `201` — объект видео (см. 3.2). Ключ объекта сервер генерирует сам: `videos/<uuid4hex>.<ext>`.

Ошибки загрузки:

| Код | `detail` | Причина |
|---|---|---|
| 400 | `Недопустимый тип видео. Разрешены: mp4, webm.` | Другое расширение |
| 400 | `Файл пустой.` | Нулевой размер |
| 400 | `Размер файла превышает 2048 МБ.` | Больше `VIDEO_MAX_SIZE_MB` |
| 401 | `Требуется авторизация.` | Нет или битый токен |
| 403 | `Действие доступно только администратору.` | Роль `client` |
| 409 | `Видео с именем «X» уже существует.` | Дубль имени (уникальность среди неудалённых) |
| 422 | — | Нет поля `name` или `file`, пустое `name` |

Практика:

* Проверяйте имя на дубль заранее — `GET /api/videos?name=<имя>` плюс сверка точного совпадения;
  но решение сервера всё равно авторитетно (между проверкой и вставкой возможна гонка),
  поэтому `409` нужно обрабатывать и предлагать переименовать;
* Загружайте файл **потоком** (не читайте в память) — ограничение 2 ГБ это позволяет;
* В nginx выключена буферизация тела запроса (`proxy_request_buffering off`), таймауты по
  неактивности — 300 с, максимальный размер тела — 2048 МБ. Долгая загрузка на медленном канале
  допустима, но пауза в передаче дольше 5 минут может оборвать соединение;
* Сразу после `201` можно вызвать `/playback` и воспроизвести, не дожидаясь ничего ещё.

### 3.7. Воспроизведение: `GET /api/videos/{video_id}/playback`

```json
{
  "url": "http://localhost/cloudmanager-files/videos/3f7c1d2e4b5a69788796a5b4c3d2e1f0.mp4?X-Amz-Algorithm=AWS4-HMAC-SHA256&X-Amz-Credential=...&X-Amz-Date=...&X-Amz-Expires=21600&X-Amz-SignedHeaders=host&X-Amz-Signature=...",
  "content_type": "video/mp4",
  "expires_at": "2026-10-16T19:44:15.123456+00:00",
  "expires_in": 21600
}
```

* `url` — presigned GET-ссылка на S3-хранилище; TTL задаётся `VIDEO_PRESIGN_TTL_SECONDS`
  (по умолчанию `21600` = 6 часов, максимум SigV4 — 7 суток);
* ссылка **same-origin** с API: путь `/cloudmanager-files/...` проксирует nginx, поэтому
  плееру не нужен ни токен, ни CORS;
* nginx включил `proxy_force_ranges on`, поэтому плеер поддерживает Range-запросы: перемотка,
  «продолжить с места», докачка работают;
* **ссылка — носитель доступа**: не логируйте её, не сохраняйте в историю, не показывайте в UI;
* по истечении TTL запрос по ссылке вернёт `403` от S3 — просто вызовите `/playback` ещё раз.

Критичный нюанс для десктопа: **подпись привязана к хосту и пути**. Хост берётся из настройки
сервера `S3_PUBLIC_ENDPOINT` (в dev по умолчанию `http://localhost`, то есть `localhost:80`).
Значит:

* если десктоп-приложение живёт на той же машине, что и стек, — всё работает как есть;
* если клиент на другой машине — администратор сервера должен указать в `.env`
  `S3_PUBLIC_ENDPOINT` как адрес, доступный клиенту (например `http://192.168.1.10:8080`
  или `https://cloudmanager.ru`), причём **с портом шлюза**, если он не 80, и перезапустить
  сервис `api`. Иначе `url` будет указывать на `localhost` клиента, и воспроизведение не заработает
  (подменить хост вручную нельзя — подпись не сойдётся).

В WinUI 3 воспроизведение — одна строка:

```csharp
var playback = await api.GetPlaybackAsync(videoId);
Player.Source = MediaSource.CreateFromUri(new Uri(playback.Url));
```

`MediaPlayerElement` сам отдаст Range-запросы и использует системные кодеки: для `mp4` нужен
поддерживаемый Windows профиль (H.264/AAC — надёжно; HEVC/AV1 — только с установленным кодеком).
`webm` (VP8/VP9) проигрывается только если в системе есть соответствующий кодек-пакет — если
приложению нужна предсказуемость, ориентируйтесь на `mp4`.

### 3.8. Удаление: `DELETE /api/videos/{video_id}` (только admin)

Возвращает `204 No Content` без тела. Удаление мягкое: запись помечается удалённой, объект
удаляется из S3, имя освобождается (его можно занять снова).

## 4. Пример клиента на C# (WinUI 3, .NET 8+)

### 4.1. DTO и настройки JSON

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudManager.Client.Api;

public sealed record RegisterRequest(string Login, string Password);
public sealed record LoginRequest(string Login, string Password);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

public sealed record UserResponse(Guid Id, string Login, string Role, DateTimeOffset CreatedAt);

public sealed record VideoResponse(
    Guid Id, string Name, string ObjectKey, string Type, long Size, DateTimeOffset CreatedAt);

public sealed record VideoPlaybackResponse(
    string Url, string ContentType, DateTimeOffset ExpiresAt, int ExpiresIn);

public sealed record VideoTypeItem(string Value, string ContentType);

public sealed record PagedResponse<T>(
    int Count, int Offset, int Page, int PageSize, IReadOnlyList<T> Data);

internal static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, // access_token, page_size, ...
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
```

### 4.2. Ошибки

```csharp
using System.Net;
using System.Text.Json;

public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode status, string? error, string? detail, string rawBody)
        : base(detail ?? error ?? $"HTTP {(int)status}")
    {
        StatusCode = status;
        Error = error;
        Detail = detail;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string? Error { get; }   // "UniqueConstraintException" и т. п.
    public string? Detail { get; }  // строка; для 422 — сырой JSON массива
    public string RawBody { get; }

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;
    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

    public static ApiException From(HttpResponseMessage response, string body)
    {
        string? error = null, detail = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("error", out var e))
                    error = e.GetString();
                if (doc.RootElement.TryGetProperty("detail", out var d))
                    detail = d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText();
            }
        }
        catch (JsonException) { /* 422, HTML или пустое тело — остаётся RawBody */ }

        return new ApiException(response.StatusCode, error, detail, body);
    }
}
```

### 4.3. Клиент API

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

public sealed class CloudManagerApiClient
{
    private readonly HttpClient _http;
    private string? _token;

    public CloudManagerApiClient(HttpClient http, Uri baseAddress)
    {
        _http = http;
        _http.BaseAddress = baseAddress;
        // Важно: дефолтный Timeout = 100 с рвёт загрузку больших видео.
        _http.Timeout = Timeout.InfiniteTimeSpan; // ограничиваем через CancellationToken
    }

    public void SetToken(string? token) => _token = token;

    // --- низкий уровень -------------------------------------------------

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken ct)
    {
        if (_token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using var response = await _http.SendAsync(request, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode) throw ApiException.From(response, body);

        return body.Length == 0
            ? default!
            : JsonSerializer.Deserialize<T>(body, ApiJson.Options)
              ?? throw new ApiException(response.StatusCode, null, "Пустой ответ", body);
    }

    private Task<T> PostJsonAsync<T>(string path, object payload, CancellationToken ct) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload, options: ApiJson.Options),
        }, ct);

    private Task<T> GetAsync<T>(string path, CancellationToken ct) =>
        SendAsync<T>(new HttpRequestMessage(HttpMethod.Get, path), ct);

    // --- авторизация ----------------------------------------------------

    public Task<UserResponse> RegisterAsync(string login, string password, CancellationToken ct = default)
        => PostJsonAsync<UserResponse>("api/auth/register", new RegisterRequest(login, password), ct);

    public async Task<TokenResponse> LoginAsync(string login, string password, CancellationToken ct = default)
    {
        var token = await PostJsonAsync<TokenResponse>(
            "api/auth/login", new LoginRequest(login, password), ct);

        _token = token.AccessToken;
        return token;
    }

    public Task<UserResponse> GetMeAsync(CancellationToken ct = default)
        => GetAsync<UserResponse>("api/auth/me", ct);

    // --- видео ----------------------------------------------------------

    public Task<PagedResponse<VideoResponse>> GetVideosAsync(
        int page = 1, int pageSize = 25, string? name = null,
        IEnumerable<string>? types = null, CancellationToken ct = default)
    {
        var query = new StringBuilder($"api/videos?page={page}&page_size={pageSize}");
        if (!string.IsNullOrWhiteSpace(name))
            query.Append("&name=").Append(Uri.EscapeDataString(name));
        foreach (var type in types ?? [])
            query.Append("&type=").Append(Uri.EscapeDataString(type));

        return GetAsync<PagedResponse<VideoResponse>>(query.ToString(), ct);
    }

    public Task<IReadOnlyList<VideoTypeItem>> GetVideoTypesAsync(CancellationToken ct = default)
        => GetAsync<IReadOnlyList<VideoTypeItem>>("api/videos/types", ct);

    public Task<VideoResponse> GetVideoByNameAsync(string name, CancellationToken ct = default)
        => GetAsync<VideoResponse>($"api/videos/by-name?name={Uri.EscapeDataString(name)}", ct);

    public async Task<VideoResponse> UploadVideoAsync(
        string name, string filePath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        if (extension is not (".mp4" or ".webm"))
            throw new ArgumentException("Разрешены только .mp4 и .webm", nameof(filePath));

        await using var file = File.OpenRead(filePath);
        if (file.Length == 0) throw new ArgumentException("Файл пустой", nameof(filePath));

        using var content = new MultipartFormDataContent
        {
            { new StringContent(name), "name" },
        };

        var streamContent = new StreamContent(new ProgressStream(file, file.Length, progress));
        streamContent.Headers.ContentType =
            new MediaTypeHeaderValue(extension == ".webm" ? "video/webm" : "video/mp4");
        // имя файла важно: сервер определяет тип видео по расширению
        content.Add(streamContent, "file", Path.GetFileName(filePath));

        using var request = new HttpRequestMessage(HttpMethod.Post, "api/videos") { Content = content };
        return await SendAsync<VideoResponse>(request, ct);
    }

    public Task<VideoPlaybackResponse> GetPlaybackAsync(Guid videoId, CancellationToken ct = default)
        => GetAsync<VideoPlaybackResponse>($"api/videos/{videoId}/playback", ct);

    public async Task DeleteVideoAsync(Guid videoId, CancellationToken ct = default)
    {
        using var response = await _http.SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"api/videos/{videoId}"), ct);
        if (!response.IsSuccessStatusCode)
            throw ApiException.From(response, await response.Content.ReadAsStringAsync(ct));
    }
}
```

### 4.4. Поток с прогрессом загрузки

```csharp
internal sealed class ProgressStream(Stream inner, long total, IProgress<double>? progress) : Stream
{
    private long _transferred;

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        _transferred += read;
        if (total > 0) progress?.Report((double)_transferred / total);
        return read;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => _transferred; set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
```

### 4.5. Использование в странице

```csharp
var api = new CloudManagerApiClient(httpClient, new Uri("http://localhost"));

// вход
var token = await api.LoginAsync(login, password);
store.SaveToken(token.AccessToken, DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn));

// список с фильтром
var page = await api.GetVideosAsync(page: 1, pageSize: 25, name: "промо", types: ["mp4"]);

// загрузка с прогрессом
var progress = new Progress<double>(p => UploadBar.Value = p * 100);
try
{
    var video = await api.UploadVideoAsync(nameBox.Text, pickedFile.Path, progress);
}
catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
{
    // имя занято: предложить другое, ex.Detail уже на русском
}

// воспроизведение
var playback = await api.GetPlaybackAsync(video.Id);
Player.Source = MediaSource.CreateFromUri(new Uri(playback.Url));
```

## 5. Чек-лист интеграции (грабли, которые ловятся на живом клиенте)

1. **`HttpClient.Timeout`** по умолчанию 100 с — большие видео не догрузятся. Ставьте
   `Timeout.InfiniteTimeSpan` и ограничивайте время через `CancellationToken`.
2. **Хост в presigned-ссылке** задаётся сервером (`S3_PUBLIC_ENDPOINT`). Для клиента на другой
   машине админ должен указать доступный ему origin (с портом, если шлюз не на 80).
   Подменять хост в ссылке нельзя — подпись не сойдётся.
3. **401 не равно 403.** 401 — показать вход; 403 — «нужны права администратора».
4. **422** приходит в формате FastAPI (без `error`) — не падайте на разборе `detail`.
5. **429 на логине** — 10 запросов в минуту с одного IP. Никаких авторетраев: покажите сообщение
   и кнопку «Повторить».
6. **Токен живёт 24 ч и не обновляется** — сохраняйте `expires_at` и разлогинивайтесь заранее.
7. **Не логируйте presigned URL** — это фактически ключ доступа к файлу на время TTL.
8. **Дубль имени видео → 409.** Проверяйте заранее, но обрабатывайте и гонку.
9. **Расширение файла решает всё**: сервер смотрит на имя в multipart, а не на `Content-Type`.
10. **Не поллите список чаще, чем нужно**: общий лимит 30 запросов в секунду на IP, а диапазон
    `type` можно указывать повторяющимися параметрами, чтобы не делать несколько запросов.
11. **Плейбек-ссылка истекла** (через 6 ч) → просто запросите `/playback` заново; `403` от S3 —
    это не «нет прав», а истёкшая подпись.

## 6. Приложение: остальные модули API

Клиенту, который планирует выйти за рамки видео, пригодятся (подробности — в `backend/README.md`):

| Метод | Путь | Доступ |
|---|---|---|
| `POST` | `/api/files` | любой авторизованный |
| `GET` | `/api/files` | любой авторизованный |
| `GET` | `/api/files/{id}/download`, `/api/files/{id}/image` | без токена |
| `PATCH` | `/api/files/{id}` | владелец файла или admin (чужой → `403`), тело `{"name": "..."}` |
| `DELETE` | `/api/files/{id}` | владелец файла или admin (чужой → `403`) |
| `POST/GET/PATCH/DELETE` | `/api/folders...` | любой авторизованный (только свои папки) |
| `GET` | `/api/news`, `/api/news/{id}` | без токена |
| `POST/PATCH/DELETE` | `/api/news...` | admin |

### Корневая папка пользователя

Вместе с пользователем регистрация создаёт его **корневую папку** (`is_root: true`,
`parent_id: null`, имя — логин). От неё клиент строит всё дерево:

```http
GET /api/folders?root_only=true
Authorization: Bearer <token>
```

```json
{
  "count": 1,
  "data": [
    { "id": "…", "name": "desktop-user", "parent_id": null, "is_root": true, "files": [], "children": [] }
  ]
}
```

Правила работы с деревом:

* `POST /api/folders` без `parent_id` создаёт папку **внутри корневой** — отдельно передавать её id не нужно;
* `parent_id` другой папки — вложенность; `PATCH` с `parent_id: null` возвращает папку в корневую;
* корневую папку нельзя удалить и нельзя переместить (`400`), переименовать — можно;
* путь от корня до текущей папки строится по `parent_id` вверх до `is_root: true`.

## 7. Настройки сервера, влияющие на клиента

| Переменная | Влияние на клиент |
|---|---|
| `GATEWAY_PORT` | Порт базового URL (по умолчанию 80) |
| `JWT_ACCESS_TOKEN_EXPIRE_MINUTES` | Время жизни токена (по умолчанию 1440 мин = 24 ч) |
| `VIDEO_MAX_SIZE_MB` | Максимальный размер загружаемого видео (по умолчанию 2048 МБ) |
| `VIDEO_PRESIGN_TTL_SECONDS` | TTL ссылки на воспроизведение (по умолчанию 21600 = 6 ч) |
| `S3_PUBLIC_ENDPOINT` | Хост в presigned-ссылке — должен быть доступен клиенту |
| `client_max_body_size` в `nginx/default.conf.template` | Жёсткий предел тела запроса на шлюзе (2048m) |

## 8. Источники (для сверки при изменениях)

* `backend/app/auth/endpoint/auth.py`, `schema/auth.py`, `security.py`, `service/auth.py`, `dependencies.py`, `provider.py`, `filter.py`
* `backend/app/users/schema/users.py`, `service/users.py`, `repository/users.py`
* `backend/app/videos/endpoint/videos.py`, `schema/videos.py`, `service/videos.py`, `repository/videos.py`, `filter.py`, `constants.py`, `model/video.py`
* `backend/app/core/exceptions.py`, `exceptions_handlers.py`, `dependencies.py`, `schemes/paginated_schema.py`, `repositories/base_repository.py`
* `backend/app/health/endpoint/health.py`
* `backend/nginx/default.conf.template`, `backend/nginx/proxy_params.conf`
* `backend/README.md` — общее описание API и переменных окружения
