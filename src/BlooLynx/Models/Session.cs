namespace BlooLynx.Models;

/// <summary>
/// Authentication session state tracked per account.
/// </summary>
public class Session
{
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public long TokenExpiresAt { get; set; }
}
