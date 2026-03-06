using Sekta.Client.ViewModels;

namespace Sekta.Client.Views;

public partial class ForwardPage : ContentPage
{
    public ForwardPage(ForwardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
