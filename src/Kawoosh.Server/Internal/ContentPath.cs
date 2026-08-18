namespace Kawoosh.Server.Internal;

/// <summary>
/// Resolves content paths against the server rather than against whatever directory it was
/// launched from.
/// </summary>
internal static class ContentPath
{
    /// <summary>
    /// Returns an absolute path. A relative one is resolved against the directory holding the
    /// executable, not the current working directory: a deployed server keeps its content
    /// beside itself, and in development an IDE sets the working directory wherever it likes.
    /// </summary>
    public static string Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
    }
}
