using System.Windows;

namespace NovaSetlist;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Services.UpdateService.CleanupLeftovers();
    }
}
