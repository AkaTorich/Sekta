using Sekta.Client.ViewModels;

namespace Sekta.Client.Views;

public partial class ProfilePage : ContentPage
{
    public ProfilePage(ProfileViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.LoadCurrentUser();
    }
}
