namespace Kawoosh.Tests.Support;

/// <summary>
/// Throwaway directory of .sgs files. Written with LF endings so line numbers in
/// diagnostics are stable across platforms. Removed on dispose.
/// </summary>
public sealed class TempScreenDirectory : IDisposable
{
    public string RootPath { get; }

    public TempScreenDirectory()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"kawoosh-screens-{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    /// <summary>Writes a screen file and returns its full path.</summary>
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
