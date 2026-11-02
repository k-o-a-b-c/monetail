namespace Monetail.Windows.Client.Transport;

public sealed record AuthenticatedAccount(Guid AuthUserId, string DisplayName,
    DateTimeOffset ExpiresAtUtc, DateTimeOffset VerifiedAtUtc);
