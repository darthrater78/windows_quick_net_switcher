#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;
    private bool _firewallLoaded;
    private AppSettings _settings = new();
    private bool _isPinnedToDesktop;
    private double _detailViewHeight;

    public MainWindow()
    {
        InitializeComponent();
        SetupTrayIcon();
        LoadSettings();
        AdapterList.ItemsSource = _adapters;
        LoadAdapters();

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
        DarkModeCheckBox.IsChecked = dark;
        ApplyTrayMenuTheme();

        MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTray;
        PinToDesktopCheckBox.IsChecked = _settings.PinToDesktop;
        SimpleViewCheckBox.IsChecked = _settings.SimpleView;
        ApplySimpleView(_settings.SimpleView);
    }

    private void SaveSettings()
    {
        _settings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked == true;
        _settings.PinToDesktop = PinToDesktopCheckBox.IsChecked == true;
        _settings.SimpleView = SimpleViewCheckBox.IsChecked == true;
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
        var pin = PinToDesktopCheckBox.IsChecked == true;
        ApplyPinToDesktop(pin);
        SaveSettings();
        StatusText.Text = pin ? "Pinned to desktop" : "Unpinned from desktop";
    }

    private void DarkMode_Click(object sender, RoutedEventArgs e)
    {
        var dark = DarkModeCheckBox.IsChecked == true;
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

    // The adapter list sits in a star-sized row, so the window holds its full height
    // whatever the content needs. Simple view rows are a third as tall, which left a
    // dead band of empty card below the last adapter; sizing to content removes it.
    // MinHeight has to drop first, or the shrink floors at the XAML minimum, and
    // MaxHeight has to be capped, or a long adapter list grows past the screen.
    //
    // Only the adapter list is measured this way. The route table would size to every
    // row it holds and snap the window to the full screen height, so the other tabs
    // keep the fixed height.
    private void UpdateWindowSizing()
    {
        if (SimpleViewCheckBox.IsChecked == true && MainTabs.SelectedIndex == 0)
        {
            if (SizeToContent == SizeToContent.Manual)
                _detailViewHeight = ActualHeight > 0 ? ActualHeight : Height;

            MinHeight = 0;
            MaxHeight = SystemParameters.WorkArea.Height;
            SizeToContent = SizeToContent.Height;
            return;
        }

        SizeToContent = SizeToContent.Manual;
        MinHeight = DetailViewMinHeight;
        MaxHeight = double.PositiveInfinity;

        if (_detailViewHeight > 0)
            Height = _detailViewHeight;
    }

    private void SettingCheckBox_Click(object sender, RoutedEventArgs e)
    {
        SaveSettings();
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

            var enabled = _adapters.Count(a => a.IsEnabled);
            StatusText.Text = $"{_adapters.Count} adapters found  ·  {enabled} enabled";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
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
        if (WindowState == WindowState.Minimized && MinimizeToTrayCheckBox.IsChecked == true)
        {
            if (_isPinnedToDesktop)
                ApplyPinToDesktop(false);
            Hide();
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (MinimizeToTrayCheckBox.IsChecked == true)
        {
            e.Cancel = true;
            if (_isPinnedToDesktop)
                ApplyPinToDesktop(false);
            Hide();
            return;
        }

        if (_isPinnedToDesktop)
            ApplyPinToDesktop(false);
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
        if (!IsOnDragHandle(e, AdapterList)) return;
        _dragStartPoint = e.GetPosition(AdapterList);
        _isDragging = false;
    }

    private void AdapterList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
            return;

        var pos = e.GetPosition(AdapterList);
        var diff = pos - _dragStartPoint;
        if (Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var item = GetListBoxItemAt(AdapterList, _dragStartPoint);
        if (item?.DataContext is not AdapterViewModel adapter)
            return;

        _isDragging = true;
        DragDrop.DoDragDrop(AdapterList, adapter, DragDropEffects.Move);
        _isDragging = false;
    }

    private void AdapterList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _isDragging = false;
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
