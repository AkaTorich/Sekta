using System.Collections.Specialized;
using Sekta.Client.Services;
using Sekta.Client.ViewModels;

namespace Sekta.Client.Controls;

public partial class EmbeddedChatView : ContentView
{
    private bool _isAtBottom = true;
    private bool _ignoreScroll;

    public EmbeddedChatView()
    {
        InitializeComponent();
        DebugLog.Log("EmbeddedChatView CONSTRUCTOR called");
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        DebugLog.Log($"EmbeddedChatView.OnBindingContextChanged: BindingContext={BindingContext?.GetType().Name ?? "null"}");

        if (BindingContext is ChatViewModel vm)
        {
            vm.Messages.CollectionChanged += OnMessagesChanged;
            vm.ScrollToItem += OnScrollToItem;
            // Re-subscribe when Messages property is replaced with a new collection
            vm.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(ChatViewModel.Messages) && s is ChatViewModel v)
                {
                    v.Messages.CollectionChanged += OnMessagesChanged;
                    // Scroll to bottom after collection replacement
                    MainThread.BeginInvokeOnMainThread(async () =>
                    {
                        await Task.Delay(100);
                        await MessagesScrollView.ScrollToAsync(0, MessagesScrollView.ContentSize.Height, false);
                    });
                }
            };
            DebugLog.Log($"EmbeddedChatView: subscribed to Messages.CollectionChanged, current count={vm.Messages.Count}");
        }
    }

    private void OnScrollToItem(object item)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            _ignoreScroll = true;
            try
            {
                var vm = BindingContext as ChatViewModel;
                if (vm != null)
                {
                    var index = vm.Messages.IndexOf((Sekta.Shared.DTOs.MessageDto)item);
                    if (index >= 0 && index < MessagesLayout.Children.Count)
                    {
                        var element = MessagesLayout.Children[index] as VisualElement;
                        if (element != null)
                            await MessagesScrollView.ScrollToAsync(element, ScrollToPosition.Start, false);
                    }
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
        DebugLog.Log($"EmbeddedChatView.OnMessagesChanged: Action={e.Action}, NewStartingIndex={e.NewStartingIndex}, NewItemsCount={e.NewItems?.Count ?? 0}");

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _isAtBottom = true;
            DebugLog.Log("  -> Reset, _isAtBottom=true");
            return;
        }

        if (e.Action == NotifyCollectionChangedAction.Replace)
        {
            DebugLog.Log("  -> Replace, skip");
            return;
        }

        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            DebugLog.Log($"  -> Not Add ({e.Action}), skip");
            return;
        }

        var vm = BindingContext as ChatViewModel;
        if (vm == null || vm.Messages.Count == 0)
        {
            DebugLog.Log("  -> vm null or Messages empty, skip");
            return;
        }

        DebugLog.Log($"  -> Add: Messages.Count={vm.Messages.Count}, LayoutChildren={MessagesLayout.Children.Count}, ScrollViewContent={MessagesScrollView.ContentSize}");

        // Force ScrollView to recalculate content size (iOS BindableLayout bug)
        MainThread.BeginInvokeOnMainThread(() =>
        {
            MessagesLayout.InvalidateMeasure();
            MessagesScrollView.InvalidateMeasure();
            DebugLog.Log($"  -> InvalidateMeasure done, LayoutChildren={MessagesLayout.Children.Count}");
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
