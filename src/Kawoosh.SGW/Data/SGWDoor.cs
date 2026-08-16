namespace Kawoosh.SGW.Data;

public class SGWDoor
{
    public string Name { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public int? KeyVnum { get; set; }

    public override string ToString()
        => $"Door(Name: {Name}, Locked: {IsLocked}, Key: {KeyVnum?.ToString() ?? "none"})";
}
