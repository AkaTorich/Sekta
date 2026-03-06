using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Sekta.Shared.DTOs;

namespace Sekta.Client.Controls;

public class LinkPreviewView : ContentView
{
    public LinkPreviewView()
    {
        IsVisible = false;
    }

    // Fallback cache for old messages without server-side preview
    private static readonly ConcurrentDictionary<string, LinkPreviewDto?> _cache = new();
    private static readonly Lazy<HttpClient> _httpLazy = new(() =>
    {
        try
        {
            return new HttpClient(new HttpClientHandler
            {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 5
            }) { Timeout = TimeSpan.FromSeconds(8) };
        }
        catch
        {
            // iOS AOT: HttpClientHandler may not be available, use platform default
            return new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
        }
    });
    private static HttpClient _http => _httpLazy.Value;

    public static readonly BindableProperty PreviewProperty =
        BindableProperty.Create(nameof(Preview), typeof(LinkPreviewDto), typeof(LinkPreviewView),
            propertyChanged: OnPreviewChanged);

    public static readonly BindableProperty MessageTextProperty =
        BindableProperty.Create(nameof(MessageText), typeof(string), typeof(LinkPreviewView),
            propertyChanged: OnMessageTextChanged);

    public LinkPreviewDto? Preview
    {
        get => (LinkPreviewDto?)GetValue(PreviewProperty);
        set => SetValue(PreviewProperty, value);
    }

    public string? MessageText
    {
        get => (string?)GetValue(MessageTextProperty);
        set => SetValue(MessageTextProperty, value);
    }

    private static void OnPreviewChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (LinkPreviewView)bindable;
        var preview = newValue as LinkPreviewDto;

        if (preview is not null)
        {
            // Defer visual tree modification to avoid crash during layout pass on iOS
            view.Dispatcher.Dispatch(() =>
            {
                view.IsVisible = true;
                BuildUI(view, preview);
            });
        }
        else if (string.IsNullOrWhiteSpace(view.MessageText) || !view.MessageText.Contains("http"))
        {
            view.IsVisible = false;
            view.Content = null;
        }
    }

    private static async void OnMessageTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (LinkPreviewView)bindable;

        if (view.Preview is not null)
            return;

        var text = newValue as string;
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("http"))
        {
            view.IsVisible = false;
            view.Content = null;
            return;
        }

        var urlMatch = Regex.Match(text, @"https?://[^\s<>""']+", RegexOptions.IgnoreCase);
        if (!urlMatch.Success)
        {
            view.IsVisible = false;
            return;
        }

        var url = urlMatch.Value.TrimEnd('.', ',', ')', ']');

        if (_cache.TryGetValue(url, out var cached))
        {
            if (cached is not null)
            {
                view.IsVisible = true;
                BuildUI(view, cached);
            }
            else
            {
                view.IsVisible = false;
                view.Content = null;
            }
            return;
        }

        var capturedText = text;
        try
        {
            var preview = await FetchPreviewAsync(url);

            if (view.MessageText != capturedText)
                return;

            if (preview is null)
            {
                view.IsVisible = false;
                view.Content = null;
                return;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (view.MessageText == capturedText)
                {
                    view.IsVisible = true;
                    BuildUI(view, preview);
                }
            });
        }
        catch
        {
            view.IsVisible = false;
        }
    }

    private static async Task<LinkPreviewDto?> FetchPreviewAsync(string url)
    {
        if (_cache.TryGetValue(url, out var cached))
            return cached;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9,ru;q=0.8");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return Cache(url, null);

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("html"))
                return Cache(url, null);

            var buffer = new byte[65536];
            await using var stream = await response.Content.ReadAsStreamAsync();
            var bytesRead = await stream.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false);
            var html = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);

            var title = ExtractMeta(html, "og:title") ?? ExtractTitle(html);
            var description = ExtractMeta(html, "og:description") ?? ExtractMeta(html, "description");
            var image = ExtractMeta(html, "og:image");

            if (image is not null && !image.StartsWith("http"))
            {
                var uri = new Uri(url);
                image = image.StartsWith("//")
                    ? $"{uri.Scheme}:{image}"
                    : $"{uri.Scheme}://{uri.Host}{(image.StartsWith('/') ? "" : "/")}{image}";
            }

            var domain = new Uri(url).Host.Replace("www.", "");

            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(description))
                return Cache(url, null);

            return Cache(url, new LinkPreviewDto(url, title, description, image, domain));
        }
        catch
        {
            return Cache(url, null);
        }
    }

    private static LinkPreviewDto? Cache(string url, LinkPreviewDto? data)
    {
        _cache.TryAdd(url, data);
        return data;
    }

    private static void BuildUI(LinkPreviewView view, LinkPreviewDto preview)
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;

        view.IsVisible = true;
        view.Margin = new Thickness(0, 4, 0, 0);

        var border = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 8 },
            Stroke = Colors.Transparent,
            BackgroundColor = isDark ? Color.FromArgb("#1A2734") : Color.FromArgb("#F0F2F5"),
            Padding = new Thickness(0)
        };

        var stack = new VerticalStackLayout { Spacing = 0 };

        if (!string.IsNullOrEmpty(preview.ImageUrl))
        {
            try
            {
                stack.Children.Add(new Image
                {
                    Source = ImageSource.FromUri(new Uri(preview.ImageUrl)),
                    Aspect = Aspect.AspectFill,
                    MaximumHeightRequest = 150,
                    HorizontalOptions = LayoutOptions.Fill
                });
            }
            catch { }
        }

        var textStack = new VerticalStackLayout { Spacing = 2, Padding = new Thickness(8, 6) };

        if (!string.IsNullOrEmpty(preview.Domain))
            textStack.Children.Add(new Label { Text = preview.Domain, FontSize = 10, TextColor = Color.FromArgb("#2AABEE") });

        if (!string.IsNullOrEmpty(preview.Title))
            textStack.Children.Add(new Label
            {
                Text = preview.Title, FontSize = 13, FontAttributes = FontAttributes.Bold,
                TextColor = isDark ? Colors.White : Colors.Black,
                MaxLines = 2, LineBreakMode = LineBreakMode.TailTruncation
            });

        if (!string.IsNullOrEmpty(preview.Description))
            textStack.Children.Add(new Label
            {
                Text = preview.Description, FontSize = 12,
                TextColor = isDark ? Color.FromArgb("#8B9DAF") : Color.FromArgb("#707579"),
                MaxLines = 3, LineBreakMode = LineBreakMode.TailTruncation
            });

        stack.Children.Add(textStack);
        border.Content = stack;

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            try { await Launcher.OpenAsync(new Uri(preview.Url)); } catch { }
        };
        border.GestureRecognizers.Add(tap);
        view.Content = border;
    }

    private static string? ExtractMeta(string html, string property)
    {
        var pattern = $"""<meta[^>]*(?:property|name)=["']{property}["'][^>]*content=["']([^"']*)["']""";
        var m = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (m.Success) return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);

        pattern = $"""<meta[^>]*content=["']([^"']*)["'][^>]*(?:property|name)=["']{property}["']""";
        m = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value) : null;
    }

    private static string? ExtractTitle(string html)
    {
        var m = Regex.Match(html, @"<title[^>]*>([^<]+)</title>", RegexOptions.IgnoreCase);
        return m.Success ? System.Net.WebUtility.HtmlDecode(m.Groups[1].Value.Trim()) : null;
    }
}
