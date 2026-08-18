using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Data.Commands;

/// <summary>
/// Runs a screen's OnEnter. Queued as the continuation of its art, so entering means the
/// screen is fully shown rather than merely selected. Carries the switch's generation so a
/// stale entry — one whose session has since switched again — can be told apart from the
/// one that still applies, which a name alone cannot do when two switches share a target.
/// </summary>
public sealed record ScreenEnteredCommand(TelnetSession Session, string ScreenName, long Generation) : GameLoopCommand;
