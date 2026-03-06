using Sekta.Client.Views;

namespace Sekta.Client;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes for navigation
        Routing.RegisterRoute("chat", typeof(ChatPage));
        Routing.RegisterRoute("register", typeof(RegisterPage));
        Routing.RegisterRoute("channel", typeof(ChannelPage));
        Routing.RegisterRoute("call", typeof(CallPage));
        Routing.RegisterRoute("stickers", typeof(StickersPage));
        Routing.RegisterRoute("creategroup", typeof(CreateGroupPage));
        Routing.RegisterRoute("groupinfo", typeof(GroupInfoPage));
        Routing.RegisterRoute("settings-standalone", typeof(SettingsPage));
        Routing.RegisterRoute("profile-standalone", typeof(ProfilePage));
        Routing.RegisterRoute("userprofile", typeof(UserProfilePage));
        Routing.RegisterRoute("forward", typeof(ForwardPage));

        // Hide tab bar on desktop — everything is accessed from the main page
        if (DeviceInfo.Idiom == DeviceIdiom.Desktop)
        {
            Shell.SetTabBarIsVisible(this, false);
        }
    }
}
