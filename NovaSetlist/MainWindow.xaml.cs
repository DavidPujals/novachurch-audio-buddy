using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NovaSetlist.Models;
using NovaSetlist.ViewModels;

namespace NovaSetlist;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        Ui.Dwm.UseDarkTitleBar(this);
        DataContext = _vm;
        Loaded += async (_, _) => await _vm.InitializeAsync();
        SizeChanged += (_, _) =>
        {
            // Breakpoints = the previous fixed minimums: below them the layout adapts.
            _vm.IsNarrow = ActualWidth < 758;
            _vm.IsShort = ActualHeight < 448;
        };
    }

    // ---------- search autocomplete ----------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchPopup.IsOpen = _vm.SearchResults.Count > 0 && SearchBox.IsKeyboardFocusWithin;
        if (SearchPopup.IsOpen)
            SearchList.SelectedIndex = 0;
    }

    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when SearchPopup.IsOpen:
                SearchList.SelectedIndex = Math.Min(SearchList.SelectedIndex + 1, SearchList.Items.Count - 1);
                SearchList.ScrollIntoView(SearchList.SelectedItem);
                e.Handled = true;
                break;

            case Key.Up when SearchPopup.IsOpen:
                SearchList.SelectedIndex = Math.Max(SearchList.SelectedIndex - 1, 0);
                SearchList.ScrollIntoView(SearchList.SelectedItem);
                e.Handled = true;
                break;

            case Key.Enter:
                if (SearchPopup.IsOpen && SearchList.SelectedItem is Song selected)
                    _vm.AddSong(selected);
                else
                    _vm.AddTopMatch();
                SearchPopup.IsOpen = false;
                e.Handled = true;
                break;

            case Key.Escape:
                SearchPopup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Don't close while the click is landing inside the popup's list.
        if (!SearchPopup.IsKeyboardFocusWithin)
            SearchPopup.IsOpen = false;
    }

    private void SearchList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: Song song })
        {
            _vm.AddSong(song);
            SearchPopup.IsOpen = false;
            SearchBox.Focus();
        }
    }

    // ---------- manual add ----------

    private void AddManually_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddSongDialog { Owner = this };
        if (dialog.ShowDialog() == true)
            _vm.AddManualSong(dialog.SongName, dialog.SongKey);
    }

    // ---------- row editing ----------

    private void EditRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SetItemViewModel item })
            item.IsEditing = !item.IsEditing;
    }

    private void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string token } swatch &&
            FindRowItem(swatch) is { } item)
            item.Color = token;
    }

    /// <summary>Walks up the visual tree to the row's SetItemViewModel.</summary>
    private static SetItemViewModel? FindRowItem(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is FrameworkElement { DataContext: SetItemViewModel item })
                return item;
            d = VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    // ---------- drag-to-reorder via the row number ----------

    private SetItemViewModel? _dragItem;
    private bool _dragActive;
    private Point _dragStart;

    private void RowGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SetItemViewModel item } grip)
        {
            _dragItem = item;
            _dragActive = false;
            _dragStart = e.GetPosition(SetlistItems);
            grip.CaptureMouse();
            e.Handled = true;
        }
    }

    private void RowGrip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragItem is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(SetlistItems);
        if (!_dragActive && Math.Abs(pos.Y - _dragStart.Y) < 4)
            return; // ignore accidental jiggles until a real drag starts
        _dragActive = true;

        var target = RowIndexAt(pos.Y);
        var current = _vm.Items.IndexOf(_dragItem);
        if (target >= 0 && current >= 0 && target != current)
            _vm.Items.Move(current, target);
    }

    private void RowGrip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // A press that never crossed the drag threshold is a click: toggle "completed".
        if (!_dragActive && _dragItem is { } item)
            item.IsCompleted = !item.IsCompleted;
        (sender as FrameworkElement)?.ReleaseMouseCapture();
        _dragItem = null;
        _dragActive = false;
    }

    private void RowGrip_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _dragItem = null;
        _dragActive = false;
    }

    /// <summary>The item index whose row contains the given y (relative to the list), or -1.</summary>
    private int RowIndexAt(double y)
    {
        for (var i = 0; i < SetlistItems.Items.Count; i++)
        {
            if (SetlistItems.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement row)
            {
                var top = row.TranslatePoint(default, SetlistItems).Y;
                if (y >= top && y < top + row.ActualHeight)
                    return i;
            }
        }
        return -1;
    }

    // ---------- compact-mode menu + row context menu ----------

    private SearchDialog? _searchDialog;

    private void MenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.IsOpen = true;
        }
    }

    private void MenuAddSongs_Click(object sender, RoutedEventArgs e)
    {
        if (_searchDialog is { IsVisible: true })
        {
            _searchDialog.Activate();
            return;
        }
        _searchDialog = new SearchDialog(_vm) { Owner = this };
        _searchDialog.Show();
    }

    private void RowMenuEdit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SetItemViewModel item })
            item.IsEditing = !item.IsEditing;
    }

    private void RowMenuRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SetItemViewModel item })
            _vm.RemoveCommand.Execute(item);
    }

    // ---------- settings ----------

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(_vm) { Owner = this };
        if (dialog.ShowDialog() != true)
            return;
        _vm.Timecode.SelectedDevice = dialog.TimecodeDevice;
        _vm.KeyDetect.SelectedDevice = dialog.KeyDetectDevice;
        await _vm.ApplySheetSettingsAsync(dialog.SpreadsheetId, dialog.SongsTabName, dialog.LeadersTabName);
    }

    protected override void OnClosed(EventArgs e)
    {
        _vm.FlushPendingSave();
        _vm.Timecode.Dispose();
        _vm.KeyDetect.Dispose();
        base.OnClosed(e);
    }
}
