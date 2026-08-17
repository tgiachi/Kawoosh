using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Data.Commands;

/// <summary>One line a player typed, waiting to be acted on at the next tick.</summary>
public sealed record PlayerInputCommand(TelnetSession Session, string Text) : GameLoopCommand;
