namespace Kawoosh.Server.Data.Commands;

/// <summary>
/// What the loop puts in its own queue to wake the world pulse. It carries nothing: the
/// pulse's times are only knowable when it fires, so a scheduled command holding them would
/// hold a guess. The loop turns this into a <see cref="WorldTickCommand" /> at that moment.
/// </summary>
internal sealed record WorldPulseDue : GameLoopCommand;
