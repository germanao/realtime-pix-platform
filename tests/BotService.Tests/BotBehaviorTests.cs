using Bot.Domain;
using Xunit;

namespace BotService.Tests;

public sealed class BotBehaviorTests
{
    [Fact]
    public void Bot_name_always_has_the_public_suffix()
    {
        var bot = new BotParticipant("bot-a", "Ada Ledger");

        Assert.Equal("Ada Ledger [BOT]", bot.DisplayName);
    }

    [Theory]
    [InlineData(0, 1000)]
    [InlineData(250, 750)]
    [InlineData(1000, 0)]
    [InlineData(1250, 0)]
    public void Funding_policy_only_tops_up_to_target(decimal current, decimal expected)
    {
        var policy = new BotFundingPolicy(1_000m);

        Assert.Equal(expected, policy.CalculateTopUp(current));
    }
}
