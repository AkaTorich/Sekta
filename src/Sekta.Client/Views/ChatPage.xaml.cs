using System.Collections.Specialized;
using Sekta.Client.Services;
using Sekta.Client.ViewModels;

namespace Sekta.Client.Views;

public partial class ChatPage : ContentPage
{
    private bool _isAtBottom = true;
    private bool _ignoreScroll;

    private readonly ChatViewModel _viewModel;

    public ChatPage(ChatViewModel viewModel, IAuthService authService)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        viewModel.Messages.CollectionChanged += OnMessagesChanged;
        viewModel.ScrollToItem += OnScrollToItem;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.Cleanup();
    }

    private void OnScrollToItem(object item)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _ignoreScroll = true;
            try
            {
                var index = _viewModel.Messages.IndexOf((Sekta.Shared.DTOs.MessageDto)item);
                if (index >= 0 && index < MessagesLayout.Children.Count)
                {
                    var element = MessagesLayout.Children[index] as VisualElement;
                    if (element != null)
                        await MessagesScrollView.ScrollToAsync(element, ScrollToPosition.Start, false);
                }
            }
            catch { }
            await Task.Delay(300);
            _ignoreScroll = false;
        });
    }

    private void OnScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        if (_ignoreScroll) return;

        var vm = BindingContext as ChatViewModel;
        if (vm == null || vm.Messages.Count == 0) return;

        var scrollView = MessagesScrollView;
        _isAtBottom = scrollView.ScrollY >= scrollView.ContentSize.Height - scrollView.Height - 50;

        if (vm.IsInitialLoadComplete)
            vm.ShowScrollToBottom = !_isAtBottom;
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _isAtBottom = true;
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Replace)
            return;

        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        var vm = BindingContext as ChatViewModel;
        if (vm == null || vm.Messages.Count == 0)
            return;

        // Force ScrollView to recalculate content size (iOS BindableLayout bug)
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MessagesLayout.InvalidateMeasure();
            MessagesScrollView.InvalidateMeasure();
        });

        if (e.NewStartingIndex == 0)
            return;

        if (_isAtBottom)
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(100);
                await MessagesScrollView.ScrollToAsync(0, MessagesScrollView.ContentSize.Height, false);
            });
        }
        else if (vm.IsInitialLoadComplete)
        {
            MainThread.BeginInvokeOnMainThread(() => vm.ShowScrollToBottom = true);
        }
    }

    private async void OnScrollDownClicked(object? sender, EventArgs e)
    {
        var vm = BindingContext as ChatViewModel;
        if (vm == null || vm.Messages.Count == 0) return;

        _ignoreScroll = true;
        _isAtBottom = true;
        vm.ShowScrollToBottom = false;

        try
        {
            await MessagesScrollView.ScrollToAsync(0, MessagesScrollView.ContentSize.Height, true);
        }
        catch { }

        await Task.Delay(500);
        _ignoreScroll = false;
    }
}
