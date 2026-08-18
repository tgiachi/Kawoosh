using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Data.Commands;

/// <summary>
/// Runs a screen's OnEnter. Queued as the continuation of its art, so entering means the
/// screen is fully shown rather than merely selected.
/// </summary>
public sealed record ScreenEnteredCommand(TelnetSession Session, string ScreenName) : GameLoopCommand;
