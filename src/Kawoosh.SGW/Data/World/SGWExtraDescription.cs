namespace Kawoosh.SGW.Data.World;

public class SGWExtraDescription
{
    public List<string> Keywords { get; set; } = [];
    public string Text { get; set; } = string.Empty;

    public override string ToString()
        => $"Extra([{string.Join(", ", Keywords)}], {Text.Length} chars)";
}
