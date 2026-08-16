using System.Windows;

namespace Fpe2001Remake.UI.Views;

public partial class TextInputDialog : Window
{
    public TextInputDialog(string title, string label, string initialValue = "")
    {
        InitializeComponent();
        Title = title;
        Label = label;
        ValueBox.Text = initialValue;
        DataContext = this;
        ValueBox.Focus();
        ValueBox.SelectAll();
    }

    public string Label { get; }

    public string ValueText => ValueBox.Text.Trim();

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ValueBox.Text))
        {
            MessageBox.Show(this, "名称不能为空。", "FPE2001-Remake", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
