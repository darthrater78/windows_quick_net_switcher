using System.Windows;
using System.Windows.Controls;

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
            // Said in the dialog, beside the field it is about, rather than in a second
            // window stacked on top of this one.
            ErrorText.Visibility = Visibility.Visible;
            MetricInput.SetResourceReference(Control.BorderBrushProperty, "ErrorBrush");
            MetricInput.BorderThickness = new Thickness(2);
            MetricInput.SelectAll();
            MetricInput.Focus();
        }
    }
}
