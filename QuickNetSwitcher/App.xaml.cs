#nullable enable
using System;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace QuickNetSwitcher;

// One copy of the app per user session. A second launch asks the running copy to come
// to the front, then exits.
//
// The app always runs elevated, so both named objects are created with an ACL that
// admits only Administrators and SYSTEM: a program running unelevated can neither
// signal the running copy nor hold the names open. A name created first by someone
// else -- squatting, to stop the app starting -- is recognised by its owner not being
// Administrators or SYSTEM, and ignored: the app starts anyway.
public partial class App : Application
{
    private static readonly SecurityIdentifier Administrators = new(WellKnownSidType.BuiltinAdministratorsSid, null);
    private static readonly SecurityIdentifier LocalSystem = new(WellKnownSidType.LocalSystemSid, null);

    private Mutex? _instance;
    private EventWaitHandle? _showSignal;
    private RegisteredWaitHandle? _showWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        var user = WindowsIdentity.GetCurrent().User?.Value ?? "user";
        var (first, trusted) = ClaimInstance($@"Local\QuickNetSwitcher-{user}");
        _showSignal = trusted ? OpenShowSignal($@"Local\QuickNetSwitcher-show-{user}") : null;

        if (!first && trusted)
        {
            _showSignal?.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        if (_showSignal != null)
        {
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
                (_, _) => Dispatcher.BeginInvoke(() => window.ShowFromTray()),
                null, Timeout.Infinite, executeOnlyOnce: false);
        }
    }

    // (first copy?, can the name be trusted?)
    private (bool First, bool Trusted) ClaimInstance(string name)
    {
        var security = new MutexSecurity();
        security.AddAccessRule(new MutexAccessRule(Administrators, MutexRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new MutexAccessRule(LocalSystem, MutexRights.FullControl, AccessControlType.Allow));

        try
        {
            _instance = MutexAcl.Create(true, name, out var created, security);
            if (created) return (true, true);
            return (false, IsOwnedByAdmins(_instance.GetAccessControl()));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // Someone else's object that won't even let us in: not ours.
            return (true, false);
        }
    }

    private static EventWaitHandle? OpenShowSignal(string name)
    {
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(new EventWaitHandleAccessRule(Administrators, EventWaitHandleRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new EventWaitHandleAccessRule(LocalSystem, EventWaitHandleRights.FullControl, AccessControlType.Allow));

        try
        {
            var signal = EventWaitHandleAcl.Create(false, EventResetMode.AutoReset, name, out var created, security);
            if (created || IsOwnedByAdmins(signal.GetAccessControl()))
                return signal;
            signal.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            return null;
        }
    }

    private static bool IsOwnedByAdmins(ObjectSecurity security) =>
        security.GetOwner(typeof(SecurityIdentifier)) is SecurityIdentifier owner
        && (owner.Equals(Administrators) || owner.Equals(LocalSystem));

    protected override void OnExit(ExitEventArgs e)
    {
        _showWait?.Unregister(null);
        base.OnExit(e);
    }
}
