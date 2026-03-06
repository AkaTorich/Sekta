using Sekta.Client.ViewModels;

namespace Sekta.Client.Views;

public partial class CallPage : ContentPage
{
    public CallPage(CallViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
