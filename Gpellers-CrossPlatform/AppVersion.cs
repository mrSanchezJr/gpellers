using System;
using System.Reflection;

namespace Gpellers;

/// <summary>
/// Единая точка правды для версии приложения и параметров репозитория GitHub.
/// Версия считывается автоматически из тега &lt;Version&gt; в Gpellers.csproj при сборке.
/// </summary>
public static class AppVersion
{
    public const string RepoOwner = "mrSanchezJr";
    public const string RepoName = "gpellers";
    public const string RepoUrl = "https://github.com/mrSanchezJr/gpellers";
    public const string ReleasesUrl = "https://github.com/mrSanchezJr/gpellers/releases";
    public const string ApiLatestReleaseUrl = "https://api.github.com/repos/mrSanchezJr/gpellers/releases/latest";

    /// <summary>
    /// Текущая версия сборки в виде System.Version (Major, Minor, Build).
    /// </summary>
    public static Version Version
    {
        get
        {
            var asmVer = Assembly.GetExecutingAssembly().GetName().Version;
            if (asmVer != null)
            {
                return new Version(asmVer.Major, asmVer.Minor, Math.Max(0, asmVer.Build));
            }
            return new Version(1, 0, 4);
        }
    }

    /// <summary>
    /// Отображаемая строка версии (например, "1.0.4").
    /// </summary>
    public static string DisplayString => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    /// <summary>
    /// Полная строка для заголовков и меток (например, "Ver: 1.0.4").
    /// </summary>
    public static string LabelString => $"Ver: {DisplayString}";
}
