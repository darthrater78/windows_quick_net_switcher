#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Drawing;
using WinForms = System.Windows.Forms;

namespace QuickNetSwitcher;

public partial class MainWindow : Window
{
    private const string RepositoryUrl = "https://github.com/darthrater78/windows_quick_net_switcher";

    // The XAML MinHeight, restored when leaving simple view.
    private const double DetailViewMinHeight = 450;

    private static string ReleaseNotesUrl
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is null
                ? $"{RepositoryUrl}/releases"
                : $"{RepositoryUrl}/releases/tag/v{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private WinForms.NotifyIcon? _trayIcon;
    private List<RouteEntry> _allRoutes = new();
    private ObservableCollection<AdapterViewModel> _adapters = new();
    private ICollectionView? _adapterView;
    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;
    private bool _dragHandleArmed;
    private DragGhostAdorner? _dragGhost;
    private bool _firewallLoaded;
    private AppSettings _settings = new();
    private bool _isPinnedToDesktop;
    private double _detailViewHeight;
    private bool _isAdjustingSize;

    // Windows reports network changes as events; the timer behind them is a backstop,
    // not the mechanism. See StartAdapterWatch.
    private readonly System.Windows.Threading.DispatcherTimer _autoRefreshTimer =
        new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly System.Windows.Threading.DispatcherTimer _networkChangeDebounce =
        new() { Interval = TimeSpan.FromMilliseconds(750) };
    private bool _refreshInFlight;

    public MainWindow()
    {
        InitializeComponent();

        // The size to snap back to, seeded from the XAML height before anything can
        // switch modes. Tracked from here on by user resizes only.
        _detailViewHeight = Height;
        SizeChanged += Window_SizeChanged;

        SetupTrayIcon();
        LoadSettings();
        AdapterList.ItemsSource = _adapters;
        _adapterView = CollectionViewSource.GetDefaultView(_adapters);
        _adapterView.Filter = IsAdapterVisible;
        LoadAdapters();
        StartAdapterWatch();

        // "Start with Windows" was removed in v1.3.0; drop the value it left behind.
        _ = Task.Run(LegacyStartupCleanup.RemoveRunEntry);

        // The caption is drawn by the OS, not WPF, and needs a handle to exist.
        SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(this);

        Loaded += (_, _) =>
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, () =>
            {
                WindowState = WindowState.Normal;
                Show();
                Activate();
                Focus();

                if (_settings.PinToDesktop)
                    ApplyPinToDesktop(true);
            });
        };
    }

    private void LoadSettings()
    {
        _settings = SettingsService.Load();

        // No explicit choice yet means follow Windows.
        var dark = _settings.DarkMode ?? ThemeService.WindowsPrefersDark();
        ThemeService.Apply(dark);
        DarkModeMenuItem.IsChecked = dark;
        ApplyTrayMenuTheme();

        MinimizeToTrayMenuItem.IsChecked = _settings.MinimizeToTray;
        PinToDesktopMenuItem.IsChecked = _settings.PinToDesktop;
        SimpleViewCheckBox.IsChecked = _settings.SimpleView;
        HideDisconnectedCheckBox.IsChecked = _settings.HideDisconnected;
        ApplySimpleView(_settings.SimpleView);
    }

    private void SaveSettings()
    {
        _settings.MinimizeToTray = MinimizeToTrayMenuItem.IsChecked == true;
        _settings.PinToDesktop = PinToDesktopMenuItem.IsChecked == true;
        _settings.SimpleView = SimpleViewCheckBox.IsChecked == true;
        _settings.HideDisconnected = HideDisconnectedCheckBox.IsChecked == true;
        SettingsService.Save(_settings);
    }

    private void ApplyPinToDesktop(bool pin)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        if (pin)
        {
            DesktopPinService.PinToDesktop(hwnd);
            _isPinnedToDesktop = true;
        }
        else
        {
            DesktopPinService.UnpinFromDesktop(hwnd);
            _isPinnedToDesktop = false;
        }
    }

    private void PinToDesktop_Click(object sender, RoutedEventArgs e)
    {
        var pin = PinToDesktopMenuItem.IsChecked == true;
        ApplyPinToDesktop(pin);
        SaveSettings();
        StatusText.Text = pin ? "Pinned to desktop" : "Unpinned from desktop";
    }

    private void DarkMode_Click(object sender, RoutedEventArgs e)
    {
        var dark = DarkModeMenuItem.IsChecked == true;
        ThemeService.Apply(dark);
        ApplyTrayMenuTheme();

        // Clicking it is what makes the choice explicit; until now the setting was
        // null and tracked the Windows theme.
        _settings.DarkMode = dark;
        SaveSettings();
        StatusText.Text = dark ? "Dark theme" : "Light theme";
    }

    // The tray menu is Windows Forms, outside WPF's resource system entirely, so it
    // keeps a bright popup unless it is coloured by hand. The stock renderer paints
    // its own background over BackColor; the system renderer honours it.
    private void ApplyTrayMenuTheme()
    {
        if (_trayIcon?.ContextMenuStrip is not { } menu) return;

        var dark = ThemeService.IsDark;
        menu.RenderMode = dark
            ? WinForms.ToolStripRenderMode.System
            : WinForms.ToolStripRenderMode.ManagerRenderMode;
        menu.BackColor = dark
            ? System.Drawing.Color.FromArgb(43, 43, 43)
            : System.Drawing.SystemColors.Menu;
        menu.ForeColor = dark
            ? System.Drawing.Color.FromArgb(240, 240, 240)
            : System.Drawing.SystemColors.MenuText;

        foreach (WinForms.ToolStripItem item in menu.Items)
        {
            item.BackColor = menu.BackColor;
            item.ForeColor = menu.ForeColor;
        }
    }

    private void HideDisconnected_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
        _adapterView?.Refresh();
        UpdateAdapterCount();
    }

    // A disabled adapter is never filtered out, whatever this setting says. Switching
    // one back on is the point of the app, and hiding it would put the row you just
    // toggled off out of reach the moment you toggled it.
    private bool IsAdapterVisible(object item) =>
        !_settings.HideDisconnected
        || item is not AdapterViewModel adapter
        || adapter.IsConnected
        || !adapter.IsEnabled;

    private void SimpleView_Click(object sender, RoutedEventArgs e)
    {
        ApplySimpleView(SimpleViewCheckBox.IsChecked == true);
        SaveSettings();
    }

    // Simple view strips the window back to what it is for: a list of connection names
    // and their toggles, with the header and status bar out of the way.
    //
    // The toolbar checkboxes stay. Hiding them was tried and reverted: the toolbar is
    // one row whether it carries one checkbox or four, so collapsing them cost the
    // settings their controls and bought back no height at all.
    private void ApplySimpleView(bool simple)
    {
        var chrome = simple ? Visibility.Collapsed : Visibility.Visible;
        HeaderPanel.Visibility = chrome;
        StatusBar.Visibility = chrome;

        foreach (var adapter in _adapters)
            adapter.SimpleView = simple;

        UpdateWindowSizing();
    }

    // Remembers the height the user picked, so leaving simple view restores it.
    //
    // Mode switches resize the window too and come through here as well; capturing
    // those was the bug behind the window creeping smaller on every round trip. A
    // programmatic shrink got recorded as the height to restore, MinHeight then
    // clamped the restore, and each trip baked the loss in -- which showed up as a
    // cramped list once there were more adapters than the shortened window could hold.
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_isAdjustingSize || SizeToContent != SizeToContent.Manual) return;

        if (e.HeightChanged && e.NewSize.Height > 0)
            _detailViewHeight = e.NewSize.Height;
    }

    // The adapter list sits in a star-sized row, so the window holds its full height
    // whatever the content needs. Simple view rows are a third as tall, which left a
    // dead band of empty card below the last adapter; sizing to content removes it.
    //
    // Only the adapter list is measured this way. The route table would size to every
    // row it holds and snap the window to the full screen height, so the other tabs
    // keep the fixed height.
    private void UpdateWindowSizing()
    {
        var fitToContent = SimpleViewCheckBox.IsChecked == true && MainTabs.SelectedIndex == 0;

        // Already in the right mode: leave the window exactly as the user left it.
        if (fitToContent == (SizeToContent == SizeToContent.Height)) return;

        _isAdjustingSize = true;

        if (fitToContent)
        {
            // MinHeight has to drop or the shrink floors at the XAML minimum, and
            // MaxHeight has to be capped or a long adapter list grows past the screen.
            MinHeight = 0;
            MaxHeight = SystemParameters.WorkArea.Height;
            SizeToContent = SizeToContent.Height;
        }
        else
        {
            // Height is assigned before SizeToContent is released, so the first layout
            // pass already targets the restored size instead of settling at MinHeight
            // on the way there. All four assignments land before any layout runs.
            Height = _detailViewHeight;
            MinHeight = DetailViewMinHeight;
            MaxHeight = double.PositiveInfinity;
            SizeToContent = SizeToContent.Manual;
        }

        // SizeChanged is raised during the layout pass these assignments trigger, and
        // that pass runs after this method returns -- so the guard cannot be cleared
        // synchronously. Loaded priority is serviced once layout and render are done.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            () => _isAdjustingSize = false);
    }

    private void SettingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
    }

    // Left-clicking the gear opens its own context menu. Declaring the menu on the
    // button keeps the two in one place; only the placement has to be set by hand,
    // since WPF positions it at the pointer for a right-click.
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsMenu.PlacementTarget = SettingsButton;
        SettingsMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        SettingsMenu.HorizontalOffset = 0;
        SettingsMenu.IsOpen = true;
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "Quick Net Switcher",
            Visible = true
        };

        _trayIcon.Icon = CreateTrayIcon();

        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (_, _) => ShowFromTray());
        contextMenu.Items.Add("Refresh", null, (_, _) => RefreshCurrentTab());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("Exit", null, (_, _) =>
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
            Application.Current.Shutdown();
        });

        _trayIcon.ContextMenuStrip = contextMenu;
    }

    private static System.Drawing.Icon CreateTrayIcon()
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(System.Drawing.Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(System.Drawing.Color.FromArgb(0, 120, 212));
        g.FillEllipse(brush, 2, 2, 28, 28);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2.5f);
        g.DrawLine(pen, 10, 16, 22, 10);
        g.DrawLine(pen, 22, 10, 18, 10);
        g.DrawLine(pen, 22, 10, 22, 14);
        g.DrawLine(pen, 22, 16, 10, 22);
        g.DrawLine(pen, 10, 22, 14, 22);
        g.DrawLine(pen, 10, 22, 10, 18);
        var handle = bmp.GetHicon();
        return System.Drawing.Icon.FromHandle(handle);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();

        if (_settings.PinToDesktop)
            ApplyPinToDesktop(true);
    }

    private void RefreshCurrentTab()
    {
        switch (MainTabs.SelectedIndex)
        {
            case 1: LoadRoutes(); break;
            case 2: LoadFirewall(); break;
            default: LoadAdapters(); break;
        }
    }

    private void LoadAdapters()
    {
        try
        {
            StatusText.Text = "Loading adapters...";
            var adapters = NetworkAdapterService.GetAdapters()
                .Select(AdapterViewModel.FromInfo)
                .ToList();

            var ordered = AdapterOrderService.ApplyOrder(adapters, a => a.AdapterId);

            _adapters.Clear();
            foreach (var a in ordered)
            {
                a.SimpleView = _settings.SimpleView;
                _adapters.Add(a);
            }

            UpdateAdapterCount();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    // Coming off Wi-Fi used to leave the list showing a connection that was no longer
    // there: adapter state was read once at load and never again, so nothing on screen
    // moved until the user pressed Refresh -- which is also why "hide disconnected"
    // looked broken, since it was filtering a snapshot taken before the link dropped.
    //
    // Windows already announces this. NetworkAddressChanged and
    // NetworkAvailabilityChanged are OS notifications, not a poll, and they cover the
    // case that matters. A single disconnect raises several of them, and they arrive on
    // a thread pool thread, so they are marshalled to the UI thread and collapsed into
    // one refresh once the burst settles.
    //
    // The 10s timer behind them is a backstop for the changes those events do not
    // raise -- an adapter enabled or disabled from Network Connections, a speed or
    // metric change -- and it runs only while the adapter list is on screen. Nothing
    // ticks while the window is in the tray, minimised, or on another tab.
    private void StartAdapterWatch()
    {
        _networkChangeDebounce.Tick += (_, _) =>
        {
            _networkChangeDebounce.Stop();
            if (IsWatchingAdapters)
                _ = RefreshAdaptersAsync();
        };

        _autoRefreshTimer.Tick += (_, _) => _ = RefreshAdaptersAsync();

        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += OnNetworkAvailabilityChanged;

        IsVisibleChanged += (_, _) => UpdateAutoRefreshState();
        UpdateAutoRefreshState();
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e) => QueueAdapterRefresh();

    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) =>
        QueueAdapterRefresh();

    private void QueueAdapterRefresh() =>
        Dispatcher.InvokeAsync(() =>
        {
            // Restarted, not started: a burst of notifications should produce one
            // refresh after it settles rather than one refresh each.
            _networkChangeDebounce.Stop();
            _networkChangeDebounce.Start();
        });

    private bool IsWatchingAdapters =>
        IsVisible && WindowState != WindowState.Minimized && MainTabs.SelectedIndex == 0;

    private void UpdateAutoRefreshState()
    {
        if (IsWatchingAdapters == _autoRefreshTimer.IsEnabled) return;

        if (IsWatchingAdapters)
        {
            _autoRefreshTimer.Start();

            // Catch up on whatever changed while the list was out of sight, so it is
            // current the moment it comes back instead of up to ten seconds stale.
            _ = RefreshAdaptersAsync();
        }
        else
        {
            _autoRefreshTimer.Stop();
        }
    }

    // The WMI queries take long enough to be felt, and this now runs unattended, so the
    // read happens off the UI thread and only the merge touches the collection.
    private async Task RefreshAdaptersAsync()
    {
        // Mid-drag a refresh would pull the row out from under the pointer, and two
        // overlapping refreshes would merge the same snapshot twice.
        if (_refreshInFlight || _isDragging) return;

        _refreshInFlight = true;
        try
        {
            var infos = await Task.Run(NetworkAdapterService.GetAdapters);

            if (!_isDragging)
                MergeAdapters(infos);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            _refreshInFlight = false;
        }
    }

    // Updated in place rather than cleared and rebuilt. A rebuild every ten seconds
    // would drop the selection, restart the row animations, and re-apply the saved
    // order over whatever the user had just dragged; matching on AdapterId leaves the
    // rows where they are and changes only the fields that moved.
    private void MergeAdapters(List<AdapterInfo> infos)
    {
        var incoming = new Dictionary<string, AdapterInfo>();
        foreach (var info in infos)
            incoming[info.AdapterId] = info;

        var changed = false;

        for (var i = _adapters.Count - 1; i >= 0; i--)
        {
            var existing = _adapters[i];

            if (incoming.Remove(existing.AdapterId, out var info))
            {
                changed |= existing.UpdateFrom(info);
            }
            else
            {
                // Gone from WMI altogether -- a USB adapter unplugged, say.
                _adapters.RemoveAt(i);
                changed = true;
            }
        }

        // Anything left arrived since the last read. It goes on the end rather than into
        // the saved order, which belongs to the user.
        foreach (var info in incoming.Values)
        {
            var adapter = AdapterViewModel.FromInfo(info);
            adapter.SimpleView = _settings.SimpleView;
            _adapters.Add(adapter);
            changed = true;
        }

        if (!changed) return;

        // A filter is not re-evaluated when an item's own properties change, so an
        // adapter that has just dropped its link stays on screen until the view is told
        // to look again.
        _adapterView?.Refresh();
        UpdateAdapterCount();
    }

    private void UpdateAdapterCount()
    {
        var enabled = _adapters.Count(a => a.IsEnabled);
        var hidden = _adapters.Count(a => !IsAdapterVisible(a));
        var hiddenNote = hidden > 0 ? $"  ·  {hidden} hidden" : "";

        StatusText.Text = $"{_adapters.Count} adapters found  ·  {enabled} enabled{hiddenNote}";

        // The status bar carrying that count is hidden in simple view, where the filter
        // is most likely to be on. Without this, a row the filter removed and an adapter
        // that genuinely vanished look identical.
        HideDisconnectedCheckBox.Content = hidden > 0
            ? $"Hide disconnected ({hidden})"
            : "Hide disconnected";
    }

    private void SaveAdapterOrder()
    {
        AdapterOrderService.Save(_adapters.Select(a => a.AdapterId));
    }

    private void LoadRoutes()
    {
        try
        {
            StatusText.Text = "Loading route table...";
            _allRoutes = RouteTableService.GetRoutes();
            ApplyRouteFilter();
            StatusText.Text = $"{_allRoutes.Count} routes loaded";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void ApplyRouteFilter()
    {
        var visible = _allRoutes.Where(r =>
        {
            return r.RouteType switch
            {
                "Default" => ShowDefaultRoutes.IsChecked == true,
                "Remote" => ShowRemoteRoutes.IsChecked == true,
                "Local" => ShowLocalRoutes.IsChecked == true,
                _ => ShowOtherRoutes.IsChecked == true
            };
        }).ToList();

        RouteGrid.ItemsSource = visible;
        StatusText.Text = $"Showing {visible.Count} of {_allRoutes.Count} routes";
    }

    private void LoadFirewall()
    {
        try
        {
            StatusText.Text = "Loading firewall status...";
            var profiles = FirewallService.GetProfiles()
                .Select(FirewallViewModel.FromProfile)
                .ToList();
            FirewallList.ItemsSource = profiles;
            _firewallLoaded = true;

            var onCount = profiles.Count(p => p.IsEnabled);
            StatusText.Text = $"{profiles.Count} firewall profiles  ·  {onCount} enabled";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private async void ToggleFirewall_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox toggle || toggle.Tag is not string profileKey)
            return;

        var enable = toggle.IsChecked == true;
        var action = enable ? "Enabling" : "Disabling";

        StatusText.Text = $"{action} {profileKey} firewall...";
        toggle.IsEnabled = false;

        try
        {
            var success = await Task.Run(() =>
                FirewallService.SetProfileState(profileKey, enable));

            if (success)
            {
                StatusText.Text = $"{profileKey} firewall {(enable ? "enabled" : "disabled")}";
                await Task.Delay(300);
                LoadFirewall();
            }
            else
            {
                StatusText.Text = $"Failed to {action.ToLower()} {profileKey} firewall";
                toggle.IsChecked = !enable;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            toggle.IsChecked = !enable;
        }
        finally
        {
            toggle.IsEnabled = true;
        }
    }

    private async void ToggleAdapter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox toggle || toggle.Tag is not string adapterId)
            return;

        var enable = toggle.IsChecked == true;
        var action = enable ? "Enabling" : "Disabling";
        var adapter = _adapters.FirstOrDefault(a => a.AdapterId == adapterId);
        var name = adapter?.Name ?? adapterId;

        StatusText.Text = $"{action} {name}...";
        toggle.IsEnabled = false;

        try
        {
            var success = await Task.Run(() =>
                NetworkAdapterService.SetAdapterState(adapterId, enable));

            if (success)
            {
                StatusText.Text = $"{name} {(enable ? "enabled" : "disabled")}";
                await Task.Delay(500);
                LoadAdapters();
            }
            else
            {
                StatusText.Text = $"Failed to {action.ToLower()} {name}";
                toggle.IsChecked = !enable;
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
            toggle.IsChecked = !enable;
        }
        finally
        {
            toggle.IsEnabled = true;
        }
    }

    private async void EditMetric_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not AdapterViewModel adapter)
            return;

        var dialog = new MetricDialog(adapter.Name, adapter.InterfaceMetric)
        {
            Owner = this
        };
        dialog.SourceInitialized += (_, _) => ThemeService.ApplyTitleBar(dialog);

        if (dialog.ShowDialog() == true)
        {
            var newMetric = dialog.MetricValue;
            StatusText.Text = $"Setting {adapter.Name} metric to {newMetric}...";

            var success = await Task.Run(() =>
                NetworkAdapterService.SetInterfaceMetric(adapter.Name, newMetric));

            if (success)
            {
                StatusText.Text = $"{adapter.Name} metric set to {newMetric}";
                await Task.Delay(300);
                LoadAdapters();
            }
            else
            {
                StatusText.Text = $"Failed to set metric for {adapter.Name}";
            }
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshCurrentTab();
    }

    private void GitHubLink_Click(object sender, RoutedEventArgs e) => OpenUrl(RepositoryUrl);

    private void ReleaseNotesLink_Click(object sender, RoutedEventArgs e) => OpenUrl(ReleaseNotesUrl);

    private void OpenUrl(string url)
    {
        try
        {
            // The app runs elevated (requireAdministrator), and shell-executing the URL
            // here would open the default browser as administrator too. Handing it to
            // explorer.exe instead delegates to the already-running user-level shell, so
            // the browser opens unelevated.
            using var browser = Process.Start(new ProcessStartInfo(SystemPaths.Explorer, url)
            {
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't open link: {ex.Message}";
        }
    }

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        UpdateWindowSizing();
        UpdateAutoRefreshState();

        if (MainTabs.SelectedIndex == 1 && _allRoutes.Count == 0)
            LoadRoutes();
        else if (MainTabs.SelectedIndex == 2 && !_firewallLoaded)
            LoadFirewall();
    }

    private void RouteFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _allRoutes.Count == 0) return;
        ApplyRouteFilter();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && MinimizeToTrayMenuItem.IsChecked == true)
        {
            if (_isPinnedToDesktop)
                ApplyPinToDesktop(false);
            Hide();
        }

        UpdateAutoRefreshState();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (MinimizeToTrayMenuItem.IsChecked == true)
        {
            e.Cancel = true;
            if (_isPinnedToDesktop)
                ApplyPinToDesktop(false);
            Hide();
            return;
        }

        if (_isPinnedToDesktop)
            ApplyPinToDesktop(false);

        // NetworkChange's events are static: leaving them subscribed would hold this
        // window alive for the life of the process.
        NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkAvailabilityChanged;
        _autoRefreshTimer.Stop();
        _networkChangeDebounce.Stop();

        _trayIcon?.Dispose();
        _trayIcon = null;
    }

    // --- Drag and drop reordering ---

    private static bool IsOnDragHandle(MouseEventArgs e, ListBox listBox)
    {
        var hit = e.OriginalSource as DependencyObject;
        while (hit != null)
        {
            if (hit is FrameworkElement fe && fe.Tag as string == "DragHandle")
                return true;
            hit = VisualTreeHelper.GetParent(hit);
        }
        return false;
    }

    private static ListBoxItem? GetListBoxItemAt(ListBox listBox, System.Windows.Point pos)
    {
        var element = listBox.InputHitTest(pos) as DependencyObject;
        while (element != null)
        {
            if (element is ListBoxItem item)
                return item;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private void AdapterList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Whether the press landed on a handle has to be remembered, not just acted on.
        // Without it, a later press anywhere in the list -- the second click of a
        // double-click, say -- would reach the move handler and reorder whichever row
        // happened to sit under the stale start point.
        _dragHandleArmed = IsOnDragHandle(e, AdapterList);
        if (!_dragHandleArmed) return;

        _dragStartPoint = e.GetPosition(AdapterList);
        _isDragging = false;
    }

    private void AdapterList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragHandleArmed || e.LeftButton != MouseButtonState.Pressed || _isDragging)
            return;

        var pos = e.GetPosition(AdapterList);
        var diff = pos - _dragStartPoint;
        if (Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = GetListBoxItemAt(AdapterList, _dragStartPoint);
        if (item?.DataContext is not AdapterViewModel adapter)
            return;

        _isDragging = true;
        ShowDragGhost(item);
        try
        {
            DragDrop.DoDragDrop(AdapterList, adapter, DragDropEffects.Move);
        }
        finally
        {
            HideDragGhost();
            _isDragging = false;
            _dragHandleArmed = false;
        }
    }

    private void AdapterList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
        _dragHandleArmed = false;
    }

    private void ShowDragGhost(ListBoxItem item)
    {
        var layer = AdornerLayer.GetAdornerLayer(AdapterList);
        if (layer == null) return;

        // Where in the row the pointer took hold, so the ghost keeps that grip rather
        // than snapping its top edge to the cursor.
        var rowTop = item.TranslatePoint(new System.Windows.Point(0, 0), AdapterList).Y;
        _dragGhost = new DragGhostAdorner(AdapterList, item, _dragStartPoint.Y - rowTop);
        layer.Add(_dragGhost);
        _dragGhost.UpdatePosition(_dragStartPoint);
    }

    private void HideDragGhost()
    {
        if (_dragGhost == null) return;

        AdornerLayer.GetAdornerLayer(AdapterList)?.Remove(_dragGhost);
        _dragGhost = null;
    }

    private void AdapterList_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(AdapterViewModel)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }
        e.Effects = DragDropEffects.Move;
        e.Handled = true;
        _dragGhost?.UpdatePosition(e.GetPosition(AdapterList));
    }

    private void AdapterList_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(AdapterViewModel)) is not AdapterViewModel dragged)
            return;

        var dropPos = e.GetPosition(AdapterList);
        var targetItem = GetListBoxItemAt(AdapterList, dropPos);
        var target = targetItem?.DataContext as AdapterViewModel;

        var oldIndex = _adapters.IndexOf(dragged);
        if (oldIndex < 0) return;

        int newIndex;
        if (target != null && target != dragged)
        {
            newIndex = _adapters.IndexOf(target);
        }
        else
        {
            newIndex = _adapters.Count - 1;
        }

        if (oldIndex == newIndex) return;

        _adapters.Move(oldIndex, newIndex);
        SaveAdapterOrder();
        StatusText.Text = $"Adapter order updated";
    }
}
