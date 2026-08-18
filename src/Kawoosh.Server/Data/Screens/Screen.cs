namespace Kawoosh.Server.Data.Screens;

/// <summary>
/// One loaded screen. The body is verbatim: variables are substituted when it is rendered,
/// not when it is read, because a value like a player's name differs per session.
/// </summary>
public sealed class Screen
{
    public string Name { get; }
    public string Body { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public string SourceFile { get; }

    public Screen(string name, string body, IReadOnlyDictionary<string, string> metadata, string sourceFile)
    {
        Name = name;
        Body = body;
        Metadata = metadata;
        SourceFile = sourceFile;
    }

    public override string ToString()
        => $"Screen({Name}, {Body.Length} chars, {Metadata.Count} metadata)";
}
