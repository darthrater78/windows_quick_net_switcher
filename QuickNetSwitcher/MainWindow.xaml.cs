#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Drawing;
using WinForms = System.Windows.Forms;

namespace QuickNetSwitcher;

public partial class MainWindow : Window
{
    private WinForms.NotifyIcon? _trayIcon;
    private List<RouteEntry> _allRoutes = new();
    private ObservableCollection<AdapterViewModel> _adapters = new();
    private System.Windows.Point _dragStartPoint;
    private bool _isDragging;

    public MainWindow()
    {
        InitializeComponent();
        SetupTrayIcon();
        AdapterList.ItemsSource = _adapters;
        LoadAdapters();
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
    }

    private void RefreshCurrentTab()
    {
        if (MainTabs.SelectedIndex == 1)
            LoadRoutes();
        else
            LoadAdapters();
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
                _adapters.Add(a);

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

    private void MainTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;

        if (MainTabs.SelectedIndex == 1 && _allRoutes.Count == 0)
            LoadRoutes();
    }

    private void RouteFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _allRoutes.Count == 0) return;
        ApplyRouteFilter();
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
