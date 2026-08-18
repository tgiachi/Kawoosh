using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Data.Commands;

/// <summary>
/// Moves a session to another screen: the old screen's OnExit, then the new screen's art,
/// then its OnEnter.
/// </summary>
public sealed record SwitchScreenCommand(TelnetSession Session, string ScreenName) : GameLoopCommand;
