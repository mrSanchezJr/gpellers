using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Gpellers;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var lbl = this.FindControl<TextBlock>("lblVersion");
        if (lbl != null)
        {
            lbl.Text = $"Версия: {AppVersion.DisplayString} (Cross-Platform)";
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
