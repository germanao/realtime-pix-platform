namespace Bot.Domain;

public sealed record BotParticipant
{
    private const string BotSuffix = " [BOT]";

    public BotParticipant(string userId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("Bot user ID is required.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("Bot display name is required.", nameof(displayName));
        }

        UserId = userId.Trim();
        DisplayName = displayName.EndsWith(BotSuffix, StringComparison.Ordinal)
            ? displayName.Trim()
            : $"{displayName.Trim()}{BotSuffix}";
    }

    public string UserId { get; }

    public string DisplayName { get; }
}

public sealed class BotFundingPolicy(decimal targetBalance)
{
    public decimal TargetBalance { get; } = targetBalance > 0
        ? targetBalance
        : throw new ArgumentOutOfRangeException(nameof(targetBalance));

    public decimal CalculateTopUp(decimal currentBalance)
    {
        if (currentBalance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(currentBalance));
        }

        return Math.Max(0, TargetBalance - currentBalance);
    }
}
