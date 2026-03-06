using Sekta.Client.Controls;
using Sekta.Client.ViewModels;
using Sekta.Shared.DTOs;

namespace Sekta.Client.Views;

public partial class ChatsListPage : ContentPage
{
    private readonly ChatsListViewModel _viewModel;
    private EmbeddedChatView? _embeddedChat;

    public ChatsListPage(ChatsListViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        // Detect desktop idiom for split-view
        _viewModel.IsDesktopMode = DeviceInfo.Idiom == DeviceIdiom.Desktop;

        // Add right-click context menu on the chat list panel
#if WINDOWS
        ChatListPanel.Loaded += (_, _) =>
        {
            var flyout = new MenuFlyout();
            var createGroup = new MenuFlyoutItem { Text = "Create Group" };
            createGroup.Clicked += (_, _) => _viewModel.CreateGroupCommand.Execute(null);
            flyout.Add(createGroup);
            FlyoutBase.SetContextFlyout(ChatListPanel, flyout);
        };
#endif

        // Listen for active chat changes to update embedded view
        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatsListViewModel.ActiveChatViewModel))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_viewModel.ActiveChatViewModel is not null)
                    {
                        EnsureEmbeddedChat();
                        _embeddedChat!.BindingContext = _viewModel.ActiveChatViewModel;
                        _embeddedChat.IsVisible = true;
                        EmbeddedChatHost.IsVisible = true;
                        NoChatPlaceholder.IsVisible = false;
                    }
                    else
                    {
                        if (_embeddedChat is not null)
                            _embeddedChat.IsVisible = false;
                        EmbeddedChatHost.IsVisible = false;
                        NoChatPlaceholder.IsVisible = true;
                    }
                });
            }
        };
    }

    private void EnsureEmbeddedChat()
    {
        if (_embeddedChat is not null) return;
        _embeddedChat = new EmbeddedChatView();
        EmbeddedChatHost.Content = _embeddedChat;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RefreshCommand.ExecuteAsync(null);
    }

    private void OnChatItemLoaded(object? sender, EventArgs e)
    {
        if (sender is not Grid grid) return;
        if (grid.BindingContext is not ChatDto chat) return;

#if WINDOWS
        // Windows: native right-click context menu with folder submenu
        var flyout = BuildWindowsContextMenu(chat);
        FlyoutBase.SetContextFlyout(grid, flyout);
#endif

        // All platforms: long press via platform handler
        AttachLongPress(grid, chat);
    }

    private void AttachLongPress(Grid grid, ChatDto chat)
    {
#if ANDROID
        var handler = grid.Handler?.PlatformView as Android.Views.View;
        if (handler is null) return;
        handler.LongClick += (_, _) => ShowChatContextMenu(chat);
#elif IOS || MACCATALYST
        var handler = grid.Handler?.PlatformView as UIKit.UIView;
        if (handler is null) return;
        var longPress = new UIKit.UILongPressGestureRecognizer(g =>
        {
            if (g.State == UIKit.UIGestureRecognizerState.Began)
                MainThread.BeginInvokeOnMainThread(() => ShowChatContextMenu(chat));
        });
        handler.AddGestureRecognizer(longPress);
#endif
    }

    private async void ShowChatContextMenu(ChatDto chat)
    {
        var inFolder = _viewModel.SelectedFolder is not null;
        var pinLabel = chat.IsPinned ? "Unpin" : "Pin";
        var notifLabel = _viewModel.IsNotificationsEnabled ? "Disable notifications" : "Enable notifications";
        var actions = new List<string> { pinLabel, notifLabel };

        if (_viewModel.Folders.Count > 0)
            actions.Add("Add to folder");

        var destructive = inFolder ? "Remove from folder" : "Delete";

        var result = await DisplayActionSheet(
            chat.Title ?? "Chat", "Cancel", destructive,
            actions.ToArray());

        if (result is null or "Cancel") return;

        switch (result)
        {
            case "Pin":
            case "Unpin":
                _viewModel.PinChatCommand.Execute(chat);
                break;
            case "Enable notifications":
            case "Disable notifications":
                _viewModel.ToggleNotificationsCommand.Execute(null);
                break;
            case "Add to folder":
                await ShowFolderPicker(chat);
                break;
            case "Remove from folder":
                await _viewModel.RemoveChatFromFolderAsync(chat);
                break;
            case "Delete":
                _viewModel.DeleteChatCommand.Execute(chat);
                break;
        }
    }

    private async Task ShowFolderPicker(ChatDto chat)
    {
        var folderNames = _viewModel.Folders.Select(f => f.Name).ToArray();
        var chosen = await DisplayActionSheet("Choose folder", "Cancel", null, folderNames);
        if (chosen is null or "Cancel") return;

        var folder = _viewModel.Folders.FirstOrDefault(f => f.Name == chosen);
        if (folder is null) return;

        await _viewModel.AddChatToSpecificFolderAsync(chat, folder);
    }

#if WINDOWS
    private MenuFlyout BuildWindowsContextMenu(ChatDto chat)
    {
        var flyout = new MenuFlyout();

        var pinItem = new MenuFlyoutItem { Text = chat.IsPinned ? "Unpin" : "Pin" };
        pinItem.Clicked += (_, _) => _viewModel.PinChatCommand.Execute(chat);
        flyout.Add(pinItem);

        var notifLabel = _viewModel.IsNotificationsEnabled ? "Disable notifications" : "Enable notifications";
        var notifItem = new MenuFlyoutItem { Text = notifLabel };
        notifItem.Clicked += (_, _) => _viewModel.ToggleNotificationsCommand.Execute(null);
        flyout.Add(notifItem);

        if (_viewModel.Folders.Count > 0)
        {
            var folderSub = new MenuFlyoutSubItem { Text = "Add to folder" };
            foreach (var folder in _viewModel.Folders)
            {
                var f = folder;
                var item = new MenuFlyoutItem { Text = f.Name };
                item.Clicked += async (_, _) =>
                {
                    try { await _viewModel.AddChatToSpecificFolderAsync(chat, f); }
                    catch { }
                };
                folderSub.Add(item);
            }
            flyout.Add(folderSub);
        }

        flyout.Add(new MenuFlyoutSeparator());

        var createGroupItem = new MenuFlyoutItem { Text = "Create Group" };
        createGroupItem.Clicked += (_, _) => _viewModel.CreateGroupCommand.Execute(null);
        flyout.Add(createGroupItem);

        flyout.Add(new MenuFlyoutSeparator());

        if (_viewModel.SelectedFolder is not null)
        {
            var removeItem = new MenuFlyoutItem { Text = "Remove from folder" };
            removeItem.Clicked += async (_, _) =>
            {
                try { await _viewModel.RemoveChatFromFolderAsync(chat); }
                catch { }
            };
            flyout.Add(removeItem);
        }
        else
        {
            var deleteItem = new MenuFlyoutItem { Text = "Delete" };
            deleteItem.Clicked += (_, _) => _viewModel.DeleteChatCommand.Execute(chat);
            flyout.Add(deleteItem);
        }

        return flyout;
    }
#endif
}
