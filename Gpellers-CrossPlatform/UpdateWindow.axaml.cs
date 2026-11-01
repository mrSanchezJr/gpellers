using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Gpellers;

public partial class UpdateWindow : Window
{
    private string _targetReleaseUrl = AppVersion.ReleasesUrl;

    public UpdateWindow()
    {
        InitializeComponent();
        Loaded += UpdateWindow_Loaded;
    }

    private async void UpdateWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        var lbl = this.FindControl<TextBlock>("label2");
        var btn = this.FindControl<Button>("button1");
        var currentVer = AppVersion.Version;
        string currentVerStr = AppVersion.DisplayString;

        try
        {
            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(6);
            // GitHub REST API требует обязательный заголовок User-Agent
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Gpellers-App", currentVerStr));

            var response = await client.GetAsync(AppVersion.ApiLatestReleaseUrl);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // На GitHub пока не опубликован ни один релиз
                if (lbl != null)
                {
                    lbl.Text = $"Текущая версия программы: {currentVerStr}\n\nНа GitHub пока нет опубликованных релизов.\nУ вас установлена актуальная сборка.";
                }
                if (btn != null) btn.IsEnabled = false;
                return;
            }

            response.EnsureSuccessStatusCode();
            string jsonStr = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            string tagName = root.GetProperty("tag_name").GetString() ?? "";
            string releaseName = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? tagName : tagName;
            string releaseBody = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() ?? AppVersion.ReleasesUrl : AppVersion.ReleasesUrl;

            _targetReleaseUrl = htmlUrl;

            // Очищаем тег от префиксов (например, "v1.0.5" -> "1.0.5")
            string cleanTag = tagName.TrimStart('v', 'V', ' ').Trim();
            if (Version.TryParse(cleanTag, out Version? remoteVer))
            {
                if (remoteVer > currentVer)
                {
                    string info = $"Доступна новая версия: {cleanTag}!\n(Текущая версия: {currentVerStr})\n\n{releaseName}";
                    if (!string.IsNullOrWhiteSpace(releaseBody))
                    {
                        info += $"\n\nЧто нового:\n{releaseBody.Trim()}";
                    }
                    if (lbl != null) lbl.Text = info;
                    if (btn != null) btn.IsEnabled = true;
                }
                else
                {
                    if (lbl != null) lbl.Text = $"Текущая версия программы: {currentVerStr}\n\nУ вас установлена самая свежая версия.\nОбновлений нет.";
                    if (btn != null) btn.IsEnabled = false;
                }
            }
            else
            {
                if (!string.Equals(cleanTag, currentVerStr, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(cleanTag))
                {
                    if (lbl != null) lbl.Text = $"На GitHub доступен релиз: {tagName}\nТекущая версия: {currentVerStr}\nПерейти на страницу релиза?";
                    if (btn != null) btn.IsEnabled = true;
                }
                else
                {
                    if (lbl != null) lbl.Text = $"Текущая версия программы: {currentVerStr}\n\nОбновлений нет.";
                    if (btn != null) btn.IsEnabled = false;
                }
            }
        }
        catch (HttpRequestException ex)
        {
            if (lbl != null) lbl.Text = $"Текущая версия: {currentVerStr}\n\nНе удалось связаться с GitHub:\n{ex.Message}";
            if (btn != null) btn.IsEnabled = false;
        }
        catch (Exception ex)
        {
            if (lbl != null) lbl.Text = $"Текущая версия: {currentVerStr}\n\nОшибка проверки:\n{ex.Message}";
            if (btn != null) btn.IsEnabled = false;
        }
    }

    private void Button1_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_targetReleaseUrl) { UseShellExecute = true });
        }
        catch { }
        Close();
    }

    private void Button2_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
