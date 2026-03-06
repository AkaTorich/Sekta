using Microsoft.Toolkit.Uwp.Notifications;

namespace Sekta.Client.Platforms.Windows;

/// <summary>
/// Shows native Windows toast notifications in the bottom-right corner,
/// visible even when the app is minimized.
/// </summary>
public static class ToastWindow
{
    private static bool _initialized;

    public static void Show(string senderName, string messageText, Guid chatId, string chatTitle)
    {
        try
        {
            if (!_initialized)
            {
                ToastNotificationManagerCompat.OnActivated += OnToastActivated;
                _initialized = true;
            }

            new ToastContentBuilder()
                .AddArgument("chatId", chatId.ToString())
                .AddArgument("chatTitle", chatTitle)
                .AddText(senderName)
                .AddText(messageText)
                .Show(toast =>
                {
                    toast.ExpirationTime = DateTimeOffset.Now.AddSeconds(8);
                });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to show Windows toast: {ex.Message}");
        }
    }

    private static void OnToastActivated(ToastNotificationActivatedEventArgsCompat e)
    {
        var args = ToastArguments.Parse(e.Argument);

        if (args.TryGetValue("chatId", out var chatIdStr) &&
            args.TryGetValue("chatTitle", out var title))
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    // Bring the app window to front
                    var nativeWindow = Application.Current?.Windows.FirstOrDefault()
                        ?.Handler?.PlatformView as Microsoft.UI.Xaml.Window;
                    if (nativeWindow != null)
                    {
                        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                        RestoreAndFocus(hwnd);
                    }

                    var url = $"chat?chatId={chatIdStr}&chatTitle={Uri.EscapeDataString(title)}";
                    await Shell.Current.GoToAsync(url);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to handle toast click: {ex.Message}");
                }
            });
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hWnd);

    private static void RestoreAndFocus(nint hwnd)
    {
        const int SW_RESTORE = 9;
        ShowWindow(hwnd, SW_RESTORE);
        SetForegroundWindow(hwnd);
    }
}
