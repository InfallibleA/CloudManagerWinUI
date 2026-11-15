namespace CloudManagerWinUIApp.Services;

/// <summary>
/// Сессия пользователя: вход, регистрация, выход и автоматическое продление JWT.
/// <para>
/// Refresh-токена у бекенда нет, поэтому рядом с токеном шифрованно хранятся
/// логин и пароль: когда срок жизни токена подходит к концу (или сервер ответил
/// 401), сервис молча выполняет повторный <c>/api/auth/login</c> и пользователь
/// остаётся в приложении.
/// </para>
/// </summary>
public sealed class AuthService
{
    /// <summary>За сколько до истечения токена выполняется плановое продление.</summary>
    private static readonly TimeSpan RenewBefore = TimeSpan.FromMinutes(10);

    /// <summary>Минимальный интервал между повторными входами (лимит на сервере — 10/мин).</summary>
    private static readonly TimeSpan MinReloginInterval = TimeSpan.FromSeconds(15);

    private readonly CloudManagerApiClient _api;
    private readonly CredentialStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Timer? _timer;
    private string? _login;
    private string? _password;
    private DateTimeOffset _lastReloginUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// Логин, который пользователь вводит в диалоге входа прямо сейчас (только вокруг
    /// <c>/api/auth/login</c>). Нужен, чтобы обработчик <see cref="CloudManagerApiClient.UserBlocked"/>
    /// отличал «заблокирован аккаунт текущей сессии» от «заблокирован аккаунт, в который
    /// пытаются войти»: во втором случае живую сессию трогать нельзя.
    /// </summary>
    private string? _foregroundLogin;

    /// <summary>Событие <see cref="Blocked"/> уже поднято для текущей блокировки.</summary>
    private bool _blockedNotified;

    public AuthService(CloudManagerApiClient api, CredentialStore store)
    {
        _api = api;
        _store = store;
        _api.TokenRefresher = TryRenewAsync;
        _api.SessionLost += (_, _) => ForgotSession();
        _api.UserBlocked += OnUserBlocked;
    }

    /// <summary>Срабатывает при входе, выходе и обновлении данных пользователя.</summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Аккаунт заблокирован администратором: сервер ответил <c>403</c> «Пользователь
    /// заблокирован.» на защищённую ручку или отказал во входе. Сессия к этому моменту
    /// уже забыта, причина — в <see cref="BlockedReason"/>.
    /// <para>
    /// Событие поднимается <b>только если работающая сессия действительно была завершена</b>:
    /// отказ во входе без сессии (и попытка войти другим аккаунтом при живой сессии) сюда
    /// не попадает — причину в этом случае показывает диалог входа.
    /// </para>
    /// </summary>
    public event EventHandler? Blocked;

    /// <summary>
    /// Причина блокировки — текст <c>detail</c> из ответа сервера; <c>null</c>, если
    /// аккаунт не заблокирован. Заполняется и при неудачной попытке входа, поэтому
    /// после перезапуска приложения блокировка тоже видна (см. <see cref="InitializeAsync"/>).
    /// Признак блокировки текстовый — см. <see cref="ApiException.IsBlockedUser"/>.
    /// </summary>
    public string? BlockedReason { get; private set; }

    public UserResponse? User { get; private set; }

    public bool IsAuthenticated => User is not null;

    public string Login => User?.Login ?? string.Empty;

    public bool IsAdmin => string.Equals(User?.Role, "admin", StringComparison.OrdinalIgnoreCase);

    public DateTimeOffset? TokenExpiresAt { get; private set; }

    /// <summary>Восстанавливает сессию из хранилища при запуске приложения.</summary>
    public async Task InitializeAsync()
    {
        var saved = _store.Load();
        if (saved is null)
            return;

        _login = saved.Login;
        _password = saved.Password;
        _api.Token = saved.AccessToken;
        TokenExpiresAt = saved.ExpiresAt;

        if (saved.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            try
            {
                var me = await _api.GetMeAsync().ConfigureAwait(false);

                // Токен мог быть обновлён обработчиком 401 во время запроса.
                if (!string.IsNullOrEmpty(_api.Token))
                    ApplyUser(me, _api.Token, TokenExpiresAt ?? saved.ExpiresAt, save: true);

                return;
            }
            catch (ApiException)
            {
                // Токен больше не принимается — попробуем перелогиниться ниже.
                // Если причина — блокировка, обработчик UserBlocked уже забыл сессию
                // и заполнил BlockedReason, поэтому TryRenewAsync ниже сразу выйдет:
                // пользователь останется неавторизованным, а причина — на экране.
            }
            catch (Exception)
            {
                return;
            }
        }

        await TryRenewAsync(CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Вход по логину и паролю.</summary>
    public async Task LoginAsync(string login, string password, CancellationToken ct = default)
    {
        TokenResponse token;

        // Пока идёт интерактивный вход, обработчик UserBlocked должен знать, что
        // 403 относится к вводимому аккаунту, а не к текущей сессии (см. OnUserBlocked).
        _foregroundLogin = login;
        try
        {
            token = await _api.LoginAsync(login, password, ct).ConfigureAwait(false);
        }
        catch (ApiException ex) when (ex.IsBlockedUser)
        {
            // Бекенд отвечает 403 заблокированному пользователю даже с верным паролем.
            // Событие Blocked здесь не поднимается: сессии ещё нет (или это чужой аккаунт),
            // поэтому баннер «вы вышли из аккаунта» был бы неверным — причину покажет диалог.
            BlockedReason = ex.DetailOrMessage;
            throw;
        }
        finally
        {
            _foregroundLogin = null;
        }

        // Токен обязателен до запроса /me, иначе тот ответит 401.
        _api.Token = token.AccessToken;
        _login = login;
        _password = password;

        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
        var me = await _api.GetMeAsync(ct).ConfigureAwait(false);
        ApplyUser(me, token.AccessToken, expiresAt, save: true);
    }

    /// <summary>Регистрация и сразу вход (бекенд токен при регистрации не выдаёт).</summary>
    public async Task RegisterAsync(string login, string password, CancellationToken ct = default)
    {
        await _api.RegisterAsync(login, password, ct).ConfigureAwait(false);
        await LoginAsync(login, password, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Выход: серверного отзыва токена нет, поэтому просто забываем сессию.
    /// Ручной выход снимает и признак блокировки, чтобы старая причина не всплывала.
    /// </summary>
    public void Logout()
    {
        BlockedReason = null;
        _blockedNotified = false;
        ForgetSessionWithCredentials();
        _store.Clear();
    }

    /// <summary>Проверяет срок жизни токена и при необходимости продлевает его заранее.</summary>
    public async Task RenewIfNeededAsync()
    {
        if (!IsAuthenticated || TokenExpiresAt is null)
            return;

        if (TokenExpiresAt - DateTimeOffset.UtcNow > RenewBefore)
            return;

        await TryRenewAsync(CancellationToken.None).ConfigureAwait(false);
    }

    // --- Внутреннее -------------------------------------------------------

    /// <summary>Молчаливый повторный вход. Используется таймером и обработчиком 401.</summary>
    private async Task<bool> TryRenewAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_login) || string.IsNullOrEmpty(_password))
            return false;

        // Если продление уже идёт (в том числе вызванное из обработчика 401 этого же
        // запроса), не ждём семафор — иначе получится самоблокировка.
        if (!await _gate.WaitAsync(0, ct).ConfigureAwait(false))
            return IsAuthenticated;

        try
        {
            if (DateTimeOffset.UtcNow - _lastReloginUtc < MinReloginInterval)
                return IsAuthenticated;

            _lastReloginUtc = DateTimeOffset.UtcNow;

            var token = await _api.LoginAsync(_login, _password, ct).ConfigureAwait(false);
            _api.Token = token.AccessToken;

            var expiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            var me = await _api.GetMeAsync(ct).ConfigureAwait(false);
            ApplyUser(me, token.AccessToken, expiresAt, save: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ApplyUser(UserResponse user, string token, DateTimeOffset expiresAt, bool save)
    {
        User = user;
        _api.Token = token;
        TokenExpiresAt = expiresAt;

        // Успешный вход (в том числе молчаливое продление) снимает признак блокировки.
        BlockedReason = null;
        _blockedNotified = false;

        if (save && _login is not null && _password is not null)
        {
            _store.Save(new StoredSession(_login, _password, token, expiresAt, user.Id, user.Role));
        }

        StartTimer();
        RaiseStateChanged();
    }

    /// <summary>
    /// Реакция на <c>403</c> «Пользователь заблокирован.» от любой ручки.
    /// <para>
    /// Если 403 пришёл на интерактивную попытку войти <b>другим</b> аккаунтом
    /// (<see cref="_foregroundLogin"/> задан и не совпадает с логином текущей сессии),
    /// рабочую сессию не трогаем: запоминаем причину и выходим — иначе блокировка чужого
    /// аккаунта выкинула бы текущего пользователя и удалила его сохранённые креды.
    /// </para>
    /// <para>
    /// В остальных случаях забываем сессию целиком (токен, сохранённые логин/пароль,
    /// таймер, файл сессии), чтобы приложение не пыталось молча переавторизоваться,
    /// и сообщаем UI — но только если сессия действительно была.
    /// </para>
    /// </summary>
    private void OnUserBlocked(object? sender, ApiException ex)
    {
        BlockedReason = ex.DetailOrMessage;

        // Логины сравниваем без учёта регистра: бекенд считает «Alice» и «alice» одним
        // аккаунтом, поэтому повторный вход в свою же сессию — не «чужой» случай.
        if (_foregroundLogin is not null
            && !string.Equals(_foregroundLogin, _login, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var hadSession = IsAuthenticated || !string.IsNullOrEmpty(_api.Token);
        ForgetSessionWithCredentials();
        _store.Clear();

        if (hadSession)
            NotifyBlocked();
    }

    /// <summary>Событие <see cref="Blocked"/> поднимается один раз на одну блокировку.</summary>
    private void NotifyBlocked()
    {
        if (_blockedNotified)
            return;

        _blockedNotified = true;
        Blocked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Забывает сессию целиком: пользователя, токен, таймер, логин и пароль.</summary>
    private void ForgetSessionWithCredentials()
    {
        _login = null;
        _password = null;
        ForgotSession();
    }

    private void ForgotSession()
    {
        User = null;
        TokenExpiresAt = null;
        _api.Token = null;
        _timer?.Dispose();
        _timer = null;
        RaiseStateChanged();
    }

    private void StartTimer()
    {
        _timer ??= new Timer(_ => _ = RenewIfNeededAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
