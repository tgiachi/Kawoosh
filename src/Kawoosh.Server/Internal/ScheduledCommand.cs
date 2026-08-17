namespace Kawoosh.Server.Internal;

/// <summary>
/// An entry with the instant it becomes due, as it travels from the calling thread to the
/// loop. The entry's handle doubles as the tie-breaker between commands due at the same
/// instant, so two lines a player typed in order reach the world in that order.
/// </summary>
internal readonly record struct ScheduledCommand(ScheduledEntry Entry, long DueAtMilliseconds);
