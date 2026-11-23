namespace BookIt.Api.Entities;

public class RefreshToken
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>SHA-256 of the token (hex). The raw token is only ever sent to the client, never stored.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Every token rotated from the same login shares a family. If an already-used token from a family
    /// is presented again, it was likely stolen, so the whole family is revoked.
    /// </summary>
    public Guid FamilyId { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    /// <summary>Set when the token is rotated (used once) or when its family is revoked after reuse.</summary>
    public DateTime? RevokedAtUtc { get; set; }
}
