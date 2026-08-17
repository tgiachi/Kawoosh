namespace Kawoosh.Server.Data.Commands;

/// <summary>
/// The world pulse. Reschedules itself from its own handler, so it is an ordinary command in
/// the same queue as everything else rather than a second path into world state.
/// </summary>
public sealed record WorldTickCommand : GameLoopCommand;
