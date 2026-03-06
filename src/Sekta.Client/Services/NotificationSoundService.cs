namespace Sekta.Client.Services;

public interface INotificationSoundService
{
    bool IsEnabled { get; set; }
    Task PlayNewMessageSoundAsync();
}

public class NotificationSoundService : INotificationSoundService
{
    private bool _isEnabled = true;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            Preferences.Default.Set("notifications_enabled", value);
        }
    }

    public NotificationSoundService()
    {
        _isEnabled = Preferences.Default.Get("notifications_enabled", true);
    }

    public async Task PlayNewMessageSoundAsync()
    {
        if (!_isEnabled) return;

        try
        {
            var stream = await FileSystem.OpenAppPackageFileAsync("new_message.mp3");
            if (stream is null) return;

#if WINDOWS
            PlayWindows(stream);
#elif ANDROID
            PlayAndroid(stream);
#elif IOS || MACCATALYST
            PlayApple(stream);
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to play notification sound: {ex.Message}");
        }
    }

#if WINDOWS
    private static void PlayWindows(Stream stream)
    {
        // Copy to temp file and play via Windows MediaPlayer
        var tempPath = Path.Combine(Path.GetTempPath(), "sekta_notification.mp3");
        using (var fs = File.Create(tempPath))
        {
            stream.CopyTo(fs);
        }
        stream.Dispose();

        var player = new Windows.Media.Playback.MediaPlayer
        {
            Source = Windows.Media.Core.MediaSource.CreateFromUri(new Uri(tempPath))
        };
        player.MediaEnded += (s, e) => player.Dispose();
        player.Play();
    }
#endif

#if ANDROID
    private static void PlayAndroid(Stream stream)
    {
        var tempPath = Path.Combine(FileSystem.CacheDirectory, "sekta_notification.mp3");
        using (var fs = File.Create(tempPath))
        {
            stream.CopyTo(fs);
        }
        stream.Dispose();

        var mediaPlayer = new Android.Media.MediaPlayer();
        mediaPlayer.SetAudioAttributes(new Android.Media.AudioAttributes.Builder()!
            .SetUsage(Android.Media.AudioUsageKind.NotificationEvent)!
            .SetContentType(Android.Media.AudioContentType.Sonification)!
            .Build()!);
        mediaPlayer.SetDataSource(tempPath);
        mediaPlayer.Prepare();
        mediaPlayer.Completion += (s, e) =>
        {
            mediaPlayer.Release();
        };
        mediaPlayer.Start();
    }
#endif

#if IOS || MACCATALYST
    private static AVFoundation.AVAudioPlayer? _audioPlayer;

    private static void PlayApple(Stream stream)
    {
        var tempPath = Path.Combine(FileSystem.CacheDirectory, "sekta_notification.mp3");
        using (var fs = File.Create(tempPath))
        {
            stream.CopyTo(fs);
        }
        stream.Dispose();

        var url = Foundation.NSUrl.FromFilename(tempPath);
        _audioPlayer = AVFoundation.AVAudioPlayer.FromUrl(url, out var error);
        if (error is not null)
        {
            System.Diagnostics.Debug.WriteLine($"AVAudioPlayer error: {error.LocalizedDescription}");
            return;
        }
        _audioPlayer.PrepareToPlay();
        _audioPlayer.Play();
    }
#endif
}
