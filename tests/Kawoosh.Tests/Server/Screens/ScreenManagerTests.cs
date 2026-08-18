using Kawoosh.Server.Data.Screens;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Services;

namespace Kawoosh.Tests.Server.Screens;

/// <summary>
/// Unit tests for screen resolution. No session and no loop: the manager only answers which
/// screen a name means.
/// </summary>
public class ScreenManagerTests
{
    [Test]
    public void TryGetScreen_ARegisteredName_ReturnsIt()
    {
        var manager = new ScreenManager([new StubScreen("world")]);

        var found = manager.TryGetScreen("world", out var screen);

        Assert.Multiple(
            () =>
            {
                Assert.That(found, Is.True);
                Assert.That(screen!.Name, Is.EqualTo("world"));
            }
        );
    }

    [Test]
    public void TryGetScreen_ANameInAnotherCase_StillReturnsIt()
    {
        // Screen names come from file names and from code; insisting the two agree on case
        // would be a rule nobody remembers.
        var manager = new ScreenManager([new StubScreen("world")]);

        Assert.That(manager.TryGetScreen("WORLD", out _), Is.True);
    }

    [Test]
    public void TryGetScreen_AnUnknownName_ReturnsFalse()
    {
        var manager = new ScreenManager([new StubScreen("world")]);

        Assert.That(manager.TryGetScreen("nowhere", out _), Is.False);
    }

    [Test]
    public void TryGetScreen_AnEmptyName_ReturnsFalse()
    {
        // A session that has not been switched anywhere yet holds an empty name.
        var manager = new ScreenManager([new StubScreen("world")]);

        Assert.That(manager.TryGetScreen("", out _), Is.False);
    }

    [Test]
    public void ScreenNames_ListsEveryRegisteredScreen()
    {
        var manager = new ScreenManager([new StubScreen("name"), new StubScreen("world")]);

        Assert.That(manager.ScreenNames, Is.EquivalentTo(new[] { "name", "world" }));
    }

    [Test]
    public void Constructor_TwoScreensSharingAName_Throws()
    {
        // At startup, which is when a duplicate should be noticed — not at the first switch,
        // by whichever player happened to trigger it.
        Assert.That(
            () => new ScreenManager([new StubScreen("world"), new StubScreen("world")]),
            Throws.InstanceOf<ArgumentException>()
        );
    }

    private sealed class StubScreen : IScreen
    {
        public string Name { get; }

        public StubScreen(string name)
        {
            Name = name;
        }

        public void OnEnter(ScreenContext context)
        {
        }

        public void OnInput(ScreenContext context, string line)
        {
        }

        public void OnExit(ScreenContext context)
        {
        }
    }
}
