using System.Net;
using System.Text.Json;
using CloudManagerWinUIApp.Localization;

namespace CloudManagerWinUIApp.Services;

/// <summary>
/// Ошибка, пришедшая от API. Тело ошибки разбирается по формату бекенда:
/// <c>{ "error": "UnauthorizedException", "detail": "Требуется авторизация." }</c>.
/// </summary>
public sealed class ApiException : Exception
{
    public ApiException(HttpStatusCode statusCode, string? error, string? detail, string rawBody)
        : base(BuildMessage(statusCode, error, detail))
    {
        StatusCode = statusCode;
        Error = error;
        Detail = detail;
        RawBody = rawBody;
    }

    public HttpStatusCode StatusCode { get; }

    /// <summary>Имя класса исключения бекенда, например <c>UniqueConstraintException</c>.</summary>
    public string? Error { get; }

    /// <summary>Строка с деталями; для 422 — сырой JSON массива FastAPI.</summary>
    public string? Detail { get; }

    public string RawBody { get; }

    /// <summary>Готовый для показа текст ошибки: <c>detail</c>, иначе сообщение исключения.</summary>
    public string DetailOrMessage => string.IsNullOrWhiteSpace(Detail) ? Message : Detail;

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;
    public bool IsForbidden => StatusCode == HttpStatusCode.Forbidden;
    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;
    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;

    /// <summary>
    /// Пользователь заблокирован администратором: <c>403</c> с телом
    /// <c>{"error":"ForbiddenException","detail":"Пользователь заблокирован."}</c>
    /// (так отвечают и <c>/api/auth/login</c>, и любая защищённая ручка с уже выданным
    /// токеном).
    /// <para>
    /// Реакция на этот признак деструктивная (разлогин и удаление сохранённой сессии),
    /// поэтому маркер сравнивается с <b>точными формулировками бекенда</b>, а не с любой
    /// подстрокой «blocked»: иначе <c>403</c> промежуточного шлюза («blocked by policy»)
    /// выкинул бы пользователя из аккаунта.
    /// </para>
    /// <para>
    /// Признак всё равно текстовый и завязан на контракт бекенда: если сервер изменит
    /// формулировку, авторазлогин перестанет срабатывать и останется обычная ошибка
    /// <c>403</c>. Другого машинного признака в ответе нет (см. docs).
    /// </para>
    /// </summary>
    public bool IsBlockedUser => StatusCode == HttpStatusCode.Forbidden && HasBlockedMarker(Detail);

    /// <summary>
    /// Точные формулировки блокировки из контракта: <c>detail</c> начинается с
    /// «Пользователь заблокирован…» либо содержит «user is blocked» / «account is blocked».
    /// </summary>
    private static bool HasBlockedMarker(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return false;

        var text = detail.Trim();

        return text.StartsWith("Пользователь заблокирован", StringComparison.OrdinalIgnoreCase)
               || text.Contains("user is blocked", StringComparison.OrdinalIgnoreCase)
               || text.Contains("account is blocked", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildMessage(HttpStatusCode status, string? error, string? detail)
    {
        var strings = Loc.Current;

        if (status == HttpStatusCode.TooManyRequests)
            return strings.TooManyRequests;

        return detail ?? error ?? strings.ServerError((int)status);
    }

    /// <summary>Разбирает тело ответа бекенда в понятное пользователю сообщение.</summary>
    public static ApiException From(HttpStatusCode status, string body)
    {
        string? error = null;
        string? detail = null;

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                if (doc.RootElement.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String)
                    error = e.GetString();

                if (doc.RootElement.TryGetProperty("detail", out var d))
                    detail = d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText();
            }
        }
        catch (JsonException)
        {
            // 422/HTML/пустое тело — остаётся raw body.
        }

        if (detail is null && error is null && status != HttpStatusCode.TooManyRequests)
        {
            var strings = Loc.Current;

            detail = status switch
            {
                HttpStatusCode.Unauthorized => strings.AuthorizationRequired,
                HttpStatusCode.Forbidden => strings.Forbidden,
                HttpStatusCode.NotFound => strings.NotFound,
                _ => string.IsNullOrWhiteSpace(body) ? strings.ServerError((int)status) : body,
            };
        }

        return new ApiException(status, error, detail, body);
    }
}
