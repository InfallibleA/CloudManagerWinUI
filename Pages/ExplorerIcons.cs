using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace CloudManagerWinUIApp.Pages;

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

    private static readonly SolidColorBrush FolderBrush = new(Color.FromArgb(255, 0xF0, 0xB3, 0x4A));
    private static readonly SolidColorBrush FileBrush = new(Color.FromArgb(255, 0x8C, 0x9A, 0xAC));

    /// <summary>Глиф элемента: папка или файл.</summary>
    public static string GlyphFor(bool isFolder) => isFolder ? FolderGlyph : FileGlyph;

    /// <summary>Кисть значка: янтарная у папки, серо-синяя у файла.</summary>
    public static SolidColorBrush BrushFor(bool isFolder) => isFolder ? FolderBrush : FileBrush;
}
