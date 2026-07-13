using System.Windows;
using NovaSetlist.ViewModels;

namespace NovaSetlist;

public partial class SettingsDialog : Window
{
    public string SpreadsheetId => IdBox.Text;
    public string SongsTabName => SongsTabBox.Text;
    public string LeadersTabName => LeadersTabBox.Text;
    public string TimecodeDevice => TimecodeDeviceBox.SelectedItem as string ?? TimecodeViewModel.OffDevice;
    public string KeyDetectDevice => KeyDeviceBox.SelectedItem as string ?? KeyDetectViewModel.OffDevice;

    public SettingsDialog(MainViewModel vm)
    {
        InitializeComponent();
        Ui.Dwm.UseDarkTitleBar(this);

        IdBox.Text = vm.Config.SpreadsheetId == "PUT_ID_HERE" ? "" : vm.Config.SpreadsheetId;
        SongsTabBox.Text = vm.Config.SongsTab;
        LeadersTabBox.Text = vm.Config.LeadersTab;

        vm.Timecode.RefreshDevices();
        vm.KeyDetect.RefreshDevices();
        TimecodeDeviceBox.ItemsSource = vm.Timecode.Devices;
        TimecodeDeviceBox.SelectedItem = vm.Timecode.SelectedDevice;
        KeyDeviceBox.ItemsSource = vm.KeyDetect.Devices;
        KeyDeviceBox.SelectedItem = vm.KeyDetect.SelectedDevice;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        new AboutDialog { Owner = this }.ShowDialog();
    }
}
