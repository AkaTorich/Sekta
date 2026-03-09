using System.Globalization;
using Sekta.Client.Services;
using Sekta.Shared.DTOs;
using Sekta.Shared.Enums;

namespace Sekta.Client.Converters;

public class BoolToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b && b)
            return Color.FromArgb("#2AABEE"); // outgoing - blue
        return Color.FromArgb("#E5E5EA"); // incoming - gray
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}

public class MessageAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isOwn && isOwn)
            return LayoutOptions.End;
        return LayoutOptions.Start;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Helper to resolve IAuthService from MAUI DI container.
/// </summary>
internal static class ConverterHelper
{
    private static IAuthService? _cachedAuthService;
    private static ISettingsService? _cachedSettingsService;

    public static IAuthService? GetAuthService()
    {
        if (_cachedAuthService != null) return _cachedAuthService;
#if ANDROID
        _cachedAuthService = IPlatformApplication.Current?.Services.GetService<IAuthService>();
#else
        _cachedAuthService = Application.Current?.Handler?.MauiContext?.Services.GetService<IAuthService>();
#endif
        return _cachedAuthService;
    }

    public static ISettingsService? GetSettingsService()
    {
        if (_cachedSettingsService != null) return _cachedSettingsService;
#if ANDROID
        _cachedSettingsService = IPlatformApplication.Current?.Services.GetService<ISettingsService>();
#else
        _cachedSettingsService = Application.Current?.Handler?.MauiContext?.Services.GetService<ISettingsService>();
#endif
        return _cachedSettingsService;
    }

    public static bool IsOwnMessage(Guid senderId)
    {
        var auth = GetAuthService();
        return auth?.CurrentUser != null && senderId == auth.CurrentUser.Id;
    }

    public static bool IsDarkTheme()
    {
        // Read directly from persisted settings — always correct
        var settings = GetSettingsService();
        return settings?.DarkMode ?? false;
    }
}

/// <summary>
/// Converts SenderId (Guid) to HorizontalOptions: End for own messages, Start for others.
/// </summary>
public class SenderToAlignmentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Guid senderId && ConverterHelper.IsOwnMessage(senderId))
            return LayoutOptions.End;
        return LayoutOptions.Start;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts SenderId (Guid) to bubble background color.
/// Light theme: green outgoing, white incoming.
/// Dark theme: dark-teal outgoing, dark-gray incoming.
/// </summary>
public class SenderToBubbleColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isDark = ConverterHelper.IsDarkTheme();
        if (value is Guid senderId && ConverterHelper.IsOwnMessage(senderId))
            return Color.FromArgb(isDark ? "#2B5278" : "#EFFDDE");
        return Color.FromArgb(isDark ? "#182533" : "#FFFFFF");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts SenderId to bool: true if own message.
/// </summary>
public class SenderToIsOwnConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Guid senderId)
            return ConverterHelper.IsOwnMessage(senderId);
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts SenderId to inverse bool: hides sender name for own messages.
/// </summary>
public class SenderToShowNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Guid senderId)
            return !ConverterHelper.IsOwnMessage(senderId);
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value?.ToString() switch
        {
            "Online" => Colors.LimeGreen,
            "Away" => Colors.Orange,
            _ => Colors.Gray
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class NullToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return false;
        if (value is int i) return i > 0;
        if (value is string s) return !string.IsNullOrEmpty(s);
        return true;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DateTimeToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
        {
            if (dt.Date == DateTime.Today)
                return dt.ToString("HH:mm");
            if (dt.Date == DateTime.Today.AddDays(-1))
                return "Вчера";
            return dt.ToString("dd.MM.yy");
        }
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class MessageTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt)
            return dt.ToString("HH:mm");
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts MessageStatus enum to checkmark text.
/// Sent = single check, Delivered/Read = double check.
/// </summary>
public class MessageStatusToCheckConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MessageStatus status)
        {
            return status switch
            {
                MessageStatus.Sent => "\u2713",       // single checkmark
                MessageStatus.Delivered => "\u2713\u2713", // double checkmark
                MessageStatus.Read => "\u2713\u2713",      // double checkmark (blue color handled separately)
                _ => ""
            };
        }
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts MessageStatus enum to checkmark color.
/// Sent/Delivered = gray, Read = blue.
/// </summary>
public class MessageStatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MessageStatus status && status == MessageStatus.Read)
            return Color.FromArgb("#2AABEE"); // blue for read
        return Color.FromArgb("#707579"); // gray for sent/delivered
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Shows checkmarks only for own messages (returns true if own message).
/// Binds to SenderId.
/// </summary>
public class SenderToShowCheckConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Guid senderId)
            return ConverterHelper.IsOwnMessage(senderId);
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts relative MediaUrl (/uploads/file.png) to full server URL.
/// </summary>
public class MediaUrlConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string url || string.IsNullOrEmpty(url))
            return null;

        // Handle local emotes (emote:aa.gif)
        if (url.StartsWith("emote:"))
        {
            var fileName = url[6..]; // e.g., "aa.gif"
            var resourceName = $"Sekta.Client.emotes.{fileName}";
            var assembly = typeof(MediaUrlConverter).Assembly;
            return ImageSource.FromStream(() => assembly.GetManifestResourceStream(resourceName)!);
        }

        // Handle local cached files (absolute paths from FileCacheService)
        if (Path.IsPathRooted(url) && File.Exists(url))
        {
            return ImageSource.FromFile(url);
        }

        string fullUrl;
        if (url.StartsWith("http"))
        {
            fullUrl = url;
        }
        else
        {
            var settings = ConverterHelper.GetSettingsService();
            var baseUrl = settings?.ServerUrl?.TrimEnd('/') ?? "http://localhost:5000";
            fullUrl = $"{baseUrl}{url}";
        }

        return ImageSource.FromUri(new Uri(fullUrl));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns true if MessageType is Photo, Video, or non-emote Sticker.
/// </summary>
public class MessageTypeToImageVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MessageType type)
            return type is MessageType.Photo or MessageType.Video or MessageType.Sticker;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns true if MediaUrl starts with "emote:" (local emote sticker).
/// </summary>
public class IsEmoteConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string url && url.StartsWith("emote:");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns true if the message has a displayable image that is NOT an emote.
/// Bind to MediaUrl; parameter should be the MessageType (passed via converter parameter is not feasible,
/// so this checks if the URL does NOT start with "emote:" and is not null).
/// </summary>
public class IsNonEmoteImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string url && !string.IsNullOrEmpty(url) && !url.StartsWith("emote:");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns true if MessageType is File or Voice (non-image attachment).
/// </summary>
public class MessageTypeToFileVisibleConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is MessageType type)
            return type is MessageType.File or MessageType.Voice;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a MessageDto to a chat list preview string like "You: message" or "Name: message".
/// </summary>
public class LastMessagePreviewConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MessageDto msg)
            return "";

        var prefix = "";
        if (msg.SenderId != Guid.Empty)
        {
            var isOwn = ConverterHelper.IsOwnMessage(msg.SenderId);
            if (isOwn)
                prefix = "You: ";
            else if (!string.IsNullOrEmpty(msg.SenderName))
                prefix = $"{msg.SenderName}: ";
        }

        var content = msg.Type switch
        {
            MessageType.Photo => "\ud83d\udcf7 Photo",
            MessageType.Video => "\ud83c\udfa5 Video",
            MessageType.Voice => "\ud83c\udfa4 Voice message",
            MessageType.File => $"\ud83d\udcce {msg.FileName ?? "File"}",
            MessageType.Sticker => "Sticker",
            _ => msg.Content ?? ""
        };

        return $"{prefix}{content}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns the first character of a string (uppercase) for avatar initials.
/// </summary>
/// <summary>
/// Converts an emote code (e.g., "aa") to an ImageSource from bundled emote assets.
/// </summary>
public class EmoteSourceConverter : IValueConverter
{
    private static readonly System.Reflection.Assembly _assembly = typeof(EmoteSourceConverter).Assembly;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string code && !string.IsNullOrEmpty(code))
        {
            var resourceName = $"Sekta.Client.emotes.{code}.gif";
            return ImageSource.FromStream(() => _assembly.GetManifestResourceStream(resourceName)!);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class FirstCharConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s.Length > 0)
            return s[0].ToString().ToUpper();
        return "?";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class TruncateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s) return value;
        var max = 20;
        if (parameter is string p && int.TryParse(p, out var n)) max = n;
        return s.Length <= max ? s : s[..max] + "…";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
