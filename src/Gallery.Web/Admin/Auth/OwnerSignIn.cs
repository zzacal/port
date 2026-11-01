using Gallery.Web.Data;

namespace Gallery.Web.Admin.Auth;

public enum SignInOutcome { Claimed, SignedIn, RefusedNotOwner, RefusedLostRace }

/// <summary>
/// Implements the owner sign-in outcome diagram from data-model.md.
/// Scoped per request. T015 fills in ResolveAsync fully.
/// </summary>
public sealed class OwnerSignIn(OwnerStore store, ILoggerFactory loggers)
{
    private readonly ILogger _logger = loggers.CreateLogger("Gallery.Auth");

    public async Task<SignInOutcome> ResolveAsync(string sub, string ip)
    {
        var atUtc = DateTime.UtcNow;

        // Read current owner
        var currentOwner = await store.ReadOwnerAsync();

        if (currentOwner == sub)
            return SignInOutcome.SignedIn;

        if (currentOwner is not null)
        {
            _logger.LogInformation("auth.refused {sub} {ip} {reason}", sub, ip, "not_owner");
            return SignInOutcome.RefusedNotOwner;
        }

        // Null → try to claim
        var claimResult = await store.TryClaimAsync(sub, ip, atUtc);

        if (claimResult == ClaimResult.Won)
        {
            _logger.LogInformation("auth.claim {sub} {ip} {at}", sub, ip, atUtc.ToString("O"));
            return SignInOutcome.Claimed;
        }

        // Lost the race — read to see who won
        var newOwner = await store.ReadOwnerAsync();
        if (newOwner == sub)
            return SignInOutcome.SignedIn;

        _logger.LogInformation("auth.refused {sub} {ip} {reason}", sub, ip, "lost_race");
        return SignInOutcome.RefusedLostRace;
    }
}
