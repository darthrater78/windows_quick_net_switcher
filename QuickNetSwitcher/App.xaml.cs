#nullable enable
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace QuickNetSwitcher;

// One copy of the app per user session. A second launch asks the running copy to come
// to the front, then exits.
//
// Both names are per user and in the session's Local namespace. A program running as
// the same user could create them first and so stop this app from starting -- but such
// a program can already end this one, so the names are not given a separate ACL.
public partial class App : Application
{
    private Mutex? _instance;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? "user";
        _instance = new Mutex(true, $@"Local\QuickNetSwitcher-{user}", out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, $@"Local\QuickNetSwitcher-show-{user}");

        if (!first)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Dispatcher.BeginInvoke(() => window.ShowFromTray()),
            null, Timeout.Infinite, executeOnlyOnce: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        base.OnExit(e);
    }
}
