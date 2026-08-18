using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Data.Commands;

/// <summary>Puts a session at its first prompt, once whatever preceded it has finished.</summary>
public sealed record BeginSessionFlowCommand(TelnetSession Session) : GameLoopCommand;
