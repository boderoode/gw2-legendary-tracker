namespace Gw2LegendaryTracker.Core.Gw2;

public enum Gw2ApiErrorKind
{
    InvalidKey,
    MissingPermissions,
    RateLimited,
    Offline,
    ServerError,
    Unexpected,
}

/// <summary>A failed API call. <see cref="Exception.Message"/> is a plain-language Dutch text that is safe to show to the user.</summary>
public sealed class Gw2ApiException : Exception
{
    public Gw2ApiException(Gw2ApiErrorKind kind, string message, Exception? inner = null)
        : base(message, inner) => Kind = kind;

    public Gw2ApiErrorKind Kind { get; }
}
