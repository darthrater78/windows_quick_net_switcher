using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Drawing;
using WinForms = System.Windows.Forms;

namespace QuickNetSwitcher;

public partial class MainWindow : Window
{
    private WinForms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        SetupTrayIcon();
        LoadAdapters();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "Quick Net Switcher",
            Visible = true
        };

        // Use a generated icon since we can't embed .ico in this environment
        _trayIcon.Icon = CreateTrayIcon();

        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        var contextMenu = new WinForms.ContextMenuStrip();
        contextMenu.Items.Add("Show", null, (_, _) => ShowFromTray());
        contextMenu.Items.Add("Refresh", null, (_, _) => LoadAdapters());
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

    private static Icon CreateTrayIcon()
    {
        var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(Color.FromArgb(0, 120, 212));
        g.FillEllipse(brush, 2, 2, 28, 28);
        // Draw a simple network icon (two arrows)
        using var pen = new Pen(Color.White, 2.5f);
        g.DrawLine(pen, 10, 16, 22, 10);
        g.DrawLine(pen, 22, 10, 18, 10);
        g.DrawLine(pen, 22, 10, 22, 14);
        g.DrawLine(pen, 22, 16, 10, 22);
        g.DrawLine(pen, 10, 22, 14, 22);
        g.DrawLine(pen, 10, 22, 10, 18);
        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void LoadAdapters()
    {
        try
        {
            StatusText.Text = "Loading adapters...";
            var adapters = NetworkAdapterService.GetAdapters()
                .Select(AdapterViewModel.FromInfo)
                .ToList();
            AdapterList.ItemsSource = adapters;

            var enabled = adapters.Count(a => a.IsEnabled);
            StatusText.Text = $"{adapters.Count} adapters found  ·  {enabled} enabled";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private async void ToggleAdapter_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox toggle || toggle.Tag is not string adapterId)
            return;

        var enable = toggle.IsChecked == true;
        var action = enable ? "Enabling" : "Disabling";
        var adapter = (AdapterList.ItemsSource as System.Collections.Generic.List<AdapterViewModel>)?
            .FirstOrDefault(a => a.AdapterId == adapterId);
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

    private void AdapterItem_Click(object sender, MouseButtonEventArgs e)
    {
        // Don't interfere with toggle clicks
        if (e.OriginalSource is System.Windows.Shapes.Ellipse)
            return;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadAdapters();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && MinimizeToTrayCheckBox.IsChecked == true)
            Hide();
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        if (MinimizeToTrayCheckBox.IsChecked == true)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
