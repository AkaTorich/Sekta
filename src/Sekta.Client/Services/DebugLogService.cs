namespace Sekta.Client.Services;

public static class DebugLog
{
    private static readonly object _lock = new();
    private static string? _logPath;

    private static string GetLogPath()
    {
        if (_logPath is not null) return _logPath;

        string downloadsPath;

#if ANDROID
        downloadsPath = Android.OS.Environment.GetExternalStoragePublicDirectory(
            Android.OS.Environment.DirectoryDownloads)!.AbsolutePath;
#elif IOS || MACCATALYST
        downloadsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
#elif WINDOWS
        downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
#else
        downloadsPath = FileSystem.AppDataDirectory;
#endif

        _logPath = Path.Combine(downloadsPath, "sekta_debug.log");
        return _logPath;
    }

    public static void Log(string message)
    {
        try
        {
            // var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
            // lock (_lock)
            // {
            //     File.AppendAllText(GetLogPath(), line + Environment.NewLine);
            // }
            System.Diagnostics.Debug.WriteLine($"[SEKTA] {message}");
        }
        catch { }
    }

    public static void Clear()
    {
        // try
        // {
        //     lock (_lock)
        //     {
        //         File.WriteAllText(GetLogPath(), $"=== Sekta Debug Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={Environment.NewLine}");
        //     }
        // }
        // catch { }
    }
}
