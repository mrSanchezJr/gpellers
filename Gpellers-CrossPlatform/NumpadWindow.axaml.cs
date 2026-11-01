using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Gpellers;

public partial class NumpadWindow : Window
{
    public event EventHandler<string>? NumberPressed;
    public event EventHandler? BackspacePressed;
    public event EventHandler? EnterPressed;
    public event EventHandler? ClearPressed;

    public bool AllowClose { get; set; } = false;

    public NumpadWindow()
    {
        InitializeComponent();

        Closing += (sender, e) =>
        {
            if (!AllowClose)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    public void SetTargetField(string? fieldName)
    {
        var indicator = this.FindControl<TextBlock>("TargetIndicatorText");
        if (indicator != null)
        {
            indicator.Text = string.IsNullOrEmpty(fieldName) ? "Активное поле: нет" : $"Поле: {fieldName}";
        }
    }

    private void Number_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content != null)
        {
            NumberPressed?.Invoke(this, btn.Content.ToString() ?? "");
        }
    }

    private void Backspace_Click(object? sender, RoutedEventArgs e)
    {
        BackspacePressed?.Invoke(this, EventArgs.Empty);
    }

    private void Enter_Click(object? sender, RoutedEventArgs e)
    {
        EnterPressed?.Invoke(this, EventArgs.Empty);
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        ClearPressed?.Invoke(this, EventArgs.Empty);
    }
}
