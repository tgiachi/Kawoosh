using Kawoosh.SGW.Data.World;
using Kawoosh.SGW.Exceptions;
using Kawoosh.SGW.Services;
using Kawoosh.SGW.Types;

namespace Kawoosh.Tests.SGW.Services;

/// <summary>
/// Tests for <see cref="SGWFileParser.ParseRoom" />, derived from docs/sgw-format.md.
/// The specification is the contract; these tests encode it.
/// </summary>
public class SGWFileParserTests
{
    private SGWFileParser _parser = null!;

    [Test]
    public void ParseRoom_BlankLineInsideDescription_IsPreservedAsParagraphBreak()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "",
                "First paragraph.",
                "",
                "Second paragraph.",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("First paragraph.\n\nSecond paragraph."));
    }

    [Test]
    public void ParseRoom_BlankLineInsideExtra_DoesNotCloseIt()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "extra \"door\": First.",
                "    Second.",
                "",
                "    Third.",
                "> north 3002",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Extras[0].Text, Is.EqualTo("First.\nSecond.\n\nThird."));
                Assert.That(room.Exits, Has.Count.EqualTo(1));
            }
        );
    }

    [Test]
    public void ParseRoom_ByteOrderMark_IsStripped()
    {
        var content = "﻿" +
                      Sgw(
                          "@room 3001 \"The Temple Altar\"",
                          "sector: indoor",
                          "@end"
                      );

        var room = _parser.ParseRoom(content);

        Assert.That(room.Id, Is.EqualTo(3001));
    }

    [Test]
    public void ParseRoom_ColonBearingDescriptionLine_IsProseNotAttribute()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "",
                "A plain stone altar stands here.",
                "sector: this line is prose, not an attribute",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Sector, Is.EqualTo("indoor"));
                Assert.That(
                    room.Description,
                    Is.EqualTo("A plain stone altar stands here.\nsector: this line is prose, not an attribute")
                );
            }
        );
    }

    [Test]
    public void ParseRoom_CommentInsideDescription_LeavesNoBlankLine()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "",
                "First line.",
                "# this comment vanishes entirely",
                "Second line.",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("First line.\nSecond line."));
    }

    // -------------------------------------------------------------- comments

    [Test]
    public void ParseRoom_CommentLine_IsIgnored()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "# a leading comment",
                "@room 3001 \"A Street\"",
                "# another comment",
                "sector: city",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Id, Is.EqualTo(3001));
                Assert.That(room.Sector, Is.EqualTo("city"));
            }
        );
    }

    // -------------------------------------------- block boundary and encoding

    [Test]
    public void ParseRoom_ContentAfterEndDirective_IsIgnored()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "",
                "The prose.",
                "@end",
                "@room 3002 \"Should Not Be Parsed\"",
                "sector: forest"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Id, Is.EqualTo(3001));
                Assert.That(room.Sector, Is.EqualTo("indoor"));
            }
        );
    }

    [Test]
    public void ParseRoom_ContentWithErrors_ThrowsParseException()
    {
        var exception = Assert.Throws<SGWParseException>(
            () => _parser.ParseRoom(
                Sgw(
                    "@room 3001 \"A Street\"",
                    "sector: nowhere",
                    "@end"
                )
            )
        );

        Assert.That(exception!.Diagnostics.Select(d => d.Code), Does.Contain(SGWDiagnosticCode.UnknownSector));
    }

    [Test]
    public void ParseRoom_CrLfLineEndings_AreNormalised()
    {
        var content = "@room 3001 \"The Temple Altar\"\r\nsector: indoor\r\n\r\nFirst line.\r\nSecond line.\r\n@end\r\n";

        var room = _parser.ParseRoom(content);

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Id, Is.EqualTo(3001));
                Assert.That(room.Description, Is.EqualTo("First line.\nSecond line."));
            }
        );
    }

    [Test]
    public void ParseRoom_DescriptionEndsAtEndDirective()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "",
                "The prose.",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("The prose."));
    }

    [Test]
    public void ParseRoom_DescriptionEndsAtScriptLine()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "",
                "The prose.",
                "@script enter Kawoosh.Scripts.Temple.OnEnter",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("The prose."));
    }

    [Test]
    public void ParseRoom_DescriptionWithoutBlankLineAfterAttributes_IsStillParsed()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "A plain stone altar stands here.",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("A plain stone altar stands here."));
    }

    // ----------------------------------------------------------- description

    [Test]
    public void ParseRoom_Description_JoinsLinesWithLineFeed()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "",
                "A plain stone altar stands here.",
                "Candle wax has pooled on the flagstones.",
                "@end"
            )
        );

        Assert.That(
            room.Description,
            Is.EqualTo("A plain stone altar stands here.\nCandle wax has pooled on the flagstones.")
        );
    }

    [Test]
    public void ParseRoom_DirectiveCasing_IsIgnored()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@ROOM 3001 \"The Temple Altar\"",
                "sector: indoor",
                "@End"
            )
        );

        Assert.That(room.Id, Is.EqualTo(3001));
    }

    [Test]
    public void ParseRoom_DoorModifiersInReverseOrder_AreAccepted()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "> up 3013 door \"a hatch\" key=9002 locked",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Exits[0].Door!.IsLocked, Is.True);
                Assert.That(room.Exits[0].Door!.KeyVnum, Is.EqualTo(9002));
            }
        );
    }

    [Test]
    public void ParseRoom_DoubleBackslashAtLineStart_YieldsSingleBackslash()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Corridor\"",
                "sector: indoor",
                "",
                "\\\\a literal backslash starts this line",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("\\a literal backslash starts this line"));
    }

    [Test]
    public void ParseRoom_EscapedEndDirective_YieldsLiteralText()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Corridor\"",
                "sector: indoor",
                "",
                "\\@end is written on the wall.",
                "Still inside the description.",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("@end is written on the wall.\nStill inside the description."));
    }

    // ------------------------------------------------------ line-start escape

    [Test]
    public void ParseRoom_EscapedHashAtLineStart_YieldsLiteralHash()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Corridor\"",
                "sector: indoor",
                "",
                "\\# this is prose, not a comment",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("# this is prose, not a comment"));
    }

    [Test]
    public void ParseRoom_ExitDirectionAlias_ResolvesToCanonicalDirection()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "> n 3002",
                "@end"
            )
        );

        Assert.That(room.Exits[0].Direction, Is.EqualTo(SGWDirection.North));
    }

    [Test]
    public void ParseRoom_ExitLine_EndsDescription()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "",
                "The prose.",
                "> north 3002",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Description, Is.EqualTo("The prose."));
                Assert.That(room.Exits, Has.Count.EqualTo(1));
            }
        );
    }

    [Test]
    public void ParseRoom_ExitWithDoor_ParsesDoorName()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "> east 3010 door \"an oak door\"",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Exits[0].Door, Is.Not.Null);
                Assert.That(room.Exits[0].Door!.Name, Is.EqualTo("an oak door"));
                Assert.That(room.Exits[0].Door!.IsLocked, Is.False);
                Assert.That(room.Exits[0].Door!.KeyVnum, Is.Null);
            }
        );
    }

    [Test]
    public void ParseRoom_ExitWithEmptyDoorName_IsAccepted()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "> south 3014 door \"\"",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Exits[0].Door, Is.Not.Null);
                Assert.That(room.Exits[0].Door!.Name, Is.Empty);
            }
        );
    }

    [Test]
    public void ParseRoom_ExitWithKeyedDoor_SetsKeyVnum()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "> down 3012 door \"a trapdoor\" locked key=9001",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Exits[0].Door!.IsLocked, Is.True);
                Assert.That(room.Exits[0].Door!.KeyVnum, Is.EqualTo(9001));
            }
        );
    }

    [Test]
    public void ParseRoom_ExitWithLockedDoor_SetsIsLocked()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "> west 3011 door \"an iron gate\" locked",
                "@end"
            )
        );

        Assert.That(room.Exits[0].Door!.IsLocked, Is.True);
    }

    [Test]
    public void ParseRoom_ExtraContinuationLines_AreJoinedToInlineText()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "extra \"door\": The door is a slab of iron,",
                "    taller than a man and twice as wide.",
                "@end"
            )
        );

        Assert.That(room.Extras[0].Text, Is.EqualTo("The door is a slab of iron,\ntaller than a man and twice as wide."));
    }

    [Test]
    public void ParseRoom_ExtraKeywords_AreLowercased()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "extra \"Door IRON\": A slab of black iron.",
                "@end"
            )
        );

        Assert.That(room.Extras[0].Keywords, Is.EqualTo(new[] { "door", "iron" }));
    }

    [Test]
    public void ParseRoom_ExtraOpener_EndsDescription()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "",
                "The prose.",
                "extra \"door\": A slab of iron.",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Description, Is.EqualTo("The prose."));
                Assert.That(room.Extras, Has.Count.EqualTo(1));
            }
        );
    }

    [Test]
    public void ParseRoom_ExtraRelativeIndentation_IsPreserved()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "extra \"map\":",
                "    top",
                "        indented further",
                "@end"
            )
        );

        Assert.That(room.Extras[0].Text, Is.EqualTo("top\n    indented further"));
    }

    // ---------------------------------------------------------------- extras

    [Test]
    public void ParseRoom_ExtraWithInlineText_IsParsed()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "extra \"door\": The door is a slab of iron.",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Extras, Has.Count.EqualTo(1));
                Assert.That(room.Extras[0].Keywords, Is.EqualTo(new[] { "door" }));
                Assert.That(room.Extras[0].Text, Is.EqualTo("The door is a slab of iron."));
            }
        );
    }

    [Test]
    public void ParseRoom_ExtraWithMultipleKeywords_RegistersEveryKeyword()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "extra \"door iron slab\": A slab of black iron.",
                "@end"
            )
        );

        Assert.That(room.Extras[0].Keywords, Is.EqualTo(new[] { "door", "iron", "slab" }));
    }

    [Test]
    public void ParseRoom_ExtraWithOnlyContinuationLines_IsParsed()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "extra \"glyphs\":",
                "    The glyphs spiral inward.",
                "    You cannot read them.",
                "@end"
            )
        );

        Assert.That(room.Extras[0].Text, Is.EqualTo("The glyphs spiral inward.\nYou cannot read them."));
    }

    [Test]
    public void ParseRoom_FlagAliases_ResolveToCanonicalFlags()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Vault\"",
                "sector: indoor",
                "flags: nomob norecall indoors",
                "@end"
            )
        );

        Assert.That(room.Flags, Is.EquivalentTo(new[] { RoomFlag.NoMob, RoomFlag.NoRecall, RoomFlag.Indoors }));
    }

    [Test]
    public void ParseRoom_FlagsCasing_IsIgnored()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Vault\"",
                "sector: indoor",
                "flags: DARK, Safe",
                "@end"
            )
        );

        Assert.That(room.Flags, Is.EquivalentTo(new[] { RoomFlag.Dark, RoomFlag.Safe }));
    }

    [Test]
    public void ParseRoom_FlagsCommaSeparated_ParsesEveryFlag()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Vault\"",
                "sector: indoor",
                "flags: safe, indoor",
                "@end"
            )
        );

        Assert.That(room.Flags, Is.EquivalentTo(new[] { RoomFlag.Safe, RoomFlag.Indoors }));
    }

    [Test]
    public void ParseRoom_FlagsNone_YieldsEmptyFlagSet()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "flags: none",
                "@end"
            )
        );

        Assert.That(room.Flags, Is.Empty);
    }

    // ----------------------------------------------------------------- flags

    [Test]
    public void ParseRoom_FlagsSpaceSeparated_ParsesEveryFlag()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Vault\"",
                "sector: indoor",
                "flags: dark no_mob no_recall",
                "@end"
            )
        );

        Assert.That(room.Flags, Is.EquivalentTo(new[] { RoomFlag.Dark, RoomFlag.NoMob, RoomFlag.NoRecall }));
    }

    [Test]
    public void ParseRoom_HashAwayFromLineStart_IsLiteralText()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Corridor\"",
                "sector: indoor",
                "",
                "Room #7 is down the hall.",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("Room #7 is down the hall."));
    }

    [Test]
    public void ParseRoom_HeaderWithExtraWhitespace_IsAccepted()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "   @room    3001    \"The Temple Altar\"   ",
                "sector: indoor",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Id, Is.EqualTo(3001));
                Assert.That(room.Name, Is.EqualTo("The Temple Altar"));
            }
        );
    }

    [Test]
    public void ParseRoom_IndentedCommentLine_IsIgnored()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "    # an indented comment",
                "sector: city",
                "@end"
            )
        );

        Assert.That(room.Sector, Is.EqualTo("city"));
    }

    [Test]
    public void ParseRoom_LastLineWithoutTerminator_IsStillParsed()
    {
        var content = "@room 3001 \"The Temple Altar\"\nsector: indoor\n\nThe prose.\n@end";

        var room = _parser.ParseRoom(content);

        Assert.That(room.Description, Is.EqualTo("The prose."));
    }

    [Test]
    public void ParseRoom_LeadingAndTrailingBlankLinesOfDescription_AreTrimmed()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "",
                "",
                "The only prose.",
                "",
                "",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("The only prose."));
    }

    [Test]
    public void ParseRoom_LeadingWhitespaceOnDescriptionLine_IsPreserved()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "",
                "Flush left.",
                "    Indented by four.",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("Flush left.\n    Indented by four."));
    }

    [Test]
    public void ParseRoom_LoneCrLineEndings_AreNormalised()
    {
        var content = "@room 3001 \"The Temple Altar\"\rsector: indoor\r\rFirst line.\r@end\r";

        var room = _parser.ParseRoom(content);

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Id, Is.EqualTo(3001));
                Assert.That(room.Description, Is.EqualTo("First line."));
            }
        );
    }

    [Test]
    public void ParseRoom_MultipleExits_AreAllParsed()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "> north 3002",
                "> s 3003",
                "> up 3004",
                "@end"
            )
        );

        Assert.That(
            room.Exits.Select(e => e.Direction),
            Is.EqualTo(new[] { SGWDirection.North, SGWDirection.South, SGWDirection.Up })
        );
    }

    [Test]
    public void ParseRoom_MultipleScriptEvents_AreAllStored()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "@script enter Kawoosh.Scripts.Temple.OnEnter",
                "@script exit  Kawoosh.Scripts.Temple.OnExit",
                "@script look  Kawoosh.Scripts.Temple.OnLook",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Scripts, Has.Count.EqualTo(3));
                Assert.That(room.Scripts["enter"], Is.EqualTo("Kawoosh.Scripts.Temple.OnEnter"));
                Assert.That(room.Scripts["exit"], Is.EqualTo("Kawoosh.Scripts.Temple.OnExit"));
                Assert.That(room.Scripts["look"], Is.EqualTo("Kawoosh.Scripts.Temple.OnLook"));
            }
        );
    }

    // ---------------------------------------------------------------- header

    [Test]
    public void ParseRoom_RoomHeader_ParsesVnumAndName()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Id, Is.EqualTo(3001));
                Assert.That(room.Name, Is.EqualTo("The Temple Altar"));
            }
        );
    }

    [Test]
    public void ParseRoom_RoomNameWithEscapedQuotes_IsUnescaped()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The \\\"Old\\\" Inn\"",
                "sector: indoor",
                "@end"
            )
        );

        Assert.That(room.Name, Is.EqualTo("The \"Old\" Inn"));
    }

    [Test]
    public void ParseRoom_ScriptEventCasing_IsNormalisedToLowercase()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "@SCRIPT Enter Kawoosh.Scripts.Temple.OnEnter",
                "@end"
            )
        );

        Assert.That(room.Scripts, Does.ContainKey("enter"));
    }

    // --------------------------------------------------------------- scripts

    [Test]
    public void ParseRoom_ScriptLine_IsStoredUnderItsEvent()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "@script enter Kawoosh.Scripts.Temple.OnEnter",
                "@end"
            )
        );

        Assert.That(room.Scripts, Does.ContainKey("enter").WithValue("Kawoosh.Scripts.Temple.OnEnter"));
    }

    [Test]
    public void ParseRoom_ScriptName_KeepsItsOriginalCasing()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "@script enter Kawoosh.Scripts.Temple.OnEnter",
                "@end"
            )
        );

        Assert.That(room.Scripts["enter"], Is.EqualTo("Kawoosh.Scripts.Temple.OnEnter"));
    }

    [Test]
    public void ParseRoom_SectorAlias_ResolvesToCanonicalToken()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Clearing\"",
                "sector: wood",
                "@end"
            )
        );

        Assert.That(room.Sector, Is.EqualTo("forest"));
    }

    // ---------------------------------------------------------------- sector

    [Test]
    public void ParseRoom_SectorAttribute_IsStored()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Clearing\"",
                "sector: forest",
                "@end"
            )
        );

        Assert.That(room.Sector, Is.EqualTo("forest"));
    }

    [Test]
    public void ParseRoom_SectorCasing_IsNormalisedToLowercase()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Clearing\"",
                "SECTOR: Forest",
                "@end"
            )
        );

        Assert.That(room.Sector, Is.EqualTo("forest"));
    }

    [Test]
    public void ParseRoom_SectorWithWhitespaceAroundColon_IsAccepted()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector   :   city   ",
                "@end"
            )
        );

        Assert.That(room.Sector, Is.EqualTo("city"));
    }

    // ----------------------------------------------------------------- exits

    [Test]
    public void ParseRoom_SimpleExit_IsParsed()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "> north 3002",
                "@end"
            )
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Exits, Has.Count.EqualTo(1));
                Assert.That(room.Exits[0].Direction, Is.EqualTo(SGWDirection.North));
                Assert.That(room.Exits[0].TargetVnum, Is.EqualTo(3002));
                Assert.That(room.Exits[0].Door, Is.Null);
            }
        );
    }

    [Test]
    public void ParseRoom_TrailingWhitespaceOnDescriptionLine_IsStripped()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"The Temple Altar\"",
                "sector: indoor",
                "",
                "The only prose.   ",
                "@end"
            )
        );

        Assert.That(room.Description, Is.EqualTo("The only prose."));
    }

    [Test]
    public void ParseRoom_VnumWithLeadingZeros_IsInsignificant()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 00042 \"Cell Forty-Two\"",
                "sector: dungeon",
                "@end"
            )
        );

        Assert.That(room.Id, Is.EqualTo(42));
    }

    [Test]
    public void ParseRoom_WhitespaceOnlyDescription_YieldsEmptyString()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "   ",
                "\t",
                "@end"
            )
        );

        Assert.That(room.Description, Is.Empty);
    }

    [Test]
    public void ParseRoom_WithoutDescription_YieldsEmptyString()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "@end"
            )
        );

        Assert.That(room.Description, Is.Empty);
    }

    [Test]
    public void ParseRoom_WithoutFlagsAttribute_YieldsEmptyFlagSet()
    {
        var room = _parser.ParseRoom(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "@end"
            )
        );

        Assert.That(room.Flags, Is.Empty);
    }

    [SetUp]
    public void SetUp()
        => _parser = new();

    [Test]
    public void TryParseFile_EmptyContent_ReturnsNoRooms()
    {
        var result = _parser.TryParseFile(string.Empty, "empty.sgw");

        Assert.Multiple(
            () =>
            {
                Assert.That(result.Rooms, Is.Empty);
                Assert.That(result.Diagnostics, Is.Empty);
            }
        );
    }

    [Test]
    public void TryParseFile_ErrorInFirstRoom_StillParsesLaterRooms()
    {
        var result = _parser.TryParseFile(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: nowhere",
                "",
                "The broken room.",
                "@end",
                "",
                "@room 3002 \"Another Street\"",
                "sector: city",
                "",
                "The good room.",
                "@end"
            ),
            "gate.sgw"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(result.Rooms.Select(r => r.Id), Is.EqualTo(new[] { 3002 }));
                Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain(SGWDiagnosticCode.UnknownSector));
            }
        );
    }

    [Test]
    public void TryParseFile_MultipleRooms_ReturnsEveryRoom()
    {
        var result = _parser.TryParseFile(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "",
                "The first room.",
                "> north 3002",
                "@end",
                "",
                "@room 3002 \"Another Street\"",
                "sector: city",
                "",
                "The second room.",
                "> south 3001",
                "@end",
                "",
                "@room 3003 \"A Third Street\"",
                "sector: city",
                "",
                "The third room.",
                "@end"
            ),
            "gate.sgw"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(result.Rooms.Select(r => r.Id), Is.EqualTo(new[] { 3001, 3002, 3003 }));
                Assert.That(result.Diagnostics, Is.Empty);
            }
        );
    }

    [Test]
    public void TryParseFile_OnlyCommentsAndBlanks_ReturnsNoRooms()
    {
        var result = _parser.TryParseFile(
            Sgw(
                "# a header comment",
                "",
                "# another comment",
                ""
            ),
            "empty.sgw"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(result.Rooms, Is.Empty);
                Assert.That(result.Diagnostics, Is.Empty);
            }
        );
    }

    [Test]
    public void TryParseFile_RecordsSourceFileAndHeaderLineOfEachRoom()
    {
        var result = _parser.TryParseFile(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "",
                "The first room.",
                "@end",
                "",
                "@room 3002 \"Another Street\"",
                "sector: city",
                "",
                "The second room.",
                "@end"
            ),
            "gate.sgw"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(result.Rooms[0].SourceFile, Is.EqualTo("gate.sgw"));
                Assert.That(result.Rooms[0].SourceLine, Is.EqualTo(1));
                Assert.That(result.Rooms[1].SourceLine, Is.EqualTo(7));
            }
        );
    }

    [Test]
    public void TryParseFile_RecordsSourceLineOfEachExit()
    {
        var result = _parser.TryParseFile(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "",
                "The prose.",
                "> north 3002",
                "> south 3003",
                "@end"
            ),
            "gate.sgw"
        );

        Assert.That(result.Rooms[0].Exits.Select(e => e.SourceLine), Is.EqualTo(new[] { 5, 6 }));
    }

    [Test]
    public void TryParseFile_RoomWithError_IsExcludedButKeepsItsDiagnostics()
    {
        var result = _parser.TryParseFile(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: nowhere",
                "",
                "The prose.",
                "@end"
            ),
            "gate.sgw"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(result.Rooms, Is.Empty);
                Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain(SGWDiagnosticCode.UnknownSector));
                Assert.That(result.HasErrors, Is.True);
            }
        );
    }

    // ------------------------------------------------------- whole-file parse

    [Test]
    public void TryParseFile_SingleRoom_ReturnsThatRoom()
    {
        var result = _parser.TryParseFile(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "",
                "The prose.",
                "@end"
            ),
            "gate.sgw"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(result.Rooms, Has.Count.EqualTo(1));
                Assert.That(result.Rooms[0].Id, Is.EqualTo(3001));
                Assert.That(result.Diagnostics, Is.Empty);
            }
        );
    }

    [Test]
    public void TryParseFile_TrailingCommentsAfterLastRoom_ProduceNoDiagnostics()
    {
        var result = _parser.TryParseFile(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "",
                "The prose.",
                "@end",
                "",
                "# nothing more to see here",
                ""
            ),
            "gate.sgw"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(result.Rooms, Has.Count.EqualTo(1));
                Assert.That(result.Diagnostics, Is.Empty);
            }
        );
    }

    [Test]
    public void TryParseFile_UnterminatedLastRoom_ReportsUnterminatedRoomBlock()
    {
        var result = _parser.TryParseFile(
            Sgw(
                "@room 3001 \"A Street\"",
                "sector: city",
                "",
                "The first room.",
                "@end",
                "",
                "@room 3002 \"Another Street\"",
                "sector: city",
                "",
                "The truncated room."
            ),
            "gate.sgw"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(result.Rooms.Select(r => r.Id), Is.EqualTo(new[] { 3001 }));
                Assert.That(
                    result.Diagnostics.Select(d => d.Code),
                    Does.Contain(SGWDiagnosticCode.UnterminatedRoomBlock)
                );
            }
        );
    }

    [Test]
    public void TryParseRoom_BareScriptName_ReportsNotFullyQualifiedScriptName()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "@script enter OnEnter",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.NotFullyQualifiedScriptName));
    }

    [Test]
    public void TryParseRoom_DiagonalDirection_IsRejectedAsUnknown()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "> ne 3002",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.UnknownDirection));
    }

    [Test]
    public void TryParseRoom_DuplicateSectorAttribute_ReportsDuplicateAttribute()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "sector: forest",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.DuplicateAttribute));
    }

    [Test]
    public void TryParseRoom_EmptySectorValue_ReportsEmptyAttributeValue()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector:",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.EmptyAttributeValue));
    }

    [Test]
    public void TryParseRoom_EndWithoutRoom_ReportsEndWithoutRoom()
    {
        var result = Parse("@end");

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.EndWithoutRoom));
    }

    [Test]
    public void TryParseRoom_ErrorDiagnostic_RendersAsFileLineMessage()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "flags: no_recal",
            "@end"
        );

        var diagnostic = result.Errors.First();

        Assert.Multiple(
            () =>
            {
                Assert.That(diagnostic.ToString(), Is.EqualTo("temple.sgw:3: unknown room flag 'no_recal'"));
                Assert.That(diagnostic.CodeText, Is.EqualTo("E070"));
            }
        );
    }

    [Test]
    public void TryParseRoom_InvalidEscapeInRoomName_ReportsInvalidEscapeSequence()
    {
        var result = Parse(
            "@room 3001 \"A \\q Street\"",
            "sector: city",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.InvalidEscapeSequence));
    }

    [Test]
    public void TryParseRoom_KeyModifierWithSpaces_ReportsMalformedKeyModifier()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "> north 3002 door \"a door\" key = 9001",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.MalformedKeyModifier));
    }

    [Test]
    public void TryParseRoom_LockedWithoutDoor_ReportsDoorModifierWithoutDoor()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "> north 3002 locked",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.DoorModifierWithoutDoor));
    }

    [Test]
    public void TryParseRoom_MissingEndDirective_ReportsUnterminatedRoomBlock()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "",
            "The prose."
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.UnterminatedRoomBlock));
    }

    [Test]
    public void TryParseRoom_MissingSector_ReportsMissingSector()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "",
            "The prose.",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.MissingSector));
    }

    [Test]
    public void TryParseRoom_NestedRoomHeader_ReportsRoomInsideRoom()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "@room 3002 \"Another Street\"",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.RoomInsideRoom));
    }

    [Test]
    public void TryParseRoom_NonNumericVnum_ReportsExpectedVnum()
    {
        var result = Parse(
            "@room abc \"A Street\"",
            "sector: city",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.ExpectedVnum));
    }

    [Test]
    public void TryParseRoom_NoneCombinedWithOtherFlags_ReportsNoneFlagCombined()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "flags: none dark",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.NoneFlagCombined));
    }

    [Test]
    public void TryParseRoom_RepeatedDoorModifier_ReportsDuplicateDoorModifier()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "> north 3002 door \"a door\" locked locked",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.DuplicateDoorModifier));
    }

    [Test]
    public void TryParseRoom_RepeatedFlagAlias_ReportsDuplicateRoomFlag()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "flags: no_mob nomob",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.DuplicateRoomFlag));
    }

    [Test]
    public void TryParseRoom_RoomWithErrors_IsExcludedFromTheResult()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: nowhere",
            "@end"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(result.HasErrors, Is.True);
                Assert.That(result.Room, Is.Null);
            }
        );
    }

    [Test]
    public void TryParseRoom_RoomWithoutDescription_ReportsMissingDescriptionAsWarning()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "@end"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.MissingDescription));
                Assert.That(result.HasErrors, Is.False);
                Assert.That(result.Room, Is.Not.Null);
            }
        );
    }

    [Test]
    public void TryParseRoom_SameDirectionTwice_ReportsDuplicateExit()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "> n 3002",
            "> north 3003",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.DuplicateExit));
    }

    [Test]
    public void TryParseRoom_SameExtraKeywordTwice_ReportsDuplicateExtraKeyword()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "extra \"door\": A slab of iron.",
            "extra \"door\": Another description.",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.DuplicateExtraKeyword));
    }

    [Test]
    public void TryParseRoom_SameScriptEventTwice_ReportsDuplicateScriptEvent()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "@script enter Kawoosh.Scripts.Street.OnEnter",
            "@script enter Kawoosh.Scripts.Street.OnEnterAgain",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.DuplicateScriptEvent));
    }

    [Test]
    public void TryParseRoom_SectorWithTwoTokens_ReportsSectorExpectsSingleValue()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: forest city",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.SectorExpectsSingleValue));
    }

    [Test]
    public void TryParseRoom_SeveralErrors_AreAllCollected()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: nowhere",
            "flags: no_such_flag",
            "@end"
        );

        Assert.That(
            result.Errors.Select(d => d.Code),
            Is.EqualTo(new[] { SGWDiagnosticCode.UnknownSector, SGWDiagnosticCode.UnknownRoomFlag })
        );
    }

    [Test]
    public void TryParseRoom_TextAfterScriptName_ReportsTextAfterScriptName()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "@script enter Kawoosh.Scripts.Street.OnEnter extra junk",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.TextAfterScriptName));
    }

    [Test]
    public void TryParseRoom_TextBeforeRoomBlock_ReportsUnexpectedTextOutsideRoom()
    {
        var result = Parse(
            "stray prose",
            "@room 3001 \"A Street\"",
            "sector: city",
            "",
            "The prose.",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.UnexpectedTextOutsideRoom));
    }

    [Test]
    public void TryParseRoom_UnknownAttribute_ReportsUnknownAttribute()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "smell: damp",
            "sector: city",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.UnknownAttribute));
    }

    [Test]
    public void TryParseRoom_UnknownDirection_ReportsUnknownDirection()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "> sideways 3002",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.UnknownDirection));
    }

    [Test]
    public void TryParseRoom_UnknownDirective_ReportsUnknownDirective()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "@teleport 3002",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.UnknownDirective));
    }

    [Test]
    public void TryParseRoom_UnknownFlag_ReportsUnknownRoomFlag()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "flags: no_recal",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.UnknownRoomFlag));
    }

    [Test]
    public void TryParseRoom_UnknownScriptEvent_ReportsUnknownScriptEvent()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "@script sneeze Kawoosh.Scripts.Street.OnSneeze",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.UnknownScriptEvent));
    }

    [Test]
    public void TryParseRoom_UnknownSector_ReportsUnknownSector()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: nowhere",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.UnknownSector));
    }

    [Test]
    public void TryParseRoom_UnterminatedRoomName_ReportsUnterminatedQuotedString()
    {
        var result = Parse(
            "@room 3001 \"A Street",
            "sector: city",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.UnterminatedQuotedString));
    }

    [Test]
    public void TryParseRoom_ValidRoom_ReportsNoDiagnostics()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "",
            "The prose.",
            "@end"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(result.Diagnostics, Is.Empty);
                Assert.That(result.HasErrors, Is.False);
                Assert.That(result.Room, Is.Not.Null);
            }
        );
    }

    [Test]
    public void TryParseRoom_VnumAboveInt32Range_ReportsVnumOutOfRange()
    {
        var result = Parse(
            "@room 99999999999 \"A Street\"",
            "sector: city",
            "@end"
        );

        Assert.That(CodesOf(result), Does.Contain(SGWDiagnosticCode.VnumOutOfRange));
    }

    [Test]
    public void TryParseRoom_VnumZero_IsAccepted()
    {
        var result = Parse(
            "@room 0 \"The Void\"",
            "sector: air",
            "",
            "Nothing at all.",
            "@end"
        );

        Assert.Multiple(
            () =>
            {
                Assert.That(result.Diagnostics, Is.Empty);
                Assert.That(result.Room!.Id, Is.Zero);
            }
        );
    }

    [Test]
    public void TryParseRoom_WarningDiagnostic_RendersWithWarningMarker()
    {
        var result = Parse(
            "@room 3001 \"A Street\"",
            "sector: city",
            "@end"
        );

        var diagnostic = result.Warnings.First();

        Assert.Multiple(
            () =>
            {
                Assert.That(diagnostic.ToString(), Is.EqualTo("temple.sgw:1: warning: room 3001 has no description"));
                Assert.That(diagnostic.CodeText, Is.EqualTo("W001"));
            }
        );
    }

    // ----------------------------------------------------------- diagnostics

    private static IEnumerable<SGWDiagnosticCode> CodesOf(SGWRoomParseResult result)
        => result.Diagnostics.Select(d => d.Code);

    private SGWRoomParseResult Parse(params string[] lines)
        => _parser.TryParseRoom(Sgw(lines), "temple.sgw");

    /// <summary>
    /// Builds a room block with explicit control over every character of every line,
    /// which raw string literals would obscure for whitespace-sensitive cases.
    /// </summary>
    private static string Sgw(params string[] lines)
        => string.Join("\n", lines);
}
