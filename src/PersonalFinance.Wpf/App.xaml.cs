using System.Windows;

namespace PersonalFinance.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var api = new FinanceApi();
        var login = new LoginWindow(api);
        if (login.ShowDialog() != true) { api.Dispose(); Shutdown(); return; }
        var main = new MainWindow(api);
        MainWindow = main;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        main.Closed += (_, _) => api.Dispose();
        main.Show();
    }
}
