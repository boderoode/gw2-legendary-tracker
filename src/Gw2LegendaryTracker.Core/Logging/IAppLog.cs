namespace Gw2LegendaryTracker.Core.Logging;

/// <summary>
/// Application log. Implementations must never throw and must never write secrets;
/// callers should still not pass API keys, but <see cref="LogRedactor"/> is the safety net.
/// </summary>
public interface IAppLog
{
    void Info(string message);

    void Warning(string message);

    /// <summary>Something went wrong. The exception, when given, is written with its stack trace (redacted).</summary>
    void Failure(string message, Exception? exception = null);
}

/// <summary>Discards everything; used where logging is optional (and in tests).</summary>
public sealed class NullAppLog : IAppLog
{
    public static NullAppLog Instance { get; } = new();

    public void Info(string message)
    {
    }

    public void Warning(string message)
    {
    }

    public void Failure(string message, Exception? exception = null)
    {
    }
}
