using System.Text;
using CloudManagerWinUIApp.Localization;
using CloudManagerWinUIApp.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace CloudManagerWinUIApp.Pages;

/// <summary>Таблица для встроенного просмотра и путь папки для возврата после закрытия.</summary>
public sealed record SpreadsheetViewerRequest(Guid FileId, string Name, IReadOnlyList<FolderCrumb> Path);

/// <summary>Строка отображаемого листа таблицы.</summary>
internal sealed record SpreadsheetRow(int RowNumber, IReadOnlyList<string> Cells);

/// <summary>
/// Встроенный просмотр таблиц <c>.xlsx</c>, <c>.xlsm</c>, <c>.xlsb</c>, <c>.xltx</c>, <c>.xltm</c> и <c>.xls</c>.
/// </summary>
public sealed partial class SpreadsheetViewerPage : Page, IFrameBackHandler
{
    private const double RowNumberWidth = 56;
    private const int MaxBytes = 25 * 1024 * 1024;

    private Guid _fileId;
    private string _fileName = string.Empty;
    private IReadOnlyList<FolderCrumb> _returnPath = [];
    private CancellationTokenSource? _loadCts;
    private bool _closed;

    private WorkbookData? _workbook;
    private WorksheetData? _activeSheet;
    private int _activeSheetIndex = -1;
    private double[] _columnWidths = [];

    public SpreadsheetViewerPage()
    {
        InitializeComponent();
    }

    public Strings L { get; } = Loc.Current;

    public event EventHandler? BackStateChanged
    {
        add { }
        remove { }
    }

    public bool CanGoBack => Frame is not null;

    public Task<bool> GoBackAsync()
    {
        if (Frame is null)
            return Task.FromResult(false);

        Close();
        return Task.FromResult(true);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is not SpreadsheetViewerRequest request)
            return;

        _fileId = request.FileId;
        _fileName = request.Name;
        _returnPath = request.Path;
        TitleText.Text = request.Name;

        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        CancelLoad();
    }

    private async Task LoadAsync()
    {
        CancelLoad();
        _closed = false;
        var cts = new CancellationTokenSource();
        _loadCts = cts;

        SetBusy(true);
        StatusBar.IsOpen = false;
        CopyButton.IsEnabled = false;

        try
        {
            var bytes = await AppServices.Api.DownloadFileBytesAsync(_fileId, MaxBytes, cts.Token);
            var workbook = await Task.Run(() => SpreadsheetReader.Read(bytes, _fileName), cts.Token);

            if (_closed || cts.IsCancellationRequested)
                return;

            _workbook = workbook;
            PopulateSheetTabs(workbook.Sheets);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ApiException ex)
        {
            if (!_closed)
                ShowNotice(ex.Message, InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            if (!_closed)
                ShowNotice(Loc.Current.SpreadsheetLoadFailed(ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            if (!_closed)
                SetBusy(false);
        }
    }

    private void PopulateSheetTabs(IReadOnlyList<WorksheetData> sheets)
    {
        SheetSelector.Items.Clear();
        _activeSheetIndex = -1;

        if (sheets.Count == 0)
        {
            SheetTabsScroller.Visibility = Visibility.Collapsed;
            ShowSheet(null);
            return;
        }

        for (var i = 0; i < sheets.Count; i++)
        {
            SheetSelector.Items.Add(new SelectorBarItem
            {
                Text = sheets[i].Name,
                Tag = i,
            });
        }

        SheetTabsScroller.Visibility = Visibility.Visible;
        SheetSelector.SelectedItem = SheetSelector.Items[0];
        SelectSheet(0);
    }

    private void SheetSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (sender.SelectedItem?.Tag is int index)
            SelectSheet(index);
    }

    private void SelectSheet(int index)
    {
        if (_workbook is null || index == _activeSheetIndex || (uint)index >= (uint)_workbook.Sheets.Count)
            return;

        _activeSheetIndex = index;
        ShowSheet(_workbook.Sheets[index]);
    }

    private void ShowSheet(WorksheetData? sheet)
    {
        _activeSheet = sheet;

        if (sheet is null || sheet.Rows.Count == 0 || sheet.ColumnCount == 0)
        {
            _columnWidths = [];
            HeaderRowGrid.Children.Clear();
            HeaderRowGrid.ColumnDefinitions.Clear();
            RowsList.ItemsSource = null;
            TableGrid.Width = double.NaN;
            EmptyText.Visibility = Visibility.Visible;
            CopyButton.IsEnabled = false;
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        CopyButton.IsEnabled = true;

        _columnWidths = ComputeColumnWidths(sheet);
        TableGrid.Width = RowNumberWidth + _columnWidths.Sum();
        BuildHeaderRow(_columnWidths);

        var rows = new List<SpreadsheetRow>(sheet.Rows.Count);
        for (var r = 0; r < sheet.Rows.Count; r++)
            rows.Add(new SpreadsheetRow(r + 1, sheet.Rows[r]));

        RowsList.ItemsSource = rows;

        if (sheet.IsTruncated)
            ShowNotice(Loc.Current.SpreadsheetTruncated(SpreadsheetReader.MaxRowsPerSheet), InfoBarSeverity.Warning);
        else if (StatusBar.Severity == InfoBarSeverity.Warning)
            StatusBar.IsOpen = false;
    }

    private static double[] ComputeColumnWidths(WorksheetData sheet)
    {
        var widths = new double[sheet.ColumnCount];
        var sampleRows = Math.Min(sheet.Rows.Count, 200);

        for (var c = 0; c < sheet.ColumnCount; c++)
        {
            var maxChars = ColumnIndexToName(c).Length + 1;
            for (var r = 0; r < sampleRows; r++)
            {
                var row = sheet.Rows[r];
                if (c < row.Count && row[c].Length > maxChars)
                    maxChars = row[c].Length;
            }
            widths[c] = Math.Clamp(maxChars * 7.8 + 24, 76, 280);
        }

        return widths;
    }

    private void BuildHeaderRow(double[] widths)
    {
        HeaderRowGrid.Children.Clear();
        HeaderRowGrid.ColumnDefinitions.Clear();

        HeaderRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RowNumberWidth) });
        HeaderRowGrid.Children.Add(CreateCellElement("#", isHeader: true, isRowNumber: true, column: 0));

        for (var c = 0; c < widths.Length; c++)
        {
            HeaderRowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(widths[c]) });
            HeaderRowGrid.Children.Add(CreateCellElement(ColumnIndexToName(c), isHeader: true, isRowNumber: false, column: c + 1));
        }
    }

    private void RowsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.Item is not SpreadsheetRow row)
            return;

        if (args.ItemContainer.ContentTemplateRoot is not Grid rowGrid)
            return;

        var expectedCols = _columnWidths.Length + 1;
        if (rowGrid.ColumnDefinitions.Count != expectedCols)
            ConfigureRowGrid(rowGrid, expectedCols);

        rowGrid.Background = row.RowNumber % 2 == 0
            ? TryGetBrush("SubtleFillColorTertiaryBrush")
            : null;

        for (var col = 0; col < expectedCols; col++)
        {
            if (rowGrid.Children[col] is Border { Child: TextBlock tb })
            {
                tb.Text = col == 0
                    ? row.RowNumber.ToString()
                    : (col - 1 < row.Cells.Count ? row.Cells[col - 1] : string.Empty);
            }
        }
    }

    private void ConfigureRowGrid(Grid grid, int totalColumns)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RowNumberWidth) });
        grid.Children.Add(CreateCellElement(string.Empty, isHeader: false, isRowNumber: true, column: 0));

        for (var c = 1; c < totalColumns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(_columnWidths[c - 1]) });
            grid.Children.Add(CreateCellElement(string.Empty, isHeader: false, isRowNumber: false, column: c));
        }
    }

    private static Border CreateCellElement(string text, bool isHeader, bool isRowNumber, int column)
    {
        var borderBrush = TryGetBrush("CardStrokeColorDefaultBrush");
        var secondaryText = TryGetBrush("TextFillColorSecondaryBrush");

        var tb = new TextBlock
        {
            Text = text,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = !isHeader && !isRowNumber,
        };

        if (isHeader)
        {
            tb.FontWeight = FontWeights.SemiBold;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
        }
        else if (isRowNumber)
        {
            if (secondaryText is not null)
                tb.Foreground = secondaryText;
            tb.HorizontalAlignment = HorizontalAlignment.Right;
        }

        var border = new Border
        {
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(0, 0, 1, 1),
            Padding = new Thickness(8, 6, 8, 6),
            Child = tb,
        };

        if (isRowNumber && !isHeader)
            border.Background = TryGetBrush("SubtleFillColorSecondaryBrush");

        Grid.SetColumn(border, column);
        return border;
    }

    private static Brush? TryGetBrush(string resourceKey)
        => Application.Current.Resources.TryGetValue(resourceKey, out var value) && value is Brush brush
            ? brush
            : null;

    private static string ColumnIndexToName(int index)
    {
        var sb = new StringBuilder();
        var n = index;
        while (n >= 0)
        {
            sb.Insert(0, (char)('A' + (n % 26)));
            n = (n / 26) - 1;
        }
        return sb.ToString();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_activeSheet is null)
            return;

        var sb = new StringBuilder();
        foreach (var row in _activeSheet.Rows)
            sb.AppendLine(string.Join('\t', row));

        var package = new DataPackage();
        package.SetText(sb.ToString().TrimEnd('\r', '\n'));
        Clipboard.SetContent(package);
        ShowNotice(Loc.Current.SheetCopied, InfoBarSeverity.Success);
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true);
        try
        {
            var path = await FileSaver.DownloadAsync(_fileId, _fileName);
            if (path is not null)
                ShowNotice($"{Loc.Current.FileSaved}: {path}", InfoBarSeverity.Success);
        }
        catch (ApiException ex)
        {
            ShowNotice(ex.Message, InfoBarSeverity.Error);
        }
        catch (Exception ex)
        {
            ShowNotice(Loc.Current.SpreadsheetLoadFailed(ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _ = GoBackAsync();

    private void Close()
    {
        if (Frame is null)
            return;

        CancelLoad();
        Frame.Navigate(typeof(FilesPage), new FolderPathRequest(_returnPath));

        var backStack = Frame.BackStack;
        if (backStack.Count > 0 && backStack[^1].SourcePageType == typeof(SpreadsheetViewerPage))
            backStack.RemoveAt(backStack.Count - 1);
    }

    private void CancelLoad()
    {
        _closed = true;
        var cts = _loadCts;
        _loadCts = null;
        if (cts is null)
            return;

        cts.Cancel();
        cts.Dispose();
    }

    private void SetBusy(bool busy)
    {
        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowNotice(string message, InfoBarSeverity severity)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }
}
