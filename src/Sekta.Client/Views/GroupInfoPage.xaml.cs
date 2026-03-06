using Sekta.Client.ViewModels;

namespace Sekta.Client.Views;

public partial class GroupInfoPage : ContentPage
{
    public GroupInfoPage(GroupInfoViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
