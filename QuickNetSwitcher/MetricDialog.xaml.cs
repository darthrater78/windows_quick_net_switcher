using System.Windows;

namespace QuickNetSwitcher;

public partial class MetricDialog : Window
{
    public int MetricValue { get; private set; }

    public MetricDialog(string adapterName, int currentMetric)
    {
        InitializeComponent();
        AdapterLabel.Text = adapterName;
        MetricInput.Text = currentMetric > 0 ? currentMetric.ToString() : "";
        MetricInput.SelectAll();
        MetricInput.Focus();
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (int.TryParse(MetricInput.Text, out var value) && value >= 1 && value <= 9999)
        {
            MetricValue = value;
            DialogResult = true;
        }
        else
        {
            MessageBox.Show("Enter a value between 1 and 9999.", "Invalid Metric",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
