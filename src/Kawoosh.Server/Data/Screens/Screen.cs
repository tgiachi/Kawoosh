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

    /// <summary>
    /// Whether this screen expects the terminal cleared before it. Set with "@clear true".
    /// A value that is not a boolean leaves it false rather than failing the load: a screen
    /// that does not clear is far less bad than a server that refuses to start over a typo
    /// in a display hint.
    /// </summary>
    public bool ClearsScreen =>
        Metadata.TryGetValue("clear", out var value) && bool.TryParse(value, out var clear) && clear;

    public override string ToString()
        => $"Screen({Name}, {Body.Length} chars, {Metadata.Count} metadata)";
}
