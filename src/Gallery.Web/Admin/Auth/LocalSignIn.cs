using System.Security.Cryptography;
using System.Text;
using Gallery.Web.Data;
using Gallery.Web.Maintenance;
using Gallery.Web.Storage;

namespace Gallery.Web.Admin.Auth;

/// <summary>The startup result of the local sign-in rule (018 research R-01, data-model.md).</summary>
public enum LocalSignInState { Enabled, Absent, TooShort, NotDevelopmentHost }

/// <summary>
/// [SAFETY] 018 local sign-in (I.7, research R-01/R-03). Decided once at startup: enabled only on a
/// development host (per <see cref="DevTargetGuard"/>) with a configured password of at least
/// <see cref="MinLength"/> characters. Only the SHA-256 of the configured password is kept; the
/// comparison is constant-time. This class never logs.
/// </summary>
public sealed class LocalSignIn
{
    public const string PasswordKey = "LocalSignIn:Password";
    public const int MinLength = 16;
    public const string LocalOwner = "local-owner";

    private readonly byte[]? _hash;

    public LocalSignIn(IConfiguration configuration, string environmentName, StorageOptions storage, MongoOptions mongo)
    {
        State = Evaluate(configuration, environmentName, storage, mongo);
        if (State == LocalSignInState.Enabled)
            _hash = SHA256.HashData(Encoding.UTF8.GetBytes(configuration[PasswordKey]!));
    }

    public LocalSignInState State { get; }

    public bool Enabled => State == LocalSignInState.Enabled;

    /// <summary>Evaluation order: absent → not a development host → too short → enabled.</summary>
    public static LocalSignInState Evaluate(
        IConfiguration configuration, string environmentName, StorageOptions storage, MongoOptions mongo)
    {
        var password = configuration[PasswordKey];
        if (string.IsNullOrWhiteSpace(password))
            return LocalSignInState.Absent;
        if (DevTargetGuard.Check(environmentName, storage, mongo).Count != 0)
            return LocalSignInState.NotDevelopmentHost;
        if (password.Length < MinLength)
            return LocalSignInState.TooShort;
        return LocalSignInState.Enabled;
    }

    public bool Matches(string? submitted)
    {
        if (!Enabled || _hash is null || string.IsNullOrEmpty(submitted))
            return false;
        var candidate = SHA256.HashData(Encoding.UTF8.GetBytes(submitted));
        return CryptographicOperations.FixedTimeEquals(candidate, _hash);
    }
}
