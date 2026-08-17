using Kawoosh.Server.Data.Commands;

namespace Kawoosh.Server.Internal;

/// <summary>
/// A command with the instant it becomes due, as it travels from the calling thread to the
/// loop. <see cref="Sequence" /> breaks ties between commands due at the same instant, so two
/// lines a player typed in order reach the world in that order.
/// </summary>
internal readonly record struct ScheduledCommand(GameLoopCommand Command, long DueAtMilliseconds, long Sequence);
