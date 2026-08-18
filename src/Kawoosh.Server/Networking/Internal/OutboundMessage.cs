namespace Kawoosh.Server.Networking.Internal;

/// <summary>
/// One queued piece of outbound text. A prompt does not terminate its line, so the cursor
/// stays where the player is about to type.
/// </summary>
internal readonly record struct OutboundMessage(string Text, bool Terminate);
