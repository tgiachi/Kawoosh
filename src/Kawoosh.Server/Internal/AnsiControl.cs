namespace Kawoosh.Server.Internal;

/// <summary>
/// The ANSI control sequences the server sends. Named here so no caller writes an escape
/// sequence inline, where a wrong digit is invisible.
/// </summary>
internal static class AnsiControl
{
    /// <summary>
    /// Clears the screen and homes the cursor. The two go together: clearing alone leaves the
    /// cursor where it was, so the next line would appear halfway down an empty page.
    /// </summary>
    public const string ClearScreen = "\u001b[2J\u001b[H";
}
