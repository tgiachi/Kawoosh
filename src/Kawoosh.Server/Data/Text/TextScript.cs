namespace Kawoosh.Server.Data.Text;

/// <summary>
/// Text compiled into timed steps. Text with no directives compiles to a single instant step,
/// which is why running everything through the compiler changes nothing for text that does
/// not use it.
/// </summary>
public sealed class TextScript
{
    public IReadOnlyList<TextStep> Steps { get; }

    /// <summary>
    /// True when the whole script can be sent in one write, which lets a caller keep the
    /// ordering guarantees it has today instead of going through the scheduler.
    /// </summary>
    public bool IsInstant => Steps.Count == 1 && Steps[0].DelayMilliseconds == 0;

    public TextScript(IReadOnlyList<TextStep> steps)
    {
        Steps = steps;
    }

    public override string ToString()
        => $"TextScript({Steps.Count} steps)";
}
