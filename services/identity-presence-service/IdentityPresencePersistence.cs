using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using RealtimePix.Contracts;
using RealtimePix.Eventing;

public sealed class IdentityPresenceDbContext(DbContextOptions<IdentityPresenceDbContext> options) : DbContext(options)
{
    public DbSet<PresenceUserEntity> Users => Set<PresenceUserEntity>();

    public DbSet<PresenceConnectionEntity> Connections => Set<PresenceConnectionEntity>();

    public DbSet<AnonymousSessionEntity> Sessions => Set<AnonymousSessionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PresenceUserEntity>(entity =>
        {
            entity.ToTable("presence_users");
            entity.HasKey(item => item.UserId);
            entity.Property(item => item.UserId).HasMaxLength(160);
            entity.Property(item => item.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(item => item.ClientId).HasMaxLength(160);
            entity.HasIndex(item => item.ClientId).IsUnique().HasFilter("\"ClientId\" IS NOT NULL");
        });

        modelBuilder.Entity<PresenceConnectionEntity>(entity =>
        {
            entity.ToTable("presence_connections");
            entity.HasKey(item => item.ConnectionId);
            entity.Property(item => item.ConnectionId).HasMaxLength(160);
            entity.Property(item => item.UserId).HasMaxLength(160).IsRequired();
            entity.HasIndex(item => item.UserId);
        });

        modelBuilder.Entity<AnonymousSessionEntity>(entity =>
        {
            entity.ToTable("anonymous_sessions");
            entity.HasKey(item => item.SessionToken);
            entity.Property(item => item.SessionToken).HasMaxLength(64);
            entity.Property(item => item.ClientId).HasMaxLength(160).IsRequired();
            entity.Property(item => item.UserId).HasMaxLength(160).IsRequired();
            entity.HasIndex(item => item.UserId);
        });

        EfCoreEventingModel.Configure(modelBuilder);
    }
}

public sealed class PresenceUserEntity
{
    public string UserId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? ClientId { get; set; }

    public bool IsBot { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}

public sealed class PresenceConnectionEntity
{
    public string ConnectionId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset ConnectedAt { get; set; }
}

public sealed class AnonymousSessionEntity
{
    public string SessionToken { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class EfPresenceStore(IdentityPresenceDbContext dbContext) : IPresenceStore
{
    public async Task<AnonymousSessionResponse> JoinAnonymousAsync(string? clientId, CancellationToken cancellationToken)
    {
        var normalizedClientId = NormalizeClientId(clientId);
        var user = await UpsertAnonymousUserAsync(normalizedClientId, cancellationToken);
        var session = CreateSession(user, normalizedClientId);
        dbContext.Sessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToSessionResponse(session, user);
    }

    public async Task<PresenceJoinResult> ConnectAnonymousAsync(string? clientId, string connectionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionId);

        await EnsureBotsAsync(cancellationToken);
        var normalizedClientId = NormalizeClientId(clientId);
        var userId = $"user-{normalizedClientId}";
        var wasNewUser = !await dbContext.Users.AnyAsync(item => item.UserId == userId, cancellationToken);
        var wasOnline = await IsOnlineAsync(userId, cancellationToken);
        var user = await UpsertAnonymousUserAsync(normalizedClientId, cancellationToken);

        var existingConnection = await dbContext.Connections.FindAsync([connectionId], cancellationToken);
        if (existingConnection is null)
        {
            dbContext.Connections.Add(new PresenceConnectionEntity
            {
                ConnectionId = connectionId,
                UserId = user.UserId,
                ConnectedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            existingConnection.UserId = user.UserId;
        }

        var session = CreateSession(user, normalizedClientId);
        dbContext.Sessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        var response = await ToResponseAsync(user, cancellationToken);
        return new PresenceJoinResult(
            ToSessionResponse(session, user),
            response,
            wasNewUser,
            !wasOnline && response.IsOnline,
            await GetActiveUsersAsync(cancellationToken));
    }

    public async Task<PresenceUserResponse?> HeartbeatAsync(string userId, CancellationToken cancellationToken)
    {
        await EnsureBotsAsync(cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.LastSeenAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ToResponseAsync(user, cancellationToken);
    }

    public async Task<PresenceLeaveResult?> LeaveAsync(string userId, string? connectionId, CancellationToken cancellationToken)
    {
        await EnsureBotsAsync(cancellationToken);
        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var wasOnline = await IsOnlineAsync(user.UserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(connectionId))
        {
            var connections = await dbContext.Connections.Where(item => item.UserId == user.UserId).ToArrayAsync(cancellationToken);
            dbContext.Connections.RemoveRange(connections);
        }
        else
        {
            var connection = await dbContext.Connections.FindAsync([connectionId], cancellationToken);
            if (connection is not null)
            {
                dbContext.Connections.Remove(connection);
            }
        }

        user.LastSeenAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        var response = await ToResponseAsync(user, cancellationToken);
        return new PresenceLeaveResult(response, wasOnline && !response.IsOnline, await GetActiveUsersAsync(cancellationToken));
    }

    public async Task<PresenceLeaveResult?> DisconnectAsync(string connectionId, CancellationToken cancellationToken)
    {
        var connection = await dbContext.Connections.FindAsync([connectionId], cancellationToken);
        return connection is null
            ? null
            : await LeaveAsync(connection.UserId, connectionId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<PresenceUserResponse>> GetActiveUsersAsync(CancellationToken cancellationToken)
    {
        await EnsureBotsAsync(cancellationToken);
        var users = await dbContext.Users.AsNoTracking().ToArrayAsync(cancellationToken);
        var connectionCounts = await dbContext.Connections
            .GroupBy(item => item.UserId)
            .Select(group => new { UserId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.UserId, item => item.Count, StringComparer.OrdinalIgnoreCase, cancellationToken);

        return users
            .Select(user => new PresenceUserResponse(user.UserId, user.DisplayName, user.IsBot, user.IsBot || connectionCounts.ContainsKey(user.UserId), user.LastSeenAt))
            .Where(user => user.IsOnline)
            .OrderByDescending(user => user.IsBot)
            .ThenBy(user => user.DisplayName)
            .ToArray();
    }

    private async Task<PresenceUserEntity> UpsertAnonymousUserAsync(string normalizedClientId, CancellationToken cancellationToken)
    {
        var userId = $"user-{normalizedClientId}";
        var now = DateTimeOffset.UtcNow;
        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        if (user is null)
        {
            user = new PresenceUserEntity
            {
                UserId = userId,
                ClientId = normalizedClientId,
                DisplayName = PresenceStore.CreateDisplayName(normalizedClientId),
                IsBot = false,
                LastSeenAt = now
            };
            dbContext.Users.Add(user);
        }
        else
        {
            user.DisplayName = PresenceStore.CreateDisplayName(normalizedClientId);
            user.IsBot = false;
            user.LastSeenAt = now;
        }

        return user;
    }

    private async Task EnsureBotsAsync(CancellationToken cancellationToken)
    {
        foreach (var bot in KnownBotUsers.All)
        {
            var user = await dbContext.Users.SingleOrDefaultAsync(item => item.UserId == bot.UserId, cancellationToken);
            if (user is null)
            {
                dbContext.Users.Add(new PresenceUserEntity
                {
                    UserId = bot.UserId,
                    DisplayName = bot.DisplayName,
                    IsBot = true,
                    LastSeenAt = DateTimeOffset.UtcNow
                });
            }
            else
            {
                user.DisplayName = bot.DisplayName;
                user.IsBot = true;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> IsOnlineAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(item => item.UserId == userId, cancellationToken);
        return user?.IsBot == true || await dbContext.Connections.AnyAsync(item => item.UserId == userId, cancellationToken);
    }

    private async Task<PresenceUserResponse> ToResponseAsync(PresenceUserEntity user, CancellationToken cancellationToken)
    {
        return new PresenceUserResponse(user.UserId, user.DisplayName, user.IsBot, await IsOnlineAsync(user.UserId, cancellationToken), user.LastSeenAt);
    }

    private static string NormalizeClientId(string? clientId)
    {
        return string.IsNullOrWhiteSpace(clientId) ? Guid.NewGuid().ToString("N") : clientId.Trim();
    }

    private static AnonymousSessionEntity CreateSession(PresenceUserEntity user, string normalizedClientId)
    {
        return new AnonymousSessionEntity
        {
            SessionToken = Guid.NewGuid().ToString("N"),
            ClientId = normalizedClientId,
            UserId = user.UserId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static AnonymousSessionResponse ToSessionResponse(AnonymousSessionEntity session, PresenceUserEntity user)
    {
        return new AnonymousSessionResponse(session.ClientId, user.UserId, user.DisplayName, session.SessionToken, user.LastSeenAt);
    }
}

public sealed class IdentityPresenceDbContextFactory : IDesignTimeDbContextFactory<IdentityPresenceDbContext>
{
    public IdentityPresenceDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IdentityPresenceDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("IDENTITY_PRESENCE_MIGRATIONS_CONNECTION")
                ?? "Host=localhost;Database=identity_presence_db;Username=postgres;Password=${POSTGRES_PASSWORD}")
            .Options;

        return new IdentityPresenceDbContext(options);
    }
}

