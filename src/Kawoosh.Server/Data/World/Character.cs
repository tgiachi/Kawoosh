namespace Kawoosh.Server.Data.World;

/// <summary>
/// Placeholder for the logged-in character of a session. The real model belongs to the
/// game layer; this carries only what the telnet layer needs to hand it around.
/// </summary>
public class Character
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;

    public override string ToString()
        => $"Character(Id: {Id}, Name: {Name})";
}
