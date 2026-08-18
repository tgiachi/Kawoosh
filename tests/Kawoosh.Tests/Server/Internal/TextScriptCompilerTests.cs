using Kawoosh.Server.Data.Text;
using Kawoosh.Server.Internal;

namespace Kawoosh.Tests.Server.Internal;

/// <summary>
/// Unit tests for the text effects compiler. Pure: no session, no clock.
/// </summary>
public class TextScriptCompilerTests
{
    private static string Text(params string[] lines)
    {
        return string.Join("\n", lines);
    }

    [Test]
    public void Compile_PlainText_IsOneInstantStep()
    {
        var script = TextScriptCompiler.Compile(Text("first", "second"));

        Assert.Multiple(
            () =>
            {
                Assert.That(script.Steps, Has.Count.EqualTo(1));
                Assert.That(script.Steps[0].Text, Is.EqualTo("first\nsecond"));
                Assert.That(script.Steps[0].DelayMilliseconds, Is.Zero);
                Assert.That(script.IsInstant, Is.True);
            }
        );
    }

    [Test]
    public void Compile_ADelayDirective_SplitsTheTextAroundIt()
    {
        var script = TextScriptCompiler.Compile(Text("before", "@delay 500", "after"));

        Assert.Multiple(
            () =>
            {
                Assert.That(script.Steps, Has.Count.EqualTo(2));
                Assert.That(script.Steps[0].Text, Is.EqualTo("before"));
                Assert.That(script.Steps[1].DelayMilliseconds, Is.EqualTo(500));
                Assert.That(script.Steps[1].Text, Is.EqualTo("after"));
                Assert.That(script.IsInstant, Is.False);
            }
        );
    }

    [Test]
    public void Compile_TwoDelaysInARow_AddUp()
    {
        var script = TextScriptCompiler.Compile(Text("a", "@delay 200", "@delay 300", "b"));

        Assert.Multiple(
            () =>
            {
                Assert.That(script.Steps, Has.Count.EqualTo(2));
                Assert.That(script.Steps[1].DelayMilliseconds, Is.EqualTo(500));
            }
        );
    }

    [Test]
    public void Compile_ADelayBeforeAnyText_DelaysTheFirstStep()
    {
        var script = TextScriptCompiler.Compile(Text("@delay 400", "hello"));

        Assert.Multiple(
            () =>
            {
                Assert.That(script.Steps, Has.Count.EqualTo(1));
                Assert.That(script.Steps[0].DelayMilliseconds, Is.EqualTo(400));
            }
        );
    }

    [Test]
    public void Compile_ATrailingDelay_ProducesNoEmptyStep()
    {
        var script = TextScriptCompiler.Compile(Text("hello", "@delay 400"));

        Assert.That(script.Steps, Has.Count.EqualTo(1));
    }

    [Test]
    public void Compile_TypewriterText_IsOneStepPerCharacter()
    {
        var script = TextScriptCompiler.Compile(Text("@typewriter 40", "abc"));

        Assert.Multiple(
            () =>
            {
                // Three characters plus the step that ends the line.
                Assert.That(script.Steps, Has.Count.EqualTo(4));
                Assert.That(script.Steps[0], Is.EqualTo(new TextStep(40, "a", false)));
                Assert.That(script.Steps[1], Is.EqualTo(new TextStep(40, "b", false)));
                Assert.That(script.Steps[2], Is.EqualTo(new TextStep(40, "c", false)));
                Assert.That(script.Steps[3], Is.EqualTo(new TextStep(0, "", true)));
            }
        );
    }

    [Test]
    public void Compile_TypewriterZero_TurnsItOffAgain()
    {
        var script = TextScriptCompiler.Compile(Text("@typewriter 40", "ab", "@typewriter 0", "plain"));

        Assert.Multiple(
            () =>
            {
                // a, b, end of line, then "plain" whole.
                Assert.That(script.Steps, Has.Count.EqualTo(4));
                Assert.That(script.Steps[3].Text, Is.EqualTo("plain"));
            }
        );
    }

    [Test]
    public void Compile_ADelayBeforeTypewriterText_AppliesToTheFirstCharacterOnly()
    {
        var script = TextScriptCompiler.Compile(Text("@typewriter 30", "@delay 500", "ab"));

        Assert.Multiple(
            () =>
            {
                Assert.That(script.Steps[0].DelayMilliseconds, Is.EqualTo(530));
                Assert.That(script.Steps[1].DelayMilliseconds, Is.EqualTo(30));
            }
        );
    }

    [Test]
    public void Compile_ADirectiveThatDoesNotParse_StaysAsText()
    {
        // Permissive on purpose, and it doubles as the escape: to show the words, write
        // something that is not a number.
        var script = TextScriptCompiler.Compile(Text("@delay soon"));

        Assert.Multiple(
            () =>
            {
                Assert.That(script.Steps, Has.Count.EqualTo(1));
                Assert.That(script.Steps[0].Text, Is.EqualTo("@delay soon"));
            }
        );
    }

    [Test]
    public void Compile_ANegativeDelay_StaysAsText()
    {
        var script = TextScriptCompiler.Compile(Text("@delay -1"));

        Assert.That(script.Steps[0].Text, Is.EqualTo("@delay -1"));
    }

    [Test]
    public void Compile_AnUnknownDirective_StaysAsText()
    {
        var script = TextScriptCompiler.Compile(Text("@colour red"));

        Assert.That(script.Steps[0].Text, Is.EqualTo("@colour red"));
    }

    [Test]
    public void Compile_ALineIndentedBeforeADirective_IsText()
    {
        // Leading whitespace is how art escapes a directive it happens to contain.
        var script = TextScriptCompiler.Compile(Text(" @delay 500"));

        Assert.That(script.Steps[0].Text, Is.EqualTo(" @delay 500"));
    }

    [Test]
    public void Compile_EmptyText_IsAnEmptyScript()
    {
        var script = TextScriptCompiler.Compile(string.Empty);

        Assert.Multiple(
            () =>
            {
                Assert.That(script.Steps, Is.Empty);
                Assert.That(script.IsInstant, Is.False);
            }
        );
    }

    [Test]
    public void Compile_BlankLinesBetweenText_AreKept()
    {
        var script = TextScriptCompiler.Compile(Text("a", "", "b"));

        Assert.That(script.Steps[0].Text, Is.EqualTo("a\n\nb"));
    }
}
