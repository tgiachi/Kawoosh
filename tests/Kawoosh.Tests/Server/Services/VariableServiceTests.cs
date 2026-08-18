using System.Globalization;
using Kawoosh.Server.Services;

namespace Kawoosh.Tests.Server.Services;

/// <summary>
/// Tests for variable substitution in screens and messages. Pure: no sockets, no game loop.
/// </summary>
public class VariableServiceTests
{
    private VariableService _variables = null!;

    [SetUp]
    public void SetUp()
    {
        _variables = new VariableService();
    }

    [Test]
    public void TranslateText_KnownVariable_IsReplaced()
    {
        _variables.AddVariable("playerName", "Thorin");

        Assert.That(_variables.TranslateText("Benvenuto {playerName}!"), Is.EqualTo("Benvenuto Thorin!"));
    }

    [Test]
    public void TranslateText_SeveralVariables_AreAllReplaced()
    {
        _variables.AddVariable("name", "Thorin");
        _variables.AddVariable("count", 12);

        Assert.That(
            _variables.TranslateText("{name}, ci sono {count} giocatori"),
            Is.EqualTo("Thorin, ci sono 12 giocatori")
        );
    }

    [Test]
    public void TranslateText_TheSameVariableTwice_IsReplacedBothTimes()
    {
        _variables.AddVariable("name", "Thorin");

        Assert.That(_variables.TranslateText("{name} e {name}"), Is.EqualTo("Thorin e Thorin"));
    }

    [Test]
    public void TranslateText_TextWithoutVariables_IsUnchanged()
    {
        const string art = "  /\\_/\\\n ( o.o )\n  > ^ <";

        Assert.That(_variables.TranslateText(art), Is.EqualTo(art));
    }

    [Test]
    public void TranslateText_TokenCaseDiffersFromRegistration_StillResolves()
    {
        _variables.AddVariable("playerName", "Thorin");

        // Text is written by people who are not the ones who registered the variable.
        Assert.That(_variables.TranslateText("{PLAYERNAME}"), Is.EqualTo("Thorin"));
    }

    // ------------------------------------------------------------- builders

    [Test]
    public void TranslateText_ABuilder_IsEvaluatedOnEveryTranslation()
    {
        var calls = 0;
        _variables.AddVariableBuilder("count", () => ++calls);

        Assert.Multiple(
            () =>
            {
                Assert.That(_variables.TranslateText("{count}"), Is.EqualTo("1"));
                Assert.That(_variables.TranslateText("{count}"), Is.EqualTo("2"));
                Assert.That(_variables.TranslateText("{count}"), Is.EqualTo("3"));
            }
        );
    }

    [Test]
    public void AddVariableBuilder_OverAStaticValue_ReplacesIt()
    {
        _variables.AddVariable("where", "static");
        _variables.AddVariableBuilder("where", () => "built");

        Assert.That(_variables.TranslateText("{where}"), Is.EqualTo("built"));
    }

    [Test]
    public void AddVariable_OverABuilder_ReplacesIt()
    {
        _variables.AddVariableBuilder("where", () => "built");
        _variables.AddVariable("where", "static");

        Assert.That(_variables.TranslateText("{where}"), Is.EqualTo("static"));
    }

    [Test]
    public void TranslateText_ABuilderThatThrows_RendersEmptyWithoutPropagating()
    {
        _variables.AddVariableBuilder("boom", () => throw new InvalidOperationException("no"));
        _variables.AddVariable("name", "Thorin");

        // A broken variable must not take down the screen that mentions it.
        Assert.That(_variables.TranslateText("[{boom}] {name}"), Is.EqualTo("[] Thorin"));
    }

    // ------------------------------------------- the two bugs being fixed

    [Test]
    public void TranslateText_AValueThatLooksLikeAToken_IsNotResolvedAgain()
    {
        // The original implementation replaced every occurrence of the matched token in the
        // whole buffer, so a's value became b's template and the output was "XX".
        _variables.AddVariable("a", "{b}");
        _variables.AddVariable("b", "X");

        Assert.That(_variables.TranslateText("{a}{b}"), Is.EqualTo("{b}X"));
    }

    [Test]
    public void TranslateText_APlayerControlledValue_CannotForgeOtherVariables()
    {
        _variables.AddVariable("playerName", "{message}");
        _variables.AddVariable("message", "ciao");

        Assert.That(
            _variables.TranslateText("{playerName} dice: {message}"),
            Is.EqualTo("{message} dice: ciao")
        );
    }

    [Test]
    public void AddVariable_FromManyThreadsWhileTranslating_DoesNotCorruptTheStore()
    {
        _variables.AddVariable("stable", "ok");

        Assert.That(
            () => Parallel.For(
                0,
                2_000,
                i =>
                {
                    _variables.AddVariable($"v{i}", i);
                    _variables.AddVariableBuilder($"b{i}", () => i);
                    _variables.TranslateText("{stable}");
                }
            ),
            Throws.Nothing
        );

        Assert.That(_variables.TranslateText("{stable}"), Is.EqualTo("ok"));
    }

    // ------------------------------------------------------ unknown tokens

    [Test]
    public void TranslateText_AnUnknownVariable_RendersEmpty()
    {
        _variables.AddVariable("known", "here");

        // Leaving "{missing}" on screen shows the player a template artefact; the gap is
        // reported to the log instead.
        Assert.That(_variables.TranslateText("{known}{missing}!"), Is.EqualTo("here!"));
    }

    // ------------------------------------------------------------- escapes

    [Test]
    public void TranslateText_DoubledBraces_RenderOneLiteralBrace()
    {
        Assert.That(_variables.TranslateText("{{ e }}"), Is.EqualTo("{ e }"));
    }

    [Test]
    public void TranslateText_ADoubledBraceAroundAName_IsNotResolved()
    {
        _variables.AddVariable("name", "Thorin");

        Assert.That(_variables.TranslateText("{{name}}"), Is.EqualTo("{name}"));
    }

    [Test]
    public void TranslateText_AnUnmatchedBrace_IsLeftAlone()
    {
        Assert.Multiple(
            () =>
            {
                Assert.That(_variables.TranslateText("{unclosed"), Is.EqualTo("{unclosed"));
                Assert.That(_variables.TranslateText("unopened}"), Is.EqualTo("unopened}"));
            }
        );
    }

    // ------------------------------------------------------------- culture

    [Test]
    public void TranslateText_ADecimalValue_UsesTheInvariantCulture()
    {
        var previous = CultureInfo.CurrentCulture;

        try
        {
            // A server must render the same text wherever it runs.
            CultureInfo.CurrentCulture = new CultureInfo("it-IT");
            _variables.AddVariable("gold", 1.5);

            Assert.That(_variables.TranslateText("{gold}"), Is.EqualTo("1.5"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    // -------------------------------------------------------------- guards

    [Test]
    public void AddVariable_WithABlankName_IsRejected()
    {
        Assert.Multiple(
            () =>
            {
                Assert.That(() => _variables.AddVariable("", 1), Throws.ArgumentException);
                Assert.That(() => _variables.AddVariable("  ", 1), Throws.ArgumentException);
                Assert.That(() => _variables.AddVariable("name", null!), Throws.ArgumentNullException);
            }
        );
    }

    [Test]
    public void TranslateText_WithNullText_IsRejected()
    {
        Assert.That(() => _variables.TranslateText(null!), Throws.ArgumentNullException);
    }

    [Test]
    public void GetVariableNames_ReturnsEveryNameSortedAscending()
    {
        _variables.AddVariable("zulu", 1);
        _variables.AddVariable("alpha", 2);
        _variables.AddVariableBuilder("mike", () => 3);

        Assert.That(_variables.GetVariableNames(), Is.EqualTo(new[] { "alpha", "mike", "zulu" }));
    }
}
