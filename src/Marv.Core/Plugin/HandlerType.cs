namespace Marv.Core.Plugin;

/// <summary>The kind of handler being invoked.</summary>
public enum HandlerType
{
    /// <summary>A command handler triggered by a prefixed message.</summary>
    Command,

    /// <summary>A regex handler triggered by a pattern match.</summary>
    Regex,

    /// <summary>A typed event handler.</summary>
    Event,

    /// <summary>A raw IRC message handler.</summary>
    RawMessage,

    /// <summary>A periodic interval handler.</summary>
    Interval
}
