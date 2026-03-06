using Sekta.Client.ViewModels;

namespace Sekta.Client.Views;

public partial class GroupsPage : ContentPage
{
    private readonly GroupsViewModel _viewModel;

    public GroupsPage(GroupsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;

        _viewModel.IsDesktopMode = DeviceInfo.Idiom == DeviceIdiom.Desktop;

        _viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GroupsViewModel.ActiveChatViewModel))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_viewModel.ActiveChatViewModel is not null)
                    {
                        EmbeddedChat.BindingContext = _viewModel.ActiveChatViewModel;
                        EmbeddedChat.IsVisible = true;
                        NoChatPlaceholder.IsVisible = false;
                    }
                    else
                    {
                        EmbeddedChat.IsVisible = false;
                        NoChatPlaceholder.IsVisible = true;
                    }
                });
            }
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RefreshCommand.ExecuteAsync(null);
    }
}
