using System.Text.RegularExpressions;
using Sekta.Shared.DTOs;

namespace Sekta.Server.Services;

public interface ILinkPreviewService
{
    Task<LinkPreviewDto?> FetchPreviewAsync(string? text);
}

public partial class LinkPreviewService : ILinkPreviewService
{
    private static readonly HttpClient _http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5
    }) { Timeout = TimeSpan.FromSeconds(8) };

    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    public async Task<LinkPreviewDto?> FetchPreviewAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("http"))
            return null;

        var match = UrlRegex().Match(text);
        if (!match.Success)
            return null;

        var url = match.Value.TrimEnd('.', ',', ')', ']');

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            request.Headers.Add("Accept", "text/html,application/xhtml+xml");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9,ru;q=0.8");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return null;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!contentType.Contains("html"))
                return null;

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
                return null;

            return new LinkPreviewDto(url, title, description, image, domain);
        }
        catch
        {
            return null;
        }
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
