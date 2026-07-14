using IdentityPresence.Application;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

namespace IdentityPresence.Infrastructure;

public sealed class PresenceEventPublisher(IIntegrationEventPublisher publisher) : IPresenceEventPublisher
{
    public Task UserJoinedAsync(AnonymousSessionResponse session, CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            EventTypes.AnonymousUserJoined,
            1,
            IdentityPresenceMetadata.ServiceName,
            new AnonymousUserJoinedPayload(session.UserId, session.DisplayName, session.ClientId, false),
            correlationId: session.UserId,
            cancellationToken: cancellationToken);

    public Task PresenceChangedAsync(PresenceUserResponse user, bool isOnline, CancellationToken cancellationToken) =>
        publisher.PublishAsync(
            EventTypes.UserPresenceChanged,
            1,
            IdentityPresenceMetadata.ServiceName,
            new UserPresenceChangedPayload(user.UserId, user.DisplayName, isOnline, user.IsBot, user.LastSeenAt),
            correlationId: user.UserId,
            cancellationToken: cancellationToken);
}

public static class IdentityPresenceMetadata
{
    public const string ServiceName = "identity-presence-service";
}
