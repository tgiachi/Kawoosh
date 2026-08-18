using Kawoosh.Server.Exceptions;
using Kawoosh.Server.Services;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Integration.Server.Services;

/// <summary>
/// Filesystem-backed tests for screen loading, required screens and hot reload.
/// </summary>
public class ScreenServiceTests
{
    private static readonly string[] NothingRequired = [];

    private TempScreenDirectory _directory = null!;
    private VariableService _variables = null!;
    private ScreenService _screens = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = new TempScreenDirectory();
        _variables = new VariableService();
        _screens = new ScreenService(_variables);
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Dispose();
    }

    [Test]
    public void Load_ADirectoryOfScreens_ReadsEachOneUnderItsFileName()
    {
        _directory.Write("greeting.sgs", "@author Squid", "---", "BENVENUTO");
        _directory.Write("motd.sgs", "Notizie del giorno");

        _screens.Load(_directory.RootPath, NothingRequired);

        Assert.Multiple(() =>
        {
            Assert.That(_screens.ScreenNames, Is.EquivalentTo(new[] { "greeting", "motd" }));
            Assert.That(_screens.Render("greeting"), Is.EqualTo("BENVENUTO"));
        });
    }

    [Test]
    public void Load_AScreenWithNoMetadata_KeepsALeadingDividerAsBody()
    {
        // No @ block means no separator: the --- is art. This is the rule the format exists
        // for, and it is easy to break from the service side by "helpfully" stripping it.
        _directory.Write("greeting.sgs", "---------------", "BENVENUTO", "---------------");

        _screens.Load(_directory.RootPath, NothingRequired);

        Assert.That(_screens.Render("greeting"), Is.EqualTo("---------------\nBENVENUTO\n---------------"));
    }

    [Test]
    public void Load_AFileNameWithMixedCase_IsKeyedInLowercase()
    {
        _directory.Write("Greeting.SGS", "BENVENUTO");

        _screens.Load(_directory.RootPath, NothingRequired);

        Assert.That(_screens.Render("greeting"), Is.EqualTo("BENVENUTO"));
    }

    [Test]
    public void Load_FilesWithOtherExtensions_AreIgnored()
    {
        _directory.Write("greeting.sgs", "BENVENUTO");
        _directory.Write("notes.txt", "non e una schermata");

        _screens.Load(_directory.RootPath, NothingRequired);

        Assert.That(_screens.ScreenNames, Is.EquivalentTo(new[] { "greeting" }));
    }

    [Test]
    public void Load_AMissingRequiredScreen_Throws()
    {
        _directory.Write("motd.sgs", "Notizie");

        var exception = Assert.Throws<ScreenLoadException>(
            () => _screens.Load(_directory.RootPath, ["greeting"])
        );

        Assert.That(exception!.Problems.Single(), Does.Contain("greeting"));
    }

    [Test]
    public void Load_AMalformedScreen_ThrowsWithTheFileAndLine()
    {
        _directory.Write("greeting.sgs", "@author Squid", "spazzatura", "---", "BENVENUTO");

        var exception = Assert.Throws<ScreenLoadException>(
            () => _screens.Load(_directory.RootPath, NothingRequired)
        );

        Assert.That(exception!.Problems.Single(), Does.StartWith("greeting.sgs:2: "));
    }

    [Test]
    public void Load_SeveralMalformedScreens_ReportsThemAll()
    {
        _directory.Write("a.sgs", "@author Squid");
        _directory.Write("b.sgs", "@author Squid", "---");

        var exception = Assert.Throws<ScreenLoadException>(
            () => _screens.Load(_directory.RootPath, NothingRequired)
        );

        Assert.That(exception!.Problems, Has.Count.EqualTo(2));
    }

    [Test]
    public void Load_AMissingDirectory_Throws()
    {
        var missing = Path.Combine(_directory.RootPath, "nope");

        Assert.Throws<DirectoryNotFoundException>(() => _screens.Load(missing, NothingRequired));
    }

    [Test]
    public void Render_AScreenWithVariables_SubstitutesThemAtRenderTime()
    {
        _directory.Write("greeting.sgs", "Ciao {playerName}");
        _screens.Load(_directory.RootPath, NothingRequired);

        _variables.AddVariable("playerName", "Thorin");
        var first = _screens.Render("greeting");

        _variables.AddVariable("playerName", "Bilbo");
        var second = _screens.Render("greeting");

        // Substituting at load would have frozen the first player's name for everyone.
        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo("Ciao Thorin"));
            Assert.That(second, Is.EqualTo("Ciao Bilbo"));
        });
    }

    [Test]
    public void Render_AnUnknownScreen_Throws()
    {
        _screens.Load(_directory.RootPath, NothingRequired);

        Assert.Throws<KeyNotFoundException>(() => _screens.Render("nope"));
    }

    [Test]
    public void TryGetScreen_AKnownScreen_ExposesItsMetadata()
    {
        _directory.Write("greeting.sgs", "@author Squid", "---", "BENVENUTO");
        _screens.Load(_directory.RootPath, NothingRequired);

        var found = _screens.TryGetScreen("greeting", out var screen);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(screen!.Metadata["author"], Is.EqualTo("Squid"));
        });
    }

    [Test]
    public void Reload_AfterAnEdit_ServesTheNewText()
    {
        _directory.Write("greeting.sgs", "vecchio");
        _screens.Load(_directory.RootPath, NothingRequired);

        _directory.Write("greeting.sgs", "nuovo");
        _screens.Reload();

        Assert.That(_screens.Render("greeting"), Is.EqualTo("nuovo"));
    }

    [Test]
    public void Reload_WhenTheNewFilesAreBroken_KeepsTheOnesAlreadyServing()
    {
        _directory.Write("greeting.sgs", "buono");
        _screens.Load(_directory.RootPath, NothingRequired);

        _directory.Write("greeting.sgs", "@author Squid", "spazzatura", "---", "rotto");

        Assert.Multiple(() =>
        {
            Assert.That(_screens.Reload, Throws.InstanceOf<ScreenLoadException>());

            // A typo saved on a live server must not blank every screen.
            Assert.That(_screens.Render("greeting"), Is.EqualTo("buono"));
        });
    }

    [Test]
    public void Reload_BeforeAnyLoad_Throws()
    {
        Assert.Throws<InvalidOperationException>(_screens.Reload);
    }
}
