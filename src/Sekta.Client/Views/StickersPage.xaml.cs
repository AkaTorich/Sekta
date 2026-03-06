using Sekta.Client.ViewModels;

namespace Sekta.Client.Views;

public partial class StickersPage : ContentPage
{
    public StickersPage(StickersViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is StickersViewModel vm)
            await vm.LoadPacksAsync();
    }
}
