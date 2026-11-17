#nullable enable
using System;
using System.Windows;
using System.Windows.Threading;

namespace QuickNetSwitcher;

// Asked after new IP settings are applied. Only Keep keeps them: Revert now, closing
// the window and the countdown running out all go back. That is the point -- a change
// that cut off the network the user reached this machine over leaves nobody to click
// Keep, and the old settings come back on their own.
public partial class KeepSettingsDialog : Window
{
    public const int Seconds = 15;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly string _previous;
    private int _left = Seconds;

    public KeepSettingsDialog(string adapterName, string now, string previous)
    {
        InitializeComponent();
        _previous = previous;
        ChangeText.Text = $"{adapterName} is now {now}.";
        UpdateCountdown();
        _timer.Tick += (_, _) =>
        {
            _left--;
            if (_left <= 0)
            {
                _timer.Stop();
                DialogResult = false;
                return;
            }
            UpdateCountdown();
        };
        Loaded += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    private void UpdateCountdown() =>
        CountdownText.Text = $"Going back to {_previous} in {_left} s.";

    private void Keep_Click(object sender, RoutedEventArgs e)
    {
        _timer.Stop();
        DialogResult = true;
    }
}
