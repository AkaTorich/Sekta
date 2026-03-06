using System.Text.RegularExpressions;

namespace Sekta.Client.Controls;

public partial class LinkedLabel : Label
{
    public static readonly BindableProperty LinkedTextProperty =
        BindableProperty.Create(nameof(LinkedText), typeof(string), typeof(LinkedLabel),
            propertyChanged: OnLinkedTextChanged);

    public string? LinkedText
    {
        get => (string?)GetValue(LinkedTextProperty);
        set => SetValue(LinkedTextProperty, value);
    }

    [GeneratedRegex(@"(https?://[^\s<>""']+)", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    private static void OnLinkedTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var label = (LinkedLabel)bindable;
        var text = newValue as string;

        if (string.IsNullOrEmpty(text))
        {
            label.FormattedText = null;
            label.Text = null;
            return;
        }

        var matches = UrlPattern().Matches(text);
        if (matches.Count == 0)
        {
            label.FormattedText = null;
            label.Text = text;
            return;
        }

        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var textColor = isDark ? Colors.White : Colors.Black;
        var linkColor = Color.FromArgb("#2AABEE");

        var formatted = new FormattedString();
        var lastIndex = 0;

        foreach (Match match in matches)
        {
            // Add text before the URL
            if (match.Index > lastIndex)
            {
                formatted.Spans.Add(new Span
                {
                    Text = text[lastIndex..match.Index],
                    TextColor = textColor,
                    FontSize = label.FontSize
                });
            }

            // Add the URL as a clickable span
            var url = match.Value.TrimEnd('.', ',', ')', ']');
            var span = new Span
            {
                Text = url,
                TextColor = linkColor,
                TextDecorations = TextDecorations.Underline,
                FontSize = label.FontSize
            };

            var tapGesture = new TapGestureRecognizer();
            var capturedUrl = url;
            tapGesture.Tapped += async (_, _) =>
            {
                try { await Launcher.OpenAsync(new Uri(capturedUrl)); } catch { }
            };
            span.GestureRecognizers.Add(tapGesture);
            formatted.Spans.Add(span);

            lastIndex = match.Index + match.Length;
        }

        // Add remaining text after last URL
        if (lastIndex < text.Length)
        {
            formatted.Spans.Add(new Span
            {
                Text = text[lastIndex..],
                TextColor = textColor,
                FontSize = label.FontSize
            });
        }

        label.FormattedText = formatted;
    }
}
