using IdentityPresence.Domain;
using IdentityPresence.Infrastructure;
using RealtimePix.Contracts;
using Xunit;

public sealed class PresenceStoreTests
{
    private sealed class DemoClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task Abandoned_connections_expire_without_deleting_user_or_bots()
    {
        var clock = new DemoClock();
        var store = new InMemoryPresenceStore(clock);
        var joined = await store.ConnectAnonymousAsync("lease-client", "http:lease-client:tab", CancellationToken.None);
        clock.Now = clock.Now.AddMinutes(3);
        var active = await store.GetActiveUsersAsync(CancellationToken.None);
        Assert.DoesNotContain(active, item => item.UserId == joined.Session.UserId);
        Assert.Contains(active, item => item.IsBot);
        var renewed = await store.HeartbeatAsync(joined.Session.UserId, CancellationToken.None, "http:lease-client:tab");
        Assert.True(renewed!.IsOnline);
    }

    [Fact]
    public async Task Heartbeat_renews_only_its_tab_and_other_tabs_can_close_independently()
    {
        var clock = new DemoClock();
        var store = new InMemoryPresenceStore(clock);
        var joined = await store.ConnectAnonymousAsync("lease-client", "tab-a", CancellationToken.None);
        await store.ConnectAnonymousAsync("lease-client", "tab-b", CancellationToken.None);
        clock.Now = clock.Now.AddSeconds(100);
        await store.HeartbeatAsync(joined.Session.UserId, CancellationToken.None, "tab-b");
        clock.Now = clock.Now.AddSeconds(30);
        var left = await store.LeaveAsync(joined.Session.UserId, "tab-a", CancellationToken.None);
        Assert.False(left!.BecameOffline);
        Assert.Single(left.ActiveUsers.Where(item => !item.IsBot));
        Assert.Null(await store.HeartbeatAsync(joined.Session.UserId, CancellationToken.None, "unknown-tab"));
    }

    [Fact]
    public async Task Connecting_anonymous_user_emits_online_presence_snapshot()
    {
        var store = new InMemoryPresenceStore();

        var result = await store.ConnectAnonymousAsync("client-a", "connection-a", CancellationToken.None);

        Assert.True(result.BecameOnline);
        Assert.Contains(result.ActiveUsers, user => user.UserId == result.Session.UserId && user.IsOnline);
        Assert.Contains(result.ActiveUsers, user => user.IsBot && user.IsOnline);
    }

    [Fact]
    public async Task Multiple_connections_keep_user_online_until_last_disconnects()
    {
        var store = new InMemoryPresenceStore();

        var first = await store.ConnectAnonymousAsync("client-a", "connection-a", CancellationToken.None);
        var second = await store.ConnectAnonymousAsync("client-a", "connection-b", CancellationToken.None);

        Assert.False(second.BecameOnline);

        var afterFirstLeave = await store.LeaveAsync(first.Session.UserId, "connection-a", CancellationToken.None);

        Assert.NotNull(afterFirstLeave);
        Assert.False(afterFirstLeave.BecameOffline);
        Assert.Contains(afterFirstLeave.ActiveUsers, user => user.UserId == first.Session.UserId);

        var afterLastDisconnect = await store.DisconnectAsync("connection-b", CancellationToken.None);

        Assert.NotNull(afterLastDisconnect);
        Assert.True(afterLastDisconnect.BecameOffline);
        Assert.DoesNotContain(afterLastDisconnect.ActiveUsers, user => user.UserId == first.Session.UserId);
    }

    [Fact]
    public async Task Bots_remain_online_after_human_user_disconnects()
    {
        var store = new InMemoryPresenceStore();
        var result = await store.ConnectAnonymousAsync("client-a", "connection-a", CancellationToken.None);

        await store.LeaveAsync(result.Session.UserId, "connection-a", CancellationToken.None);

        var activeUsers = await store.GetActiveUsersAsync(CancellationToken.None);
        Assert.DoesNotContain(activeUsers, user => user.UserId == result.Session.UserId);
        Assert.Contains(activeUsers, user => user.IsBot && user.IsOnline);
    }

    [Fact]
    public void Every_known_bot_name_has_the_required_suffix()
    {
        Assert.NotEmpty(KnownBotUsers.All);
        Assert.All(KnownBotUsers.All, bot => Assert.EndsWith(" [BOT]", bot.DisplayName));
    }

    [Fact]
    public void Anonymous_name_catalog_has_thirty_unique_players_from_each_sport()
    {
        Assert.Equal(30, AnonymousDisplayNames.FootballWorldCup2026.Count);
        Assert.Equal(30, AnonymousDisplayNames.Nfl.Count);
        Assert.Equal(30, AnonymousDisplayNames.Nba.Count);
        Assert.Equal(90, AnonymousDisplayNames.AthleteNames.Count);
        Assert.Equal(
            90,
            AnonymousDisplayNames.AthleteNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Anonymous_name_is_stable_and_uses_an_athlete_prefix()
    {
        var first = AnonymousDisplayNames.Create("stable-client");
        var second = AnonymousDisplayNames.Create("stable-client");

        Assert.Equal(first, second);
        Assert.Contains(
            AnonymousDisplayNames.AthleteNames,
            athlete => first.StartsWith($"{athlete} ", StringComparison.Ordinal));
        Assert.Contains(
            AnonymousDisplayNames.Suffixes,
            suffix => first.EndsWith($" {suffix}", StringComparison.Ordinal));
    }
}
