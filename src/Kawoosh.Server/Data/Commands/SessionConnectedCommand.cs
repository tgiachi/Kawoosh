using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Data.Commands;

/// <summary>
/// A client has connected. Handled on the loop so the greeting and the first prompt are
/// ordered against everything else the session is sent.
/// </summary>
public sealed record SessionConnectedCommand(TelnetSession Session) : GameLoopCommand;
