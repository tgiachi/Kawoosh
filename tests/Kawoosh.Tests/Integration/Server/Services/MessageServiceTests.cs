using Kawoosh.Server.Exceptions;
using Kawoosh.Server.Services;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Integration.Server.Services;

/// <summary>
/// Filesystem-backed tests for message loading, required keys, reload and rendering.
/// </summary>
public class MessageServiceTests
{
    private static readonly string[] NothingRequired = [];

    private TempMessageDirectory _directory = null!;
    private VariableService _variables = null!;
    private MessageService _messages = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = new TempMessageDirectory();
        _variables = new VariableService();
        _messages = new MessageService(_variables);
    }

    [TearDown]
    public void TearDown()
    {
        _directory.Dispose();
    }

    [Test]
    public void Load_ADirectoryOfFiles_ReadsEveryKey()
    {
        _directory.Write("login.sgm", "login.prompt = Password:");
        _directory.Write("combat.sgm", "combat.miss = You miss.");

        _messages.Load(_directory.RootPath, NothingRequired);

        Assert.Multiple(() =>
        {
            Assert.That(_messages.MessageKeys, Is.EquivalentTo(new[] { "login.prompt", "combat.miss" }));
            Assert.That(_messages.Render("login.prompt"), Is.EqualTo("Password:"));
        });
    }

    [Test]
    public void Load_FilesWithOtherExtensions_AreIgnored()
    {
        _directory.Write("login.sgm", "login.prompt = Password:");
        _directory.Write("notes.txt", "not a message file");

        _messages.Load(_directory.RootPath, NothingRequired);

        Assert.That(_messages.MessageKeys, Is.EquivalentTo(new[] { "login.prompt" }));
    }

    [Test]
    public void Load_TheSameKeyInTwoFiles_Throws()
    {
        _directory.Write("a.sgm", "shared.key = from a");
        _directory.Write("b.sgm", "shared.key = from b");

        var exception = Assert.Throws<ContentLoadException>(
            () => _messages.Load(_directory.RootPath, NothingRequired)
        );

        Assert.That(exception!.Problems.Single(), Does.Contain("shared.key"));
    }

    [Test]
    public void Load_AMissingRequiredKey_Throws()
    {
        _directory.Write("login.sgm", "login.prompt = Password:");

        var exception = Assert.Throws<ContentLoadException>(
            () => _messages.Load(_directory.RootPath, ["login.welcome"])
        );

        Assert.That(exception!.Problems.Single(), Does.Contain("login.welcome"));
    }

    [Test]
    public void Load_AMalformedLine_ThrowsWithTheFileAndLine()
    {
        _directory.Write("login.sgm", "login.prompt = Password:", "this line is wrong");

        var exception = Assert.Throws<ContentLoadException>(
            () => _messages.Load(_directory.RootPath, NothingRequired)
        );

        Assert.That(exception!.Problems.Single(), Does.StartWith("login.sgm:2: "));
    }

    [Test]
    public void Load_AMissingDirectory_Throws()
    {
        var missing = Path.Combine(_directory.RootPath, "nope");

        Assert.Throws<DirectoryNotFoundException>(() => _messages.Load(missing, NothingRequired));
    }

    [Test]
    public void Render_AMessageWithAGlobalVariable_SubstitutesItAtRenderTime()
    {
        _directory.Write("login.sgm", "login.welcome = Welcome to {serverName}.");
        _messages.Load(_directory.RootPath, NothingRequired);

        _variables.AddVariable("serverName", "Kawoosh");

        Assert.That(_messages.Render("login.welcome"), Is.EqualTo("Welcome to Kawoosh."));
    }

    [Test]
    public void Render_AMessageWithPerCallArguments_SubstitutesThem()
    {
        _directory.Write("combat.sgm", "combat.hit = {attacker} hits {target}.");
        _messages.Load(_directory.RootPath, NothingRequired);

        var rendered = _messages.Render("combat.hit", ("attacker", "Thorin"), ("target", "an orc"));

        Assert.That(rendered, Is.EqualTo("Thorin hits an orc."));
    }

    [Test]
    public void Render_AnUnknownKey_Throws()
    {
        _messages.Load(_directory.RootPath, NothingRequired);

        Assert.Throws<KeyNotFoundException>(() => _messages.Render("nope"));
    }

    [Test]
    public void TryGetTemplate_AKnownKey_ReturnsTheRawTextWithoutSubstituting()
    {
        _directory.Write("login.sgm", "login.welcome = Welcome to {serverName}.");
        _messages.Load(_directory.RootPath, NothingRequired);

        var found = _messages.TryGetTemplate("login.welcome", out var template);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(template, Is.EqualTo("Welcome to {serverName}."));
        });
    }

    [Test]
    public void Reload_AfterAnEdit_ServesTheNewText()
    {
        _directory.Write("login.sgm", "login.prompt = old");
        _messages.Load(_directory.RootPath, NothingRequired);

        _directory.Write("login.sgm", "login.prompt = new");
        _messages.Reload();

        Assert.That(_messages.Render("login.prompt"), Is.EqualTo("new"));
    }

    [Test]
    public void Reload_WhenTheNewFilesAreBroken_KeepsTheOnesAlreadyServing()
    {
        _directory.Write("login.sgm", "login.prompt = good");
        _messages.Load(_directory.RootPath, NothingRequired);

        _directory.Write("login.sgm", "login.prompt = broken", "this line is wrong");

        Assert.Multiple(() =>
        {
            Assert.That(_messages.Reload, Throws.InstanceOf<ContentLoadException>());

            // A typo saved on a live server must not blank every prompt in the game.
            Assert.That(_messages.Render("login.prompt"), Is.EqualTo("good"));
        });
    }

    [Test]
    public void Reload_BeforeAnyLoad_Throws()
    {
        Assert.Throws<InvalidOperationException>(_messages.Reload);
    }
}
