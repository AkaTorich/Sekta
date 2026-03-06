using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using Sekta.Client.Services;
using Sekta.Client.ViewModels;
using Sekta.Client.Views;

namespace Sekta.Client;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Services
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IAuthService, AuthService>();
        builder.Services.AddSingleton<IApiService, ApiService>();
        builder.Services.AddSingleton<ISignalRService, SignalRService>();
        builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
        builder.Services.AddSingleton<IAudioRecorderService, AudioRecorderService>();
        builder.Services.AddSingleton<INotificationService, PushNotificationService>();
        builder.Services.AddSingleton<INotificationSoundService, NotificationSoundService>();
        builder.Services.AddSingleton<InAppNotificationService>();
        builder.Services.AddSingleton<IMessageCacheService, MessageCacheService>();
        builder.Services.AddSingleton<IChatCacheService, ChatCacheService>();

        // ViewModels
        builder.Services.AddTransient<LoginViewModel>();
        builder.Services.AddTransient<RegisterViewModel>();
        builder.Services.AddTransient<ChatsListViewModel>();
        builder.Services.AddTransient<ChatViewModel>();
        builder.Services.AddTransient<ContactsViewModel>();
        builder.Services.AddTransient<ProfileViewModel>();
        builder.Services.AddTransient<SettingsViewModel>();
        builder.Services.AddTransient<ChannelViewModel>();
        builder.Services.AddTransient<CallViewModel>();
        builder.Services.AddTransient<StickersViewModel>();
        builder.Services.AddTransient<GroupsViewModel>();
        builder.Services.AddTransient<CreateGroupViewModel>();
        builder.Services.AddTransient<GroupInfoViewModel>();
        builder.Services.AddTransient<UserProfileViewModel>();
        builder.Services.AddTransient<ForwardViewModel>();

        // Pages
        builder.Services.AddTransient<LoginPage>();
        builder.Services.AddTransient<RegisterPage>();
        builder.Services.AddTransient<ChatsListPage>();
        builder.Services.AddTransient<ChatPage>();
        builder.Services.AddTransient<ContactsPage>();
        builder.Services.AddTransient<ProfilePage>();
        builder.Services.AddTransient<SettingsPage>();
        builder.Services.AddTransient<ChannelPage>();
        builder.Services.AddTransient<CallPage>();
        builder.Services.AddTransient<StickersPage>();
        builder.Services.AddTransient<GroupsPage>();
        builder.Services.AddTransient<CreateGroupPage>();
        builder.Services.AddTransient<GroupInfoPage>();
        builder.Services.AddTransient<UserProfilePage>();
        builder.Services.AddTransient<ForwardPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
