namespace IdentityPresence.Domain;

public readonly record struct AnonymousIdentity
{
    public AnonymousIdentity(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client ID is required.", nameof(clientId));
        }

        ClientId = clientId.Trim();
    }

    public string ClientId { get; }

    public string UserId => $"user-{ClientId}";
}
