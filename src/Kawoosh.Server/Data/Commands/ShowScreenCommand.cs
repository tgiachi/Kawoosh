using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Data.Commands;

/// <summary>
/// Sends a named screen to a session. Rendering happens when the loop runs it, so the
/// variables it carries are the ones true at that moment.
/// </summary>
public sealed record ShowScreenCommand(TelnetSession Session, string ScreenName) : GameLoopCommand;
