namespace Kawoosh.Server.Networking.Internal;

/// <summary>
/// The telnet bytes, in one place because the reader strips them and the writer sends them.
/// </summary>
internal static class TelnetProtocol
{
    public const byte SubnegotiationEnd = 240;
    public const byte Subnegotiation = 250;
    public const byte Will = 251;
    public const byte Wont = 252;
    public const byte Do = 253;
    public const byte Dont = 254;
    public const byte InterpretAsCommand = 255;

    /// <summary>Option 1: who echoes what the player types.</summary>
    public const byte Echo = 1;

    /// <summary>
    /// "I will echo" — which makes a client stop echoing locally. The server then sends
    /// nothing back, so what the player types is invisible.
    /// </summary>
    public static byte[] SuppressEcho()
    {
        return [InterpretAsCommand, Will, Echo];
    }

    /// <summary>"I will not echo" — the client goes back to showing what the player types.</summary>
    public static byte[] RestoreEcho()
    {
        return [InterpretAsCommand, Wont, Echo];
    }
}
