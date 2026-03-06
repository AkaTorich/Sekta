using Sekta.Client.ViewModels;

namespace Sekta.Client.Views;

public partial class ChannelPage : ContentPage
{
    public ChannelPage(ChannelViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
