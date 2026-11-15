using CloudManagerWinUIApp.Localization;
using Microsoft.UI.Xaml.Controls;

namespace CloudManagerWinUIApp.Pages;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
    }

    /// <summary>Строки текущего языка.</summary>
    public Strings L { get; } = Loc.Current;
}
