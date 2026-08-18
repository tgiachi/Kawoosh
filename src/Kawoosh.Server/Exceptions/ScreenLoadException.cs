namespace Kawoosh.Server.Exceptions;

/// <summary>
/// Thrown when a screen directory cannot be served: a file is malformed, or a screen the
/// caller declared required is absent.
/// </summary>
public sealed class ScreenLoadException : Exception
{
    public IReadOnlyList<string> Problems { get; }

    public ScreenLoadException(IReadOnlyList<string> problems)
        : base(BuildMessage(problems))
    {
        Problems = problems;
    }

    private static string BuildMessage(IReadOnlyList<string> problems)
    {
        return string.Join(Environment.NewLine, problems);
    }
}
