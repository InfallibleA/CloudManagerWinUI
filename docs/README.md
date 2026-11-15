# cloud_manager-backend

Бэкенд сайта-визитки **cloud_manager** на FastAPI: JWT-авторизация, пользователи, контент витрины и health-пробы.

## Стек

| Слой | Технология |
|---|---|
| Язык | Python 3.14 |
| Web | FastAPI + Uvicorn |
| БД | PostgreSQL 17, SQLAlchemy 2.0 (async) + asyncpg, Alembic |
| Файлы | Garage — S3-совместимое хранилище |
| DI | Dishka (`dishka.integrations.fastapi`) |
| Конфиг | pydantic-settings (`app/config.py`) |
| Качество | Ruff, pre-commit, pytest |

## Структура

```
app/
├── auth/           # регистрация, логин, JWT, текущий пользователь, список пользователей
│   ├── endpoint/   # POST /api/auth/register, /login, GET /me, /users
│   ├── schema/     # схемы запросов/ответов
│   ├── service/    # бизнес-логика аутентификации
│   ├── dependencies.py, provider.py, security.py
├── users/          # модель, репозиторий, сервис, схемы пользователей
│   └── cli.py      # создание администратора из консоли
├── files/          # файлы: S3-хранилище, модель, репозиторий, сервис
│   ├── endpoint/   # POST /api/files, GET /api/files, GET .../download и .../image (без авторизации), DELETE
│   ├── model/      # таблица files: имя, тип, object_key, размер
│   ├── repository/ # запросы к БД и мягкое удаление
│   ├── schema/     # схемы запроса, ответа и фильтров
│   ├── service/    # валидация типа/размера и работа с хранилищем
│   └── storage.py  # асинхронная обёртка над S3-клиентом (Garage) + presigned-ссылки
├── folders/        # папки пользователя: вложенность, файлы внутри, каскадное удаление
│   ├── endpoint/   # POST/GET/PATCH/DELETE /api/folders (только с токеном)
│   ├── model/      # folders (parent_id на себя) + таблица связей folder_files
│   ├── repository/ # выдача своих папок, рекурсивное поддерево, связи с файлами
│   ├── schema/     # схемы создания, обновления, фильтров и ответа
│   ├── service/    # проверка родителя и файлов, удаление ветки вместе с файлами
│   └── filter.py   # query-фильтры списка папок
├── videos/         # видео: загрузка, presigned-ссылка на воспроизведение, список, удаление
│   ├── endpoint/   # POST/GET/DELETE /api/videos, /by-name, /playback и /types
│   ├── model/      # таблица videos: уникальное имя, тип, object_key, размер
│   ├── repository/ # запросы к БД, поиск по имени, мягкое удаление
│   ├── schema/     # схемы создания, фильтров, ответа и плейбека
│   ├── service/    # валидация типа/размера, уникальность имени, работа с хранилищем
│   └── filter.py   # query-фильтры списка видео
├── news/           # новости: модель, репозиторий, сервис, главное изображение
│   ├── endpoint/   # POST/GET/PATCH/DELETE /api/news
│   ├── model/      # таблица news: name, description, weight, media_id
│   ├── repository/ # выдача, обновление и логика веса
│   ├── schema/     # схемы создания, обновления, фильтров и ответа
│   ├── service/    # проверка файлов и работа с новостями
│   └── filter.py   # query-фильтры списка новостей
├── health/         # GET /health (liveness), GET /health/ready (readiness)
├── core/
│   ├── cors.py               # CORSMiddleware: домены сайта и админки
│   ├── engines/database.py   # Transaction: движок и сессии SQLAlchemy
│   ├── models/               # Base, SoftDel и миксины
│   ├── repositories/         # базовые репозитории
│   ├── schemes/              # CloudManagerBaseModel, пагинация, сортировка
│   ├── exceptions*.py        # доменные исключения и их обработчик
│   └── utils/
├── config.py       # типизированные настройки из окружения
├── di.py           # сборка Dishka-контейнера
├── main.py         # точка входа и ASGI-приложение
└── routers.py      # корневой роутер
migration/          # Alembic (env.py, versions/)
tests/              # pytest (asyncio_mode = auto)
```

## Быстрый старт (Docker)

Команды этой инструкции выполняются из папки `backend/`. Общая структура и запуск
из корня описаны в [README монорепозитория](../README.md). Compose использует постоянное
имя проекта `cloud_manager-backend`, чтобы имена томов и сети не зависели от расположения исходников.

```bash
cp example.env .env      # заполните JWT_SECRET_KEY и доступы к БД
docker compose build
docker compose up -d postgres garage migrate api nginx
docker compose run --rm api python -m app.users.cli create-admin # для создания админа
```

* API (через nginx) — http://localhost (порт `GATEWAY_PORT`, по умолчанию `80`)
* Swagger — http://localhost/docs
* S3 API Garage — http://localhost:3900 (порт на хосте — `GARAGE_HOST_PORT`, регион `garage`)

Бакет `cloudmanager-files` и ключ доступа создаются один раз при инициализации хранилища —
см. «Хранилище файлов (Garage)» ниже.

## Локальный запуск

```bash
poetry install
poetry run alembic upgrade head
poetry run uvicorn app.main:app --reload
```

## API

| Метод | Путь | Назначение |
|---|---|---|
| POST | `/api/auth/register` | Регистрация пользователя (роль `client`) и его корневой папки — **без авторизации** |
| POST | `/api/auth/login` | Получение JWT access-токена |
| GET | `/api/auth/users` | Список пользователей: фильтры `login`, `role` и пагинация — **только админ** |
| GET | `/api/auth/me` | Текущий пользователь (нужен `Authorization: Bearer <token>`) |
| POST | `/api/files` | Загрузка файла: поля `name` и `file` (multipart) — любой зарегистрированный пользователь |
| GET | `/api/files` | Список файлов: фильтры `name`, `type` и пагинация `page`, `page_size` |
| GET | `/api/files/{file_id}/download` | Скачивание файла по `object_key` — **без авторизации** |
| GET | `/api/files/{file_id}/image` | Изображение инлайном (`image/png`, `image/gif`, `image/webp`, `image/avif`, `image/jpeg`) для `<img src>` — **без авторизации** |
| PATCH | `/api/files/{file_id}` | Переименование файла: поле `name` — админ (любой файл) или владелец файла |
| DELETE | `/api/files/{file_id}` | Мягкое удаление записи и объекта из хранилища — админ (любой файл) или владелец файла |
| POST | `/api/folders` | Создание папки: `name`, необязательные `parent_id` и `file_ids`; без `parent_id` — внутри корневой — только зарегистрированный пользователь |
| GET | `/api/folders` | Свои папки: фильтры `name`, `parent_id`, `root_only`, `creator_id` (только админ) и пагинация — только зарегистрированный пользователь |
| GET | `/api/folders/{folder_id}` | Папка с файлами и подпапками — только зарегистрированный пользователь |
| PATCH | `/api/folders/{folder_id}` | Изменение `name`, `parent_id` (перенос, `null` — в корневую папку), `file_ids` — только зарегистрированный пользователь |
| DELETE | `/api/folders/{folder_id}` | Удаление папки вместе со всеми подпапками и файлами внутри (корневая — нельзя) — только зарегистрированный пользователь |
| POST | `/api/news` | Создание новости: `name`, `description`, `media_id` — только админ |
| GET | `/api/news` | Список: фильтры `name`, `weight_from`, `weight_to`, пагинация; сортировка по весу — без авторизации |
| GET | `/api/news/{news_id}` | Новость вместе с файлами — без авторизации |
| PATCH | `/api/news/{news_id}` | Изменение `name`, `description`, `weight`, `media_id` — только админ |
| DELETE | `/api/news/{news_id}` | Мягкое удаление новости и её связей — только админ |
| POST | `/api/videos` | Загрузка видео: поля `name` (уникальное) и `file` (multipart) — только админ |
| GET | `/api/videos` | Список видео: фильтры `name`, `type` и пагинация; сначала новые — без авторизации |
| GET | `/api/videos/types` | Справочник типов видео (`mp4`, `webm`) — без авторизации |
| GET | `/api/videos/by-name` | Видео по точному имени: `?name=<строка>` — без авторизации |
| GET | `/api/videos/{video_id}` | Видео по идентификатору — без авторизации |
| GET | `/api/videos/{video_id}/playback` | Presigned-ссылка на воспроизведение — без авторизации |
| DELETE | `/api/videos/{video_id}` | Мягкое удаление записи и объекта из хранилища — только админ |
| GET | `/health` | Liveness-проба |
| GET | `/health/ready` | Readiness-проба (проверка БД) |

Скачивание файла открыто **без токена**: `GET /api/files/{file_id}/download` доступен любому, кто знает
id файла — иначе посетитель сайта не сможет открыть вложение новости. Остальные ручки файлов требуют
заголовок `Authorization: Bearer <token>` (пользователь берётся из того же провайдера, что и у
`/api/auth/me`): список и загрузка — любой зарегистрированной роли; переименование и удаление —
`admin` (любой файл) или владелец файла (`403` — чужой файл, `404` — несуществующий).

`PATCH /api/files/{file_id}` меняет только имя в БД: ключ объекта, тип и размер остаются прежними,
файл в хранилище не перезаписывается.

Показ изображений — тоже **без токена**: `GET /api/files/{file_id}/image` отдаёт картинку с
`Content-Disposition: inline` и заголовками `Cache-Control`/`ETag`, поэтому фронт вставляет её прямо в
`<img src="/api/files/<id>/image">`, а браузер кэширует на сутки и переспрашивает сервер по `ETag`.
Не-изображения (`pdf`, `doc`, `xls` и т.п.) получают `400` — их место в `/download`.

> SVG намеренно не поддерживается: это исполняемый XML, и инлайновая отдача таких файлов
> открывает stored-XSS на домене сайта.

Идентификатор файла — UUIDv4, перебрать его нереально, но ссылка на скачивание фактически публичная:
не загружайте через `/api/files` документы, которые не должны быть доступны по ссылке.

Чтение новостей (`GET /api/news`, `GET /api/news/{news_id}`) доступно **без токена** —
это витрина сайта; создание, изменение и удаление новостей — только для админа.

Разрешённые типы: `pdf`, `doc`, `docx`, `xls`, `xlsx`, `xlsm`, `txt`, `jpeg`, `jpg`, `png`, `gif`,
`webp`, `avif`.
Размер ограничен переменной `FILE_MAX_SIZE_MB`.

## Новости

Новость — это контент витрины: `name`, `description`, `weight` (вес записи) и главное
изображение `media_id`. Своих файлов у новости нет — файлы живут в Garage и управляются
через `/api/files`.

Правила:

* `media_id` — необязательный id файла-картинки (`jpeg`, `jpg`, `png`, `gif`, `webp`, `avif`):
  документ в качестве изображения отклоняется с `400`. Явный `media_id: null` в PATCH снимает
  изображение, отсутствие поля — не меняет его. Удаление файла-изображения обнуляет `media_id`.
* При создании новость встаёт в конец списка: вес = максимальный + 1, у первой новости вес `0`.
  Вес задаёт сервер — поле `weight` в запросе создания не участвует.
  Так «первый всегда 0», а фильтровать можно от 0 до бесконечности (`weight_from`/`weight_to`).
* При смене веса новость «вынимается» со старого места и вставляется на новое: остальные записи
  сдвигаются так, что веса всегда идут подряд `0..N-1`. При удалении новости веса пересчитываются
  заново, поэтому пропусков в последовательности не остаётся.
* Вес за пределами списка отклоняется: если запросить вес больше максимального (например, `4`
  при позициях `0..3`), вернётся `400` с пояснением — «самая большая» новость не может уехать
  ещё дальше.
* Удаление новости ничего не удаляет из хранилища: файлы и изображения остаются в `/api/files`.
* Ответ новости компактный: `id`, `name`, `description`, `weight`, `media_id` — без `created_at`
  и без вложенных файлов.

## Папки (folders)

Папка — личное пространство зарегистрированного пользователя: вложенность любой глубины
(`parent_id` ссылается на ту же таблицу), файлы внутри и каскадное удаление ветки.
Владение определяется полем `creator_id`: его пишет приложение из текущего пользователя
запроса, поэтому «служебный» `ROOT_USER` в папках не появляется.

Корневая папка:

* При регистрации (`POST /api/auth/register`, а также при создании админа из CLI)
  вместе с пользователем создаётся его корневая папка: `is_root = true`,
  `parent_id = null`, имя — логин пользователя. От неё строится все дерево.
* Создать вторую корневую нельзя: без `parent_id` папка автоматически попадает внутрь
  корневой (её id приходит в ответе `parent_id`).
* Найти корневую: `GET /api/folders?root_only=true` — она единственная в выдаче
  и помечена `is_root: true`.
* Удалить корневую или переместить её внутрь другой папки нельзя (`400`);
  переименовать — можно.
* У аккаунтов, созданных до появления корневых папок, `is_root`-папки нет: для них
  поведение прежнее (папки верхнего уровня создаются с `parent_id = null`).

Правила:

* Доступ — только с токеном (любая роль): без `Authorization` все ручки отдают `401`.
  Каждый пользователь видит и меняет **только свои** папки: чужой id отвечает `404`,
  а не `403` — существование чужих папок не подтверждается.
* Фильтр `creator_id` в `GET /api/folders` работает только для админа: он может
  посмотреть папки любого пользователя. Остальным всегда отдаются свои папки,
  а чужой `creator_id` даёт пустой список (не `403` и не `404`).
* `parent_id` проверяется: родитель должен существовать и принадлежать пользователю,
  иначе `400`. Перенос папки в саму себя или внутрь своей подпапки тоже отклоняется —
  вложенность всегда остаётся деревом без циклов.
* `file_ids` проверяются на существование: неизвестный или удалённый файл → `400`
  со списком id. В PATCH это **полный новый набор**: отсутствие поля не меняет связи,
  пустой список вынимает все файлы.
* `PATCH` с `parent_id: null` возвращает папку в корневую папку пользователя; без поля
  родитель не меняется.
* Удаление папки уносит всю ветку: подпапки любого уровня помечаются удалёнными (одной
  записью `soft_del`), а файлы внутри удаляются как при `DELETE /api/files` — запись
  мягко удаляется, объект убирается из S3. Связи `folder_files`
  с этими файлами чистятся, «висячих» записей не остаётся.
* Связь папки с файлом — таблица `folder_files` (`folder_id`, `file_id`) со своими
  `creator_id` и `created_at`: видно, кто именно положил файл в папку.
* Файлы, удалённые через `/api/files/{id}`, исчезают и из выдачи папок, но сами папки
  не трогаются.
* Список и выдача папки отдают только неудалённые файлы (`id`, `name`, `type`, `size`).

## Видео (videos)

Видео лежат в том же бакете Garage, что и файлы, но отдаются браузеру напрямую —
по **presigned-ссылке** с ограниченным временем жизни. Так backend не проксирует
гигабайты через себя, а `<video>` играет с поддержкой Range-запросов (перемотка).

* Загрузка — `POST /api/videos` (multipart: `name` + `file`, только админ). Файл уходит
  в S3 потоком (`FileStorage.upload_stream`, minio грузит частями) — целиком в память
  он не читается. Ключ объекта — `videos/<uuid>.<ext>`.
* Имя видео **уникально** среди неудалённых записей (частичный индекс
  `uq_videos_name_active ... WHERE deleted_id IS NULL`): дубль → `409`. После удаления
  имя можно занять снова.
* `GET /api/videos/by-name?name=...` — точный поиск по имени (регистрозависимо), иначе `404`.
* `GET /api/videos/{id}/playback` — возвращает `url` (presigned GET), `content_type`,
  `expires_at` и `expires_in`. TTL задаётся `VIDEO_PRESIGN_TTL_SECONDS` (по умолчанию
  6 часов, максимум SigV4 — 7 суток). Ссылка — носитель доступа: не логируется и не кэшируется.
* Типы — только `mp4` и `webm`; MIME проставляется при загрузке, поэтому браузер играет файл.
* Удаление — `DELETE /api/videos/{id}` (только админ): мягко помечает запись и удаляет объект.

Подпись привязана к хосту и пути, поэтому presigned-ссылки строит отдельный S3-клиент
с публичным адресом `S3_PUBLIC_ENDPOINT` (origin сайта), а nginx отдаёт их same-origin
(полный конфиг шлюза — `nginx/default.conf.template`):

```nginx
location /cloudmanager-files/ {
    set $garage_s3 ${GARAGE_HOST}:${GARAGE_PORT};  # адрес из .env, путь не меняется — иначе подпись не сойдётся
    proxy_pass http://$garage_s3;                 # через переменную адрес резолвится на каждом запросе
    proxy_set_header Host $http_host;             # хост как в запросе браузера
    proxy_force_ranges on;                        # Range/206 для перемотки
    proxy_buffering off;
}
```

Адрес Garage живёт в одном месте — .env (`GARAGE_HOST`/`GARAGE_PORT`), а не размазан
литералами по конфигам: из этих переменных compose собирает `S3_ENDPOINT` для приложения,
их же entrypoint образа nginx подставляет в шаблон через `envsubst`
(`nginx/default.conf.template` → `/etc/nginx/conf.d/default.conf`). Порт должен совпадать
с `api_bind_addr` в `garage/garage.toml` — Garage настраивается своим файлом и env не читает.

Сам `proxy_pass` идёт через переменную намеренно: с литеральным `proxy_pass http://garage:3900`
nginx резолвит имя один раз при запуске и, если контейнер `garage` в этот момент недоступен
(ещё поднимается или перезапускается), падает с `[emerg] host not found in upstream "garage"`
и уходит в crash-loop. С переменной имя разрешает `resolver 127.0.0.11` прямо в момент
запроса, поэтому шлюз стартует всегда и лишь отдаёт `502`, пока хранилище лежит.

CORS для видео не нужен: браузер обращается к своему же домену. Порты `garage`
(3900/3901/3903) наружу в продакшене публиковать нельзя — доступ только через nginx.

## Несколько экземпляров backend (масштабирование)

Шлюз `nginx` балансирует запросы между репликами сервиса `api`, поэтому поднять
несколько экземпляров backend можно без правки конфига:

```bash
docker compose up -d --scale api=3
```

Что учесть:

* `api` не публикует порт на хост и не имеет `container_name` — это и позволяет
  запускать несколько реплик. Весь трафик идёт через `nginx` (`GATEWAY_PORT`).
* nginx получает список реплик из Docker DNS (`resolver 127.0.0.11`) и обновляет
  его каждые 10 секунд, поэтому новые реплики подхватываются на лету.
* Миграции (`migrate`) и инициализация Garage выполняются один раз, а не на каждую реплику.
* Разделяемого состояния у `api` нет: данные и файлы — в Postgres и Garage.
* `docker compose exec api ...` при нескольких репликах неоднозначен — используйте
  `docker compose run --rm api ...` или `docker compose exec --index=1 api ...`.

## Ограничение частоты запросов (rate limiting)

Лимиты живут на шлюзе `nginx` (`nginx/default.conf.template`), поэтому действуют сразу на все
реплики `api` — in-memory лимитер в приложении при нескольких репликах работал бы
некорректно. Ограничение считается по IP клиента (`$binary_remote_addr`).

| Зона | Что ограничивает | Лимит | Всплеск |
|---|---|---|---|
| `auth_public` | `POST /api/auth/register`, `POST /api/auth/login` | `10 r/m` | `burst=5` |
| `api_all` | всё остальное под `/` | `30 r/s` | `burst=60` |

Превышение возвращает `429` в едином формате ошибок приложения:

```json
{"error": "TooManyRequests", "detail": "Слишком много запросов. Попробуйте позже."}
```

Что учесть:

* Лимиты и `burst` правятся в `nginx/default.conf.template`; после изменения —
  `docker compose build nginx && docker compose up -d nginx`.
* Видео (`/cloudmanager-files/`) и `/nginx-health` не лимитируются.
* Если шлюз стоит за внешним балансировщиком, включите модуль `real_ip` (заготовка
  закомментирована в конфиге), иначе все клиенты получат IP прокси и один общий лимит.
* Rate limiting не заменяет капчу: против распределённых ботов нужна Yandex SmartCaptcha.

> Миграция `0008_videos` рассчитана на голову `0007_product_media`. Если в томе БД
> осталась старая ревизия (например, `0009_video_title_unique`, которой нет в
> `migration/versions`), сначала сбросьте указатель:
> `UPDATE alembic_version SET version_num='0007_product_media';`, удалите остатки старой
> схемы (`DROP TABLE IF EXISTS videos CASCADE; DROP TYPE IF EXISTS video_type;`) и примените
> `alembic upgrade head`.

## Пользователи и роли

Роль хранится в поле `users.role` (enum `user_role`):

| Роль | Возможности |
|---|---|
| `admin` | Удаление любых файлов, просмотр списка пользователей, а также всё, что доступно клиенту |
| `client` | Список и загрузка файлов, переименование и удаление **своих** файлов, свои папки (`/api/folders`); скачивание доступно и без роли |
| без токена | Регистрация, скачивание файла по id, чтение новостей |

Открытая саморегистрация — `POST /api/auth/register`: создаёт пользователя с ролью
`client` и его корневую папку (см. раздел «Папки») и возвращает публичную карточку
пользователя (`201`), повторный логин — `400`.
Администраторы создаются только из консоли, от лица `ROOT_USER` (его id попадает
в `creator_id`). Команда запрашивает логин и пароль (пароль вводится без отображения
и с подтверждением):

```bash
# локально
poetry run python -m app.users.cli create-admin

# в docker compose
docker compose run --rm api python -m app.users.cli create-admin --login admin
```

Логин и пароль проверяются по тем же правилам, что и в схеме `UserCreateSchema`
(логин от 3 символов, пароль от 8).

## Хранилище файлов (Garage)

Файлы лежат в Garage — self-hosted S3-совместимом хранилище на Rust (AGPLv3, без лицензий
и аккаунтов). Конфиг — `garage/garage.toml` (одна нода, `replication_factor = 1`,
регион `garage`), данные — в томах `garage_meta` и `garage_data`.

В образе Garage только бинарник `/garage` без shell, поэтому кластер инициализируется
командами (один раз после первого `docker compose up -d garage`):

```bash
# 1. Назначить роль ноде. `garage node id` печатает `<hex>@<адрес>`, а для layout assign нужен только hex (часть до «@»)
docker compose exec garage /garage layout assign -z dc1 -c 1G <node_hex>
docker compose exec garage /garage layout apply --version 1

# 2. Бакет и ключ доступа с теми же dev-доступами, что в example.env.
#    Имя бакета — только строчные буквы, цифры, «.» и «-» (S3 запрещает подчёркивания), поэтому cloudmanager-files
docker compose exec garage /garage bucket create cloudmanager-files
docker compose exec garage /garage key import --yes -n cloud_manager-app GK636c6f75645f6d616e6167657200000001 636c6f75645f6d616e616765722d6465762d7365637265742d6e6f742d666f722d70726f64
docker compose exec garage /garage bucket allow --read --write --owner cloudmanager-files --key cloud_manager-app
```

Что важно знать:

* Регион в подписи запросов должен совпадать с `s3_region` в конфиге (`garage`) — за это
  отвечает переменная `S3_REGION`; при несовпадении Garage вернёт
  `AuthorizationHeaderMalformed`.
* У ключа приложения **нет** права создавать бакеты: бакет создаётся один раз командой
  выше, а приложение только проверяет его наличие (`FileStorage.check_bucket`).
* Свой ключ: `/garage key create <name>`, секрет виден в `/garage key info --show-secret <name>`.
* Веб-консоли у Garage нет, управление — через `/garage` внутри контейнера.
* В продакшене не публикуйте порты Garage на хост: S3 API (`GARAGE_HOST_PORT`) доступен браузеру
  через nginx, а `3901` (RPC) и `3903` (admin API) — только внутри docker-сети.

Секреты в `garage/garage.toml` — только для локальной разработки, для продакшена
сгенерируйте свои (`openssl rand -hex 32` для `rpc_secret`, `openssl rand -base64 32`
для `admin_token`).

Ошибки возвращаются единым форматом `{"error": "<Класс>", "detail": ...}`.

## CORS

CORS включён сразу (`app/core/cors.py`), чтобы фронтенд сайта и админки не упирался в браузерные
ограничения. По умолчанию `CORS_ORIGINS=*` — запросы из браузера с любого домена разрешены, при этом
авторизация всё равно нужна: токен отправляется заголовком `Authorization`, а не cookies.

Что важно знать:

* Список доменов пишется через запятую: `CORS_ORIGINS=https://cloudmanager.ru,https://www.cloudmanager.ru`.
  Незаданный домен не получает CORS-заголовков — браузер заблокирует ответ.
* `CORS_ALLOW_CREDENTIALS=true` вместе с `CORS_ORIGINS=*` несовместимы (браузер отклонит ответ),
  поэтому приложение падает на старте с понятной ошибкой. Включайте cookies только с явным списком доменов.
* Ответы скачивания файлов отдают `Content-Disposition` и `Content-Length` через `expose-headers` —
  иначе фронтенд не узнает имя файла при скачивании через `fetch`/`XHR`.
* Предполётные `OPTIONS`-запросы кэшируются на 10 минут (`max_age=600`).

## Переменные окружения

Полный список — в `example.env`.

| Переменная | Назначение | По умолчанию |
|---|---|---|
| `DB_SCHEME` | схема подключения к БД | `postgresql+asyncpg` |
| `POSTGRES_HOST`, `POSTGRES_PORT` | адрес и порт БД внутри сети | `postgres`, `5432` |
| `POSTGRES_HOST_PORT` | порт БД, опубликованный на хосте | `5439` |
| `POSTGRES_DB`, `POSTGRES_USER`, `POSTGRES_PASSWORD` | доступы к БД | `cloud_manager`, `cloud_manager_user`, `cloud_manager_password` |
| `DB_POOL_SIZE`, `DB_MAX_OVERFLOW` | параметры пула соединений | `20`, `5` |
| `APP_NAME`, `APP_VERSION` | название и версия в OpenAPI | `cloud_manager`, `1.0` |
| `APP_HOST`, `APP_PORT` | адрес и внутренний порт приложения (в контейнере) | `127.0.0.1`, `8000` |
| `GATEWAY_PORT` | порт nginx-шлюза, публикуемый на хост | `80` |
| `JWT_SECRET_KEY` | ключ подписи JWT (**секрет**, заменить в продакшене) | `dev-only-secret-key-change-me-in-production` |
| `JWT_ALGORITHM` | алгоритм подписи | `HS256` |
| `JWT_ACCESS_TOKEN_EXPIRE_MINUTES` | время жизни access-токена | `1440` |
| `GARAGE_HOST`, `GARAGE_PORT` | адрес S3 API Garage внутри docker-сети (совпадает с `api_bind_addr` в `garage/garage.toml`); из них собирается `S3_ENDPOINT` и прокси nginx | `garage`, `3900` |
| `GARAGE_HOST_PORT` | порт S3 API Garage, публикуемый на хост (для локальной разработки) | `3900` |
| `S3_ENDPOINT` | адрес S3 API Garage (`host:port`); задаётся автоматически как `${GARAGE_HOST}:${GARAGE_PORT}`, вручную нужен только при запуске без Docker | `garage:3900` |
| `S3_REGION` | регион подписи запросов, совпадает с `s3_region` | `garage` |
| `S3_ACCESS_KEY`, `S3_SECRET_KEY` | ключ доступа Garage (**секрет**) | dev-значения из `example.env` |
| `S3_BUCKET` | бакет для файлов | `cloudmanager-files` |
| `S3_SECURE` | использовать HTTPS для S3-хранилища | `false` |
| `S3_PUBLIC_ENDPOINT` | публичный адрес Garage для presigned-ссылок (origin сайта) | `http://localhost` |
| `FILE_MAX_SIZE_MB` | максимальный размер загружаемого файла | `20` |
| `VIDEO_MAX_SIZE_MB` | максимальный размер загружаемого видео | `2048` |
| `VIDEO_PRESIGN_TTL_SECONDS` | время жизни presigned-ссылки на видео, сек (≤ 604800) | `21600` |
| `CORS_ORIGINS` | домены сайта и админки через запятую или `*` | `*` |
| `CORS_ALLOW_CREDENTIALS` | разрешить cookies (**нельзя** вместе с `*`) | `false` |
| `CORS_ALLOW_METHODS` | разрешённые HTTP-методы | `*` |
| `CORS_ALLOW_HEADERS` | разрешённые заголовки запроса | `*` |

## Разработка

```bash
poetry run ruff check --fix .
poetry run ruff format .
poetry run pytest
poetry run alembic revision --autogenerate -m "change"
```

Стиль кода: одинарные кавычки, длина строки 99, сортировка импортов — см. `pyproject.toml`.


## Докер команды напоминалка

# .env поменялся, код — нет
```bash
docker compose up -d
```

# поменялся код / Dockerfile / pyproject.toml → пересобрать образ
```bash
docker compose up -d --build
```

# если compose выпендривается и не видит изменений конфига — принудительно
```bash
docker compose up -d --build --force-recreate
```

# только конкретные сервисы
```bash
docker compose up -d --build postgres garage migrate api
```

# полный перезапуск с нуля (осторожно: -v удаляет тома с БД и Garage)
```bash
docker compose down
docker compose up -d --build
```

# обновить миграции через алембик для докера:
```bash
docker compose exec api alembic upgrade head
```

# создать пользака админа:
```bash
docker compose exec api python -m app.users.cli create-admin
```

## Если контейнер «застрял» после конфликта портов

Признаки:

* `docker compose ps` показывает `Restarting` у `nginx`, в логах —
  `nginx: [emerg] host not found in upstream "garage" in /etc/nginx/conf.d/default.conf`;
* у `postgres` в `docker inspect` видно
  `Bind for 0.0.0.0:5439 failed: port is already allocated`;
* `docker inspect <контейнер>` показывает пустой `NetworkSettings.Networks`, то есть контейнер
  не подключён к сети `cloud_manager-backend_cloud_manager`.

Причина: контейнер создался в момент, когда хост-порт (`GATEWAY_PORT` или
`POSTGRES_HOST_PORT`) уже занимал другой стек (например, `techcode-backend`). Docker оставил
контейнер без рабочей сети и без проброса порта, и после этого ни `docker compose start`,
ни `up -d` его не чинят: compose считает конфиг неизменным и просто перезапускает «битый»
контейнер. Лечится пересозданием:

```bash
docker compose up -d --no-deps --force-recreate postgres nginx
```

`--no-deps` нужен, чтобы не трогать реплики `api` — иначе compose сбросит их число к одной
(нужное число задаётся так: `docker compose up -d --scale api=3`).

Проверка, что всё поднялось:

```bash
curl -s localhost/health/ready                   # 200
curl -s "localhost/api/news?page_size=1"     # 200
docker port cloud_manager_postgres               # 5432/tcp -> 0.0.0.0:5439
```

Хост-порты общие для всех docker-проектов, поэтому если рядом живёт ещё один стек с теми же
дефолтами (`80` и `5439`), задайте этому проекту свои значения в `.env` — `GATEWAY_PORT`
и `POSTGRES_HOST_PORT`.