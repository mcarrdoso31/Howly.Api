using BZ.Howly.Api.Core.Shared;

namespace BZ.Howly.Api.Core.Identity;

/// <summary>
/// Persisted refresh token. Tokens issued from the same login share a <see cref="FamilyId"/>,
/// so reuse of an already rotated token can revoke the whole chain.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Issue(Guid userId, IssuedRefreshToken issued, DateTimeOffset now, Guid? familyId = null) => new()
    {
        Id = Ids.New(),
        UserId = userId,
        FamilyId = familyId ?? Ids.New(),
        TokenHash = issued.Hash,
        CreatedAt = now,
        ExpiresAt = issued.ExpiresAt,
    };

    public bool IsRevoked => RevokedAt is not null;

    public bool IsActive(DateTimeOffset now) => !IsRevoked && now < ExpiresAt;

    public void Revoke(DateTimeOffset now, Guid? replacedByTokenId = null)
    {
        if (IsRevoked)
            return;

        RevokedAt = now;
        ReplacedByTokenId = replacedByTokenId;
    }
}
