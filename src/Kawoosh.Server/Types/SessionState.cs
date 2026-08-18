namespace Kawoosh.Server.Types;

/// <summary>
/// Where a session is in the conversation. The same typed line means different things in
/// each: at the name prompt "north" is a character name, in the world it is a direction.
/// </summary>
public enum SessionState : byte
{
    AwaitingName = 0,
    AwaitingPassword,
    Playing
}
