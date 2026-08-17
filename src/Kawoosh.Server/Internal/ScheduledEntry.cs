using Kawoosh.Server.Data.Commands;

namespace Kawoosh.Server.Internal;

/// <summary>
/// A scheduled command and its cancellation flag. The flag lives on the entry rather than in
/// a set of cancelled handles, so a cancelled command leaves nothing behind once its due time
/// passes and the loop drops it.
/// </summary>
internal sealed class ScheduledEntry
{
    private volatile bool _cancelled;

    public long Handle { get; }
    public GameLoopCommand Command { get; }

    public bool IsCancelled => _cancelled;

    public ScheduledEntry(long handle, GameLoopCommand command)
    {
        Handle = handle;
        Command = command;
    }

    public void Cancel()
    {
        _cancelled = true;
    }
}
