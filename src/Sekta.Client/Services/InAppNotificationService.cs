using Sekta.Shared.DTOs;

namespace Sekta.Client.Services;

/// <summary>
/// Shows in-app notification banners at the top of the screen
/// when messages arrive for chats the user is not currently viewing.
/// </summary>
public class InAppNotificationService
{
    private readonly IAuthService _authService;
    private Border? _currentBanner;
    private CancellationTokenSource? _dismissCts;

    /// <summary>
    /// The chat ID currently being viewed by the user.
    /// Set by ChatViewModel/ChatsListViewModel to suppress banners for active chat.
    /// </summary>
    public Guid? ActiveChatId { get; set; }

    public InAppNotificationService(IAuthService authService)
    {
        _authService = authService;
    }

    public void ShowMessageBanner(MessageDto message)
    {
        if (message.SenderId == _authService.CurrentUser?.Id)
            return;

        if (ActiveChatId == message.ChatId)
            return;

        var senderName = message.SenderName ?? "New message";
        var content = message.Type == Shared.Enums.MessageType.Text
            ? message.Content ?? ""
            : message.Type.ToString();
        if (content.Length > 60)
            content = content[..60] + "...";
        var chatTitle = message.SenderName ?? "Chat";

#if WINDOWS
        MainThread.BeginInvokeOnMainThread(() =>
            Platforms.Windows.ToastWindow.Show(senderName, content, message.ChatId, chatTitle));
#else
        MainThread.BeginInvokeOnMainThread(() => ShowBannerOnCurrentPage(message));
#endif
    }

    private void ShowBannerOnCurrentPage(MessageDto message)
    {
        try
        {
            var page = Shell.Current?.CurrentPage;
            if (page == null) return;

            // Dismiss previous banner
            DismissCurrentBanner();

            var senderName = message.SenderName ?? "New message";
            var content = message.Type == Shared.Enums.MessageType.Text
                ? message.Content ?? ""
                : message.Type.ToString();
            if (content.Length > 60)
                content = content[..60] + "...";

            var chatId = message.ChatId;
            var chatTitle = message.SenderName ?? "Chat";

            var banner = new Border
            {
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Stroke = Colors.Transparent,
                BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Dark
                    ? Color.FromArgb("#2B3845")
                    : Color.FromArgb("#FFFFFF"),
                Padding = new Thickness(14, 10),
                Margin = new Thickness(12, 8, 12, 0),
                Shadow = new Shadow
                {
                    Brush = new SolidColorBrush(Colors.Black),
                    Offset = new Point(0, 2),
                    Radius = 8,
                    Opacity = 0.25f
                },
                HorizontalOptions = LayoutOptions.Fill,
                VerticalOptions = LayoutOptions.Start,
                ZIndex = 999,
                Content = new Grid
                {
                    ColumnDefinitions =
                    [
                        new ColumnDefinition(new GridLength(36)),
                        new ColumnDefinition(GridLength.Star)
                    ],
                    ColumnSpacing = 10,
                    Children =
                    {
                        CreateAvatar(senderName),
                        CreateTextContent(senderName, content)
                    }
                }
            };

            // Tap to navigate to the chat
            var tap = new TapGestureRecognizer();
            tap.Tapped += async (_, _) =>
            {
                DismissCurrentBanner();
                var url = $"chat?chatId={chatId}&chatTitle={Uri.EscapeDataString(chatTitle)}";
                await Shell.Current.GoToAsync(url);
            };
            banner.GestureRecognizers.Add(tap);

            // Swipe up to dismiss
            var swipe = new SwipeGestureRecognizer { Direction = SwipeDirection.Up };
            swipe.Swiped += (_, _) => DismissCurrentBanner();
            banner.GestureRecognizers.Add(swipe);

            _currentBanner = banner;

            // Insert banner into the page's content
            InsertBannerIntoPage(page, banner);

            // Slide-in animation
            banner.TranslationY = -80;
            banner.Opacity = 0;
            banner.TranslateTo(0, 0, 250, Easing.CubicOut);
            banner.FadeTo(1, 200);

            // Auto-dismiss after 4 seconds
            _dismissCts?.Cancel();
            _dismissCts = new CancellationTokenSource();
            _ = AutoDismissAsync(_dismissCts.Token);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to show notification banner: {ex.Message}");
        }
    }

    private static Border CreateAvatar(string name)
    {
        var avatar = new Border
        {
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            Stroke = Colors.Transparent,
            WidthRequest = 36,
            HeightRequest = 36,
            BackgroundColor = Color.FromArgb("#2AABEE"),
            Content = new Label
            {
                Text = name.Length > 0 ? name[..1].ToUpper() : "?",
                FontSize = 16,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        Grid.SetColumn(avatar, 0);
        return avatar;
    }

    private static VerticalStackLayout CreateTextContent(string senderName, string content)
    {
        var isDark = Application.Current?.RequestedTheme == AppTheme.Dark;
        var layout = new VerticalStackLayout
        {
            Spacing = 1,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = senderName,
                    FontSize = 14,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#2AABEE"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1
                },
                new Label
                {
                    Text = content,
                    FontSize = 13,
                    TextColor = isDark ? Color.FromArgb("#C8D2DC") : Color.FromArgb("#555555"),
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1
                }
            }
        };
        Grid.SetColumn(layout, 1);
        return layout;
    }

    private void InsertBannerIntoPage(Page page, Border banner)
    {
        if (page is not ContentPage contentPage) return;

        if (contentPage.Content is Grid grid)
        {
            // Span all rows and columns so the banner floats on top of everything
            if (grid.RowDefinitions.Count > 0)
                Grid.SetRowSpan(banner, Math.Max(grid.RowDefinitions.Count, 1));
            if (grid.ColumnDefinitions.Count > 0)
                Grid.SetColumnSpan(banner, grid.ColumnDefinitions.Count);
            grid.Children.Add(banner);
        }
        else if (contentPage.Content is Layout layout)
        {
            layout.Children.Add(banner);
        }
        else
        {
            var existingContent = contentPage.Content;
            var wrapper = new Grid();
            contentPage.Content = wrapper;
            if (existingContent != null)
                wrapper.Children.Add(existingContent);
            wrapper.Children.Add(banner);
        }
    }

    private void DismissCurrentBanner()
    {
        _dismissCts?.Cancel();
        if (_currentBanner == null) return;

        var banner = _currentBanner;
        _currentBanner = null;

        try
        {
            if (banner.Parent is Layout parentLayout)
            {
                parentLayout.Children.Remove(banner);
            }
        }
        catch { }
    }

    private async Task AutoDismissAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(4000, ct);
            if (ct.IsCancellationRequested) return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_currentBanner == null) return;
                var banner = _currentBanner;
                banner.TranslateTo(0, -80, 200, Easing.CubicIn);
                banner.FadeTo(0, 200).ContinueWith(_ =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        if (_currentBanner == banner)
                            DismissCurrentBanner();
                    });
                });
            });
        }
        catch (TaskCanceledException) { }
    }
}
