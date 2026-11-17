using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CloudManagerWinUIApp.Pages;

/// <summary>Тип элемента сетки файлового менеджера.</summary>
public enum ExplorerItemKind
{
    Folder,
    File,
    Text,
    Spreadsheet,
    Video,
    Audio,
}

/// <summary>
/// Значки и цвета папок и файлов проводника. Общие для файлового менеджера
/// (<see cref="ExplorerItem"/>) и админ-панели (<see cref="AdminExplorerRow"/>),
/// чтобы цвета и глифы не разъезжались при будущих правках.
/// </summary>
internal static class ExplorerIcons
{
    /// <summary>Глиф папки.</summary>
    public const string FolderGlyph = "\uE8B7";

    /// <summary>Глиф файла.</summary>
    public const string FileGlyph = "\uE7C3";

    /// <summary>Глиф текстового файла — открывается во встроенном просмотре.</summary>
    public const string TextGlyph = "\uE8A5";

    /// <summary>Глиф таблицы (Excel) — открывается во встроенном просмотре таблиц.</summary>
    public const string SpreadsheetGlyph = "\uE9F9";

    /// <summary>Глиф видео — сразу видно, что элемент открывается в плеере.</summary>
    public const string VideoGlyph = "\uE768";

    /// <summary>Глиф аудио — открывается во встроенном аудиоплеере.</summary>
    public const string AudioGlyph = "\uE8D6";

    private static readonly SolidColorBrush FolderBrush = new(Color.FromArgb(255, 0xF0, 0xB3, 0x4A));
    private static readonly SolidColorBrush FileBrush = new(Color.FromArgb(255, 0x8C, 0x9A, 0xAC));
    private static readonly SolidColorBrush SpreadsheetBrush = new(Color.FromArgb(255, 0x2E, 0x9E, 0x5B));
    private static readonly SolidColorBrush VideoBrush = new(Color.FromArgb(255, 0x5B, 0x9D, 0xF0));
    private static readonly SolidColorBrush AudioBrush = new(Color.FromArgb(255, 0xA6, 0x6E, 0xE8));

    /// <summary>Глиф элемента по его типу.</summary>
    public static string GlyphFor(ExplorerItemKind kind) => kind switch
    {
        ExplorerItemKind.Folder => FolderGlyph,
        ExplorerItemKind.Video => VideoGlyph,
        ExplorerItemKind.Audio => AudioGlyph,
        ExplorerItemKind.Spreadsheet => SpreadsheetGlyph,
        ExplorerItemKind.Text => TextGlyph,
        _ => FileGlyph,
    };

    /// <summary>Кисть значка по его типу.</summary>
    public static SolidColorBrush BrushFor(ExplorerItemKind kind) => kind switch
    {
        ExplorerItemKind.Folder => FolderBrush,
        ExplorerItemKind.Video => VideoBrush,
        ExplorerItemKind.Audio => AudioBrush,
        ExplorerItemKind.Spreadsheet => SpreadsheetBrush,
        _ => FileBrush,
    };
}
