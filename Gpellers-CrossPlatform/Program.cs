using System;
using Avalonia;

namespace Gpellers;

internal class Program
{
    // =========================================================================
    // Точка входа в кроссплатформенное приложение Avalonia UI (.NET 8).
    // Поддерживает запуск под Windows и Linux (x64 / ARM).
    // =========================================================================
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
