using Kawoosh.Server.Internal;

namespace Kawoosh.Tests.Server.Internal;

/// <summary>
/// Unit tests for the .sgm parser. Pure: no filesystem.
/// </summary>
public class MessageParserTests
{
    private Dictionary<string, string> _messages = null!;
    private List<string> _problems = null!;

    [SetUp]
    public void SetUp()
    {
        _messages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _problems = [];
    }

    private void Parse(params string[] lines)
    {
        MessageParser.Parse(string.Join("\n", lines), "login.sgm", _messages, _problems);
    }

    [Test]
    public void Parse_AKeyAndValue_IsStored()
    {
        Parse("login.name-prompt = By what name are you known?");

        Assert.Multiple(() =>
        {
            Assert.That(_messages["login.name-prompt"], Is.EqualTo("By what name are you known?"));
            Assert.That(_problems, Is.Empty);
        });
    }

    [Test]
    public void Parse_SeveralLines_AreAllStored()
    {
        Parse("a.one = first", "b.two = second", "c.three = third");

        Assert.That(_messages, Has.Count.EqualTo(3));
    }

    [Test]
    public void Parse_WhitespaceAroundTheEquals_IsNotPartOfEitherSide()
    {
        Parse("login.prompt    =    Password:");

        Assert.That(_messages["login.prompt"], Is.EqualTo("Password:"));
    }

    [Test]
    public void Parse_TrailingWhitespaceInAValue_IsKept()
    {
        // "Password: " needs its space: the cursor sits after it. Trimming would make every
        // prompt in the game subtly wrong.
        Parse("login.prompt = Password: ");

        Assert.That(_messages["login.prompt"], Is.EqualTo("Password: "));
    }

    [Test]
    public void Parse_AnEscapedLineFeed_BecomesOne()
    {
        Parse("login.banner = first\\nsecond");

        Assert.That(_messages["login.banner"], Is.EqualTo("first\nsecond"));
    }

    [Test]
    public void Parse_AValueContainingAnEqualsSign_KeepsIt()
    {
        Parse("math.equation = one = uno");

        Assert.That(_messages["math.equation"], Is.EqualTo("one = uno"));
    }

    [Test]
    public void Parse_CommentsAndBlankLines_AreIgnored()
    {
        Parse("# a comment", "", "   ", "  # an indented comment", "a.one = first");

        Assert.Multiple(() =>
        {
            Assert.That(_messages, Has.Count.EqualTo(1));
            Assert.That(_problems, Is.Empty);
        });
    }

    [Test]
    public void Parse_AKeyWithDots_IsOneKeyAndNotStructure()
    {
        Parse("login.error.name-taken = taken");

        Assert.That(_messages.ContainsKey("login.error.name-taken"), Is.True);
    }

    [Test]
    public void Parse_KeysAreMatchedWithoutRegardToCase()
    {
        Parse("Login.Prompt = Password:");

        Assert.That(_messages["login.prompt"], Is.EqualTo("Password:"));
    }

    [Test]
    public void Parse_AnEmptyValue_IsAllowed()
    {
        // A deliberately blank message is a legitimate thing to want.
        Parse("filler.blank =");

        Assert.Multiple(() =>
        {
            Assert.That(_messages["filler.blank"], Is.Empty);
            Assert.That(_problems, Is.Empty);
        });
    }

    [Test]
    public void Parse_ALineWithNoEquals_IsReportedWithItsLine()
    {
        Parse("a.one = first", "this line has no equals sign");

        Assert.Multiple(() =>
        {
            Assert.That(_problems, Has.Count.EqualTo(1));
            Assert.That(_problems[0], Does.StartWith("login.sgm:2: "));
        });
    }

    [Test]
    public void Parse_AnEmptyKey_IsReported()
    {
        Parse("= a value with no key");

        Assert.That(_problems, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_ADuplicateKeyInTheSameFile_IsReportedAndTheFirstWins()
    {
        Parse("a.one = first", "a.one = second");

        Assert.Multiple(() =>
        {
            Assert.That(_messages["a.one"], Is.EqualTo("first"));
            Assert.That(_problems, Has.Count.EqualTo(1));
            Assert.That(_problems[0], Does.StartWith("login.sgm:2: "));
        });
    }

    [Test]
    public void Parse_AKeyAlreadyInTheDictionary_IsReportedAsADuplicate()
    {
        // The dictionary is passed in, so a key defined in a second file collides with the
        // first through exactly this path.
        _messages["a.one"] = "from another file";

        Parse("a.one = second");

        Assert.Multiple(() =>
        {
            Assert.That(_messages["a.one"], Is.EqualTo("from another file"));
            Assert.That(_problems, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Parse_CrLfLineEndingsAndABom_AreNormalised()
    {
        MessageParser.Parse("﻿a.one = first\r\nb.two = second\r\n", "login.sgm", _messages, _problems);

        Assert.Multiple(() =>
        {
            Assert.That(_messages["a.one"], Is.EqualTo("first"));
            Assert.That(_messages["b.two"], Is.EqualTo("second"));
            Assert.That(_problems, Is.Empty);
        });
    }
}
