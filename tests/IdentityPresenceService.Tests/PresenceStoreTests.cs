using Xunit;

public sealed class PresenceStoreTests
{
    [Fact]
    public void Connecting_anonymous_user_emits_online_presence_snapshot()
    {
        var store = new global::PresenceStore();

        var result = store.ConnectAnonymous("client-a", "connection-a");

        Assert.True(result.BecameOnline);
        Assert.Contains(result.ActiveUsers, user => user.UserId == result.Session.UserId && user.IsOnline);
        Assert.Contains(result.ActiveUsers, user => user.IsBot && user.IsOnline);
    }

    [Fact]
    public void Multiple_connections_keep_user_online_until_last_disconnects()
    {
        var store = new global::PresenceStore();

        var first = store.ConnectAnonymous("client-a", "connection-a");
        var second = store.ConnectAnonymous("client-a", "connection-b");

        Assert.False(second.BecameOnline);

        var afterFirstLeave = store.Leave(first.Session.UserId, "connection-a");

        Assert.NotNull(afterFirstLeave);
        Assert.False(afterFirstLeave.BecameOffline);
        Assert.Contains(afterFirstLeave.ActiveUsers, user => user.UserId == first.Session.UserId);

        var afterLastDisconnect = store.Disconnect("connection-b");

        Assert.NotNull(afterLastDisconnect);
        Assert.True(afterLastDisconnect.BecameOffline);
        Assert.DoesNotContain(afterLastDisconnect.ActiveUsers, user => user.UserId == first.Session.UserId);
    }

    [Fact]
    public void Bots_remain_online_after_human_user_disconnects()
    {
        var store = new global::PresenceStore();
        var result = store.ConnectAnonymous("client-a", "connection-a");

        store.Leave(result.Session.UserId, "connection-a");

        var activeUsers = store.GetActiveUsers();
        Assert.DoesNotContain(activeUsers, user => user.UserId == result.Session.UserId);
        Assert.Contains(activeUsers, user => user.IsBot && user.IsOnline);
    }
}
