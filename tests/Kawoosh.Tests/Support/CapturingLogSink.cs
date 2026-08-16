using Serilog.Core;
using Serilog.Events;

namespace Kawoosh.Tests.Support;

/// <summary>
/// An in-memory Serilog sink. Lets a test assert on what a component logged when the log line
/// is the only observable side effect of a decision, such as an outbound message being dropped.
/// </summary>
public sealed class CapturingLogSink : ILogEventSink
{
    private readonly Lock _gate = new();
    private readonly List<string> _templates = [];

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            _templates.Add(logEvent.MessageTemplate.Text);
        }
    }

    /// <summary>Counts the events emitted with the given message template.</summary>
    public int Count(string template)
    {
        lock (_gate)
        {
            return _templates.Count(candidate => candidate == template);
        }
    }
}
