namespace Kawoosh.Tests.Support;

/// <summary>
/// Throwaway directory of .sgm files, written with LF endings so diagnostics line numbers
/// are stable across platforms. Removed on dispose.
/// </summary>
public sealed class TempMessageDirectory : IDisposable
{
    public string RootPath { get; }

    public TempMessageDirectory()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"kawoosh-messages-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    /// <summary>Writes a message file and returns its full path.</summary>
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
