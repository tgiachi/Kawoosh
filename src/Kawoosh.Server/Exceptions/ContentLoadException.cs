namespace Kawoosh.Server.Exceptions;

/// <summary>
/// Thrown when a directory of content cannot be served: a file is malformed, or something the
/// caller declared required is absent. Shared by screens and messages, which fail the same way.
/// </summary>
public sealed class ContentLoadException : Exception
{
    public IReadOnlyList<string> Problems { get; }

    public ContentLoadException(IReadOnlyList<string> problems)
        : base(BuildMessage(problems))
    {
        Problems = problems;
    }

    private static string BuildMessage(IReadOnlyList<string> problems)
    {
        return string.Join(Environment.NewLine, problems);
    }
}
