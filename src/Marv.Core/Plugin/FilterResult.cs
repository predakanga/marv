namespace Marv.Core.Plugin;

/// <summary>
/// Result of a filter evaluation. Currently carries only an allow/deny flag,
/// but exists as a struct rather than a plain bool to allow future extension
/// (e.g. a logging reason, short-circuit flag) without breaking existing
/// evaluator signatures.
/// </summary>
public readonly struct FilterResult
{
    /// <summary>Whether the handler is allowed to proceed.</summary>
    public bool IsAllowed { get; init; }

    /// <summary>A result that allows the handler to proceed.</summary>
    public static FilterResult Allowed => new() { IsAllowed = true };

    /// <summary>A result that prevents the handler from running.</summary>
    public static FilterResult Denied => new() { IsAllowed = false };
}
