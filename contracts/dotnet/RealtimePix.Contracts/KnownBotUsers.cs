namespace RealtimePix.Contracts;

public sealed record BotUserDescriptor(string UserId, string DisplayName);

public static class KnownBotUsers
{
    public static readonly IReadOnlyList<BotUserDescriptor> All =
    [
        new("bot-aurora-ledger", "Aurora Ledger"),
        new("bot-indigo-pix", "Indigo PIX"),
        new("bot-silver-signal", "Silver Signal")
    ];
}

