using Kawoosh.Server.Internal;

namespace Kawoosh.Tests.Server.Internal;

/// <summary>
/// Unit tests for the .sgs parser. Pure: no filesystem.
/// </summary>
public class ScreenParserTests
{
    private List<string> _problems = null!;

    [SetUp]
    public void SetUp()
    {
        _problems = [];
    }

    private static string Sgs(params string[] lines)
    {
        return string.Join("\n", lines);
    }

    [Test]
    public void Parse_MetadataThenSeparator_SplitsBoth()
    {
        var screen = ScreenParser.Parse(
            "greeting",
            Sgs("@author Squid", "@since 2026-08-18", "---", "BENVENUTO"),
            "greeting.sgs",
            _problems
        );

        Assert.Multiple(() =>
        {
            Assert.That(screen.Metadata["author"], Is.EqualTo("Squid"));
            Assert.That(screen.Metadata["since"], Is.EqualTo("2026-08-18"));
            Assert.That(screen.Body, Is.EqualTo("BENVENUTO"));
            Assert.That(_problems, Is.Empty);
        });
    }

    [Test]
    public void Parse_NoMetadata_TreatsTheWholeFileAsBody()
    {
        // The first line is a divider, not a separator: this is the case the whole
        // "metadata only when the file opens with @" rule exists to protect.
        var body = Sgs("---------------", "  BENVENUTO", "---------------");
        var screen = ScreenParser.Parse("greeting", body, "greeting.sgs", _problems);

        Assert.Multiple(() =>
        {
            Assert.That(screen.Body, Is.EqualTo(body));
            Assert.That(screen.Metadata, Is.Empty);
            Assert.That(_problems, Is.Empty);
        });
    }

    [Test]
    public void Parse_ADividerInsideTheBody_IsKept()
    {
        var screen = ScreenParser.Parse(
            "greeting",
            Sgs("@author Squid", "---", "---------------", "BENVENUTO", "---------------"),
            "greeting.sgs",
            _problems
        );

        Assert.That(screen.Body, Is.EqualTo(Sgs("---------------", "BENVENUTO", "---------------")));
    }

    [Test]
    public void Parse_BodyContent_IsVerbatim()
    {
        // No comments, no escapes, no line-start significance: every one of these
        // characters is art somewhere.
        var body = Sgs("# not a comment", "> not an exit", "\\ not an escape", "  {kept}");
        var screen = ScreenParser.Parse("art", Sgs("@author Squid", "---") + "\n" + body, "art.sgs", _problems);

        Assert.That(screen.Body, Is.EqualTo(body));
    }

    [Test]
    public void Parse_BlankLinesInsideTheMetadataBlock_AreIgnored()
    {
        var screen = ScreenParser.Parse(
            "greeting",
            Sgs("@author Squid", "", "@since 2026", "---", "BENVENUTO"),
            "greeting.sgs",
            _problems
        );

        Assert.Multiple(() =>
        {
            Assert.That(screen.Metadata, Has.Count.EqualTo(2));
            Assert.That(_problems, Is.Empty);
        });
    }

    [Test]
    public void Parse_MetadataKeys_AreLowercasedAndValuesTrimmed()
    {
        var screen = ScreenParser.Parse(
            "greeting",
            Sgs("@Author    Squid   ", "---", "BENVENUTO"),
            "greeting.sgs",
            _problems
        );

        Assert.That(screen.Metadata["author"], Is.EqualTo("Squid"));
    }

    [Test]
    public void Parse_AnUnknownMetadataKey_IsKept()
    {
        // Metadata is documentation, not behaviour: rejecting unknown keys would mean
        // editing code every time a writer wants a new note.
        var screen = ScreenParser.Parse(
            "greeting",
            Sgs("@mood cheerful", "---", "BENVENUTO"),
            "greeting.sgs",
            _problems
        );

        Assert.Multiple(() =>
        {
            Assert.That(screen.Metadata["mood"], Is.EqualTo("cheerful"));
            Assert.That(_problems, Is.Empty);
        });
    }

    [Test]
    public void Parse_ARepeatedMetadataKey_KeepsTheLastAndWarns()
    {
        var screen = ScreenParser.Parse(
            "greeting",
            Sgs("@author First", "@author Second", "---", "BENVENUTO"),
            "greeting.sgs",
            _problems
        );

        Assert.Multiple(() =>
        {
            Assert.That(screen.Metadata["author"], Is.EqualTo("Second"));
            Assert.That(_problems, Has.Count.EqualTo(1));
            Assert.That(_problems[0], Does.StartWith("greeting.sgs:2: "));
        });
    }

    [Test]
    public void Parse_AMetadataBlockNeverClosed_IsReported()
    {
        ScreenParser.Parse("greeting", Sgs("@author Squid", "@since 2026"), "greeting.sgs", _problems);

        Assert.Multiple(() =>
        {
            Assert.That(_problems, Has.Count.EqualTo(1));
            Assert.That(_problems[0], Does.Contain("---"));
        });
    }

    [Test]
    public void Parse_AJunkLineInsideTheMetadataBlock_IsReportedWithItsLine()
    {
        ScreenParser.Parse(
            "greeting",
            Sgs("@author Squid", "questa non e una direttiva", "---", "BENVENUTO"),
            "greeting.sgs",
            _problems
        );

        Assert.Multiple(() =>
        {
            Assert.That(_problems, Has.Count.EqualTo(1));
            Assert.That(_problems[0], Does.StartWith("greeting.sgs:2: "));
        });
    }

    [Test]
    public void Parse_AnEmptyBody_IsReported()
    {
        ScreenParser.Parse("greeting", Sgs("@author Squid", "---", "", "   "), "greeting.sgs", _problems);

        Assert.That(_problems, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_CrLfLineEndings_AreNormalised()
    {
        var screen = ScreenParser.Parse(
            "greeting",
            "@author Squid\r\n---\r\nprima\r\nseconda\r\n",
            "greeting.sgs",
            _problems
        );

        Assert.That(screen.Body, Is.EqualTo("prima\nseconda"));
    }

    [Test]
    public void Parse_AByteOrderMark_IsStripped()
    {
        var screen = ScreenParser.Parse(
            "greeting",
            "﻿@author Squid\n---\nBENVENUTO",
            "greeting.sgs",
            _problems
        );

        Assert.Multiple(() =>
        {
            Assert.That(screen.Metadata["author"], Is.EqualTo("Squid"));
            Assert.That(_problems, Is.Empty);
        });
    }

    [Test]
    public void Parse_ATrailingNewline_IsStrippedButInnerBlankLinesAreKept()
    {
        var screen = ScreenParser.Parse(
            "greeting",
            "@author Squid\n---\nprima\n\nseconda\n",
            "greeting.sgs",
            _problems
        );

        Assert.That(screen.Body, Is.EqualTo("prima\n\nseconda"));
    }

    [Test]
    public void Parse_TheName_IsCarriedOntoTheScreen()
    {
        var screen = ScreenParser.Parse("motd", Sgs("---", "x"), "motd.sgs", _problems);

        Assert.Multiple(() =>
        {
            Assert.That(screen.Name, Is.EqualTo("motd"));
            Assert.That(screen.SourceFile, Is.EqualTo("motd.sgs"));
        });
    }
}
