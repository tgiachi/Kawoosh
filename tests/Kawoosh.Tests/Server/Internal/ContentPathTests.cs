using Kawoosh.Server.Internal;

namespace Kawoosh.Tests.Server.Internal;

/// <summary>
/// Tests for content path resolution. The server must find its screens whether it was
/// launched from the repository root, from an IDE, or from the install directory.
/// </summary>
public class ContentPathTests
{
    [Test]
    public void Resolve_ARelativePath_IsAnchoredToTheExecutableNotTheWorkingDirectory()
    {
        var resolved = ContentPath.Resolve("screens");

        Assert.Multiple(() =>
        {
            Assert.That(Path.IsPathRooted(resolved), Is.True);
            Assert.That(resolved, Does.StartWith(AppContext.BaseDirectory));
        });
    }

    [Test]
    public void Resolve_AnAbsolutePath_IsLeftAlone()
    {
        var absolute = Path.Combine(Path.GetTempPath(), "kawoosh-screens");

        Assert.That(ContentPath.Resolve(absolute), Is.EqualTo(absolute));
    }

    [Test]
    public void Resolve_ABlankPath_IsRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => ContentPath.Resolve(""), Throws.ArgumentException);
            Assert.That(() => ContentPath.Resolve("   "), Throws.ArgumentException);
        });
    }
}
