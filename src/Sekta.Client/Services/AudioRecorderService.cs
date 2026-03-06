namespace Sekta.Client.Services;

public interface IAudioRecorderService
{
    bool IsRecording { get; }
    TimeSpan RecordingDuration { get; }
    Task StartRecordingAsync();
    Task<string?> StopRecordingAsync();
}

public class AudioRecorderService : IAudioRecorderService, IDisposable
{
    private bool _isRecording;
    private DateTime _recordingStartTime;
    private string? _currentFilePath;
    private IDispatcherTimer? _durationTimer;
    private TimeSpan _recordingDuration;

#if WINDOWS
    private Windows.Media.Capture.MediaCapture? _mediaCapture;
#endif

    public bool IsRecording => _isRecording;
    public TimeSpan RecordingDuration => _recordingDuration;

    public event EventHandler<TimeSpan>? DurationUpdated;

    public async Task StartRecordingAsync()
    {
        if (_isRecording)
            return;

        _currentFilePath = Path.Combine(FileSystem.CacheDirectory, $"voice_{DateTime.UtcNow:yyyyMMdd_HHmmss}.m4a");
        _isRecording = true;
        _recordingStartTime = DateTime.UtcNow;
        _recordingDuration = TimeSpan.Zero;

        StartDurationTimer();

#if WINDOWS
        await StartWindowsRecordingAsync();
#elif ANDROID
        StartAndroidRecording();
        await Task.CompletedTask;
#elif IOS || MACCATALYST
        StartAppleRecording();
        await Task.CompletedTask;
#else
        await Task.CompletedTask;
#endif
    }

    public async Task<string?> StopRecordingAsync()
    {
        if (!_isRecording)
            return null;

        _isRecording = false;
        _recordingDuration = DateTime.UtcNow - _recordingStartTime;
        StopDurationTimer();

        if (_recordingDuration.TotalSeconds < 0.5)
        {
            await StopPlatformAsync();
            CleanupFile();
            return null;
        }

        await StopPlatformAsync();

        if (_currentFilePath is not null && File.Exists(_currentFilePath))
        {
            var size = new FileInfo(_currentFilePath).Length;
            System.Diagnostics.Debug.WriteLine($"AudioRecorder: file={_currentFilePath}, size={size}");
            if (size > 100)
                return _currentFilePath;
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"AudioRecorder: file missing or null path={_currentFilePath}");
        }

        CleanupFile();
        return null;
    }

    // ═══════════════ WINDOWS ═══════════════
#if WINDOWS
    private Windows.Storage.Streams.InMemoryRandomAccessStream? _memStream;

    private async Task StartWindowsRecordingAsync()
    {
        try
        {
            _mediaCapture = new Windows.Media.Capture.MediaCapture();
            await _mediaCapture.InitializeAsync(new Windows.Media.Capture.MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = Windows.Media.Capture.StreamingCaptureMode.Audio
            });

            _memStream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            var profile = Windows.Media.MediaProperties.MediaEncodingProfile.CreateMp3(
                Windows.Media.MediaProperties.AudioEncodingQuality.Medium);

            await _mediaCapture.StartRecordToStreamAsync(profile, _memStream);
            System.Diagnostics.Debug.WriteLine("AudioRecorder Win: recording started OK");
        }
        catch (UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("AudioRecorder Win: microphone access denied");
            MainThread.BeginInvokeOnMainThread(async () =>
                await Shell.Current.DisplayAlert("Microphone", "Microphone access denied. Enable it in Windows Settings → Privacy → Microphone.", "OK"));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AudioRecorder Win start: {ex.Message}");
        }
    }

    private async Task StopWindowsRecordingAsync()
    {
        try
        {
            if (_mediaCapture is not null)
            {
                await _mediaCapture.StopRecordAsync();
                _mediaCapture.Dispose();
                _mediaCapture = null;
            }

            if (_memStream is not null && _currentFilePath is not null)
            {
                _currentFilePath = Path.ChangeExtension(_currentFilePath, ".mp3");
                using var fileStream = File.Create(_currentFilePath);
                _memStream.Seek(0);
                await _memStream.AsStreamForRead().CopyToAsync(fileStream);
                _memStream.Dispose();
                _memStream = null;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AudioRecorder Win stop: {ex.Message}");
        }
    }
#endif

    // ═══════════════ ANDROID ═══════════════
#if ANDROID
    private Android.Media.MediaRecorder? _androidRecorder;

    private void StartAndroidRecording()
    {
        try
        {
            _currentFilePath = Path.ChangeExtension(_currentFilePath, ".mp4");
            _androidRecorder = new Android.Media.MediaRecorder();
            _androidRecorder.SetAudioSource(Android.Media.AudioSource.Mic);
            _androidRecorder.SetOutputFormat(Android.Media.OutputFormat.Mpeg4);
            _androidRecorder.SetAudioEncoder(Android.Media.AudioEncoder.Aac);
            _androidRecorder.SetAudioSamplingRate(44100);
            _androidRecorder.SetAudioEncodingBitRate(128000);
            _androidRecorder.SetOutputFile(_currentFilePath);
            _androidRecorder.Prepare();
            _androidRecorder.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AudioRecorder Android start: {ex.Message}");
        }
    }

    private void StopAndroidRecording()
    {
        try
        {
            _androidRecorder?.Stop();
            _androidRecorder?.Release();
            _androidRecorder?.Dispose();
            _androidRecorder = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AudioRecorder Android stop: {ex.Message}");
        }
    }
#endif

    // ═══════════════ iOS / macOS ═══════════════
#if IOS || MACCATALYST
    private AVFoundation.AVAudioRecorder? _appleRecorder;

    private void StartAppleRecording()
    {
        try
        {
            var audioSession = AVFoundation.AVAudioSession.SharedInstance();
            audioSession.SetCategory(AVFoundation.AVAudioSessionCategory.Record);
            audioSession.SetActive(true);

            _currentFilePath = Path.ChangeExtension(_currentFilePath, ".m4a");
            var url = Foundation.NSUrl.FromFilename(_currentFilePath!);

            var settingsDict = new Foundation.NSDictionary(
                AVFoundation.AVAudioSettings.AVFormatIDKey, (Foundation.NSNumber)1633772320, // kAudioFormatMPEG4AAC = 'aac ' = 0x61616300
                AVFoundation.AVAudioSettings.AVSampleRateKey, (Foundation.NSNumber)44100.0f,
                AVFoundation.AVAudioSettings.AVNumberOfChannelsKey, (Foundation.NSNumber)1,
                AVFoundation.AVAudioSettings.AVEncoderAudioQualityKey, (Foundation.NSNumber)(int)AVFoundation.AVAudioQuality.High
            );
            var settings = new AVFoundation.AudioSettings(settingsDict);

            _appleRecorder = AVFoundation.AVAudioRecorder.Create(url, settings, out var error);
            if (error is not null)
            {
                System.Diagnostics.Debug.WriteLine($"AudioRecorder Apple init: {error.LocalizedDescription}");
                return;
            }
            _appleRecorder?.Record();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AudioRecorder Apple start: {ex.Message}");
        }
    }

    private void StopAppleRecording()
    {
        try
        {
            _appleRecorder?.Stop();
            _appleRecorder?.Dispose();
            _appleRecorder = null;

            var audioSession = AVFoundation.AVAudioSession.SharedInstance();
            audioSession.SetActive(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AudioRecorder Apple stop: {ex.Message}");
        }
    }
#endif

    // ═══════════════ Shared ═══════════════
    private async Task StopPlatformAsync()
    {
#if WINDOWS
        await StopWindowsRecordingAsync();
#elif ANDROID
        StopAndroidRecording();
        await Task.CompletedTask;
#elif IOS || MACCATALYST
        StopAppleRecording();
        await Task.CompletedTask;
#else
        await Task.CompletedTask;
#endif
    }

    private void StartDurationTimer()
    {
        _durationTimer = Application.Current?.Dispatcher.CreateTimer();
        if (_durationTimer is not null)
        {
            _durationTimer.Interval = TimeSpan.FromMilliseconds(100);
            _durationTimer.Tick += OnDurationTimerTick;
            _durationTimer.Start();
        }
    }

    private void StopDurationTimer()
    {
        if (_durationTimer is not null)
        {
            _durationTimer.Stop();
            _durationTimer.Tick -= OnDurationTimerTick;
            _durationTimer = null;
        }
    }

    private void OnDurationTimerTick(object? sender, EventArgs e)
    {
        if (_isRecording)
        {
            _recordingDuration = DateTime.UtcNow - _recordingStartTime;
            DurationUpdated?.Invoke(this, _recordingDuration);
        }
    }

    private void CleanupFile()
    {
        try
        {
            if (_currentFilePath is not null && File.Exists(_currentFilePath))
                File.Delete(_currentFilePath);
        }
        catch { }
        _currentFilePath = null;
    }

    public void Dispose()
    {
        StopDurationTimer();
#if WINDOWS
        _mediaCapture?.Dispose();
        _mediaCapture = null;
        _memStream?.Dispose();
        _memStream = null;
#elif ANDROID
        _androidRecorder?.Release();
        _androidRecorder?.Dispose();
        _androidRecorder = null;
#elif IOS || MACCATALYST
        _appleRecorder?.Dispose();
        _appleRecorder = null;
#endif
        CleanupFile();
        GC.SuppressFinalize(this);
    }
}
