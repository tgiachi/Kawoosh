namespace Kawoosh.SGW.Data.World;

public class SGWDoor
{
    public string Name { get; set; }
    public bool IsLocked { get; set; }
    public int? KeyVnum { get; set; }

    public override string ToString()
        => $"Door(Name: {Name}, Locked: {IsLocked}, Key: {KeyVnum?.ToString() ?? "none"})";
}
