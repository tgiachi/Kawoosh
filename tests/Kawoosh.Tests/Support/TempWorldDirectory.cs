namespace Kawoosh.Tests.Support;

/// <summary>
/// Throwaway directory used by world-loading tests. Files are written with LF endings so
/// diagnostics line numbers are stable across platforms. Removed on dispose.
/// </summary>
public sealed class TempWorldDirectory : IDisposable
{
    public string RootPath { get; }

    public TempWorldDirectory()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"kawoosh-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    /// <summary>Writes a file into the directory and returns its full path.</summary>
    public string Write(string fileName, params string[] lines)
    {
        var fullPath = Path.Combine(RootPath, fileName);
        File.WriteAllText(fullPath, string.Join("\n", lines));

        return fullPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, true);
        }
    }
}
