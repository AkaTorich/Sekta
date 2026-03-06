namespace Sekta.Shared;

public static class HubRoutes
{
    public const string Chat = "/hubs/chat";
    public const string Call = "/hubs/call";
}

public static class ApiRoutes
{
    public const string Base = "/api";
    public const string Auth = $"{Base}/auth";
    public const string Users = $"{Base}/users";
    public const string Chats = $"{Base}/chats";
    public const string Channels = $"{Base}/channels";
    public const string Files = $"{Base}/files";
    public const string Stickers = $"{Base}/stickers";
    public const string Bot = $"{Base}/bot";
    public const string Notifications = $"{Base}/notifications";
    public const string Folders = $"{Base}/folders";
}
