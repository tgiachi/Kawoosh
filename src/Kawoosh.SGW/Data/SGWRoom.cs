using Kawoosh.SGW.Types;

namespace Kawoosh.SGW.Data;

public class SGWRoom
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Sector { get; set; } = string.Empty;
    public List<RoomFlag> Flags { get; set; } = [];

    public List<SGWExit> Exits { get; set; } = [];
    public List<SGWExtraDescription> Extras { get; set; } = [];

    public Dictionary<string, string> Scripts { get; set; } = new();

    public void AddScript(string scriptName, string scriptContent)
    {
        Scripts.TryAdd(scriptName, scriptContent);
    }

    public override string ToString()
        => $"Room(Id: {Id}, Name: {Name}, Sector: {Sector}, Flags: [{string.Join(", ", Flags)}])";
}
