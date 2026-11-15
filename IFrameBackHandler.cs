namespace CloudManagerWinUIApp;

/// <summary>
/// Страница сама управляет кнопкой «Назад» в заголовке окна
/// (например, поднимает на уровень выше внутри своего раздела).
/// </summary>
public interface IFrameBackHandler
{
    /// <summary>Доступен ли переход назад прямо сейчас.</summary>
    bool CanGoBack { get; }

    /// <summary>Выполняет переход назад. Возвращает <c>false</c>, если переходить некуда.</summary>
    Task<bool> GoBackAsync();

    /// <summary>Состояние «можно назад» изменилось (например, вошли в папку).</summary>
    event EventHandler? BackStateChanged;
}
