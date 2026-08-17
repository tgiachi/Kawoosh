using Kawoosh.SGW.Data.Diagnostic;
using Kawoosh.SGW.Exceptions;
using Kawoosh.SGW.Services;
using Kawoosh.SGW.Types;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Integration.SGW.Services;

/// <summary>
/// Disk-backed tests for the world loading entry points of
/// <see cref="SGWFileParser" />, derived from docs/sgw-format.md sections 5.4 and 8.8.
/// </summary>
public class SGWFileParserTests
{
    private SGWFileParser _parser = null!;
    private TempWorldDirectory _directory = null!;

    [Test]
    public void ParseWorldFromDirectory_Diagnostics_AreOrderedByFileThenLine()
    {
        _directory.Write("b.sgw", "@room 3002 \"Second\"", "sector: nowhere", "", "Bad sector.", "@end");
        _directory.Write(
            "a.sgw",
            "@room 3001 \"First\"",
            "sector: city",
            "flags: no_such",
            "",
            "Bad flag on line 3.",
            "> north 9999",
            "@end"
        );

        _parser.ParseWorldFromDirectory(_directory.RootPath, out var diagnostics);

        Assert.Multiple(
            () =>
            {
                Assert.That(diagnostics, Is.Ordered.By(nameof(SGWDiagnostic.File)).Then.By(nameof(SGWDiagnostic.Line)));
                Assert.That(diagnostics[0].File, Is.EqualTo("a.sgw"));
                Assert.That(diagnostics.Last().File, Is.EqualTo("b.sgw"));
            }
        );
    }

    [Test]
    public void ParseWorldFromDirectory_DuplicateVnum_KeepsTheFirstDefinitionInLoadOrder()
    {
        _directory.Write("a.sgw", "@room 3001 \"First\"", "sector: city", "", "The first.", "@end");
        _directory.Write("b.sgw", "@room 3001 \"Second\"", "sector: city", "", "The second.", "@end");

        var world = _parser.ParseWorldFromDirectory(_directory.RootPath, out _);

        Assert.Multiple(
            () =>
            {
                Assert.That(world.Count, Is.EqualTo(1));
                Assert.That(world.GetRoom(3001)!.Name, Is.EqualTo("First"));
            }
        );
    }

    [Test]
    public void ParseWorldFromDirectory_DuplicateVnum_PointsAtTheFirstDefinition()
    {
        _directory.Write("a.sgw", "@room 3001 \"First\"", "sector: city", "", "The first.", "@end");
        _directory.Write("b.sgw", "@room 3001 \"Second\"", "sector: city", "", "The second.", "@end");

        _parser.ParseWorldFromDirectory(_directory.RootPath, out var diagnostics);
        var duplicate = diagnostics.First(d => d.Code == SGWDiagnosticCode.DuplicateRoomVnum);

        Assert.Multiple(
            () =>
            {
                Assert.That(duplicate.File, Is.EqualTo("b.sgw"));
                Assert.That(duplicate.Message, Is.EqualTo("duplicate room vnum 3001"));
                Assert.That(duplicate.Related, Is.Not.Null);
                Assert.That(duplicate.Related!.File, Is.EqualTo("a.sgw"));
                Assert.That(duplicate.Related!.Line, Is.EqualTo(1));
                Assert.That(duplicate.ToString(), Does.Contain("note: first defined here"));
            }
        );
    }

    // -------------------------------------------------------- duplicate vnum

    [Test]
    public void ParseWorldFromDirectory_DuplicateVnum_ReportsDuplicateRoomVnum()
    {
        _directory.Write("a.sgw", "@room 3001 \"First\"", "sector: city", "", "The first.", "@end");
        _directory.Write("b.sgw", "@room 3001 \"Second\"", "sector: city", "", "The second.", "@end");

        _parser.ParseWorldFromDirectory(_directory.RootPath, out var diagnostics);

        Assert.That(CodesOf(diagnostics), Does.Contain(SGWDiagnosticCode.DuplicateRoomVnum));
    }

    [Test]
    public void ParseWorldFromDirectory_ErrorsInSeveralFiles_AreAllCollected()
    {
        _directory.Write("a.sgw", "@room 3001 \"Broken\"", "sector: nowhere", "", "Bad sector.", "@end");
        _directory.Write("b.sgw", "@room 3002 \"Also Broken\"", "sector: city", "flags: no_such", "", "Bad flag.", "@end");

        _parser.ParseWorldFromDirectory(_directory.RootPath, out var diagnostics);

        Assert.Multiple(
            () =>
            {
                Assert.That(CodesOf(diagnostics), Does.Contain(SGWDiagnosticCode.UnknownSector));
                Assert.That(CodesOf(diagnostics), Does.Contain(SGWDiagnosticCode.UnknownRoomFlag));
            }
        );
    }

    // ---------------------------------------------------- exit cross-checking

    [Test]
    public void ParseWorldFromDirectory_ExitToUnknownRoom_ReportsExitToUnknownRoom()
    {
        _directory.Write(
            "gate.sgw",
            "@room 3001 \"A Street\"",
            "sector: city",
            "",
            "The only room.",
            "> north 9999",
            "@end"
        );

        _parser.ParseWorldFromDirectory(_directory.RootPath, out var diagnostics);
        var diagnostic = diagnostics.First(d => d.Code == SGWDiagnosticCode.ExitToUnknownRoom);

        Assert.Multiple(
            () =>
            {
                Assert.That(diagnostic.Line, Is.EqualTo(5));
                Assert.That(diagnostic.Severity, Is.EqualTo(SGWDiagnosticSeverity.Error));
                Assert.That(diagnostic.ToString(), Is.EqualTo("gate.sgw:5: exit 'north' points to unknown room vnum 9999"));
            }
        );
    }

    [Test]
    public void ParseWorldFromDirectory_ExitWithoutReciprocal_ReportsOneWayWarning()
    {
        _directory.Write(
            "gate.sgw",
            "@room 3001 \"A Street\"",
            "sector: city",
            "",
            "The first room.",
            "> north 3002",
            "@end",
            "",
            "@room 3002 \"A Dead End\"",
            "sector: city",
            "",
            "No way back.",
            "@end"
        );

        _parser.ParseWorldFromDirectory(_directory.RootPath, out var diagnostics);
        var diagnostic = diagnostics.First(d => d.Code == SGWDiagnosticCode.OneWayExit);

        Assert.Multiple(
            () =>
            {
                Assert.That(diagnostic.Severity, Is.EqualTo(SGWDiagnosticSeverity.Warning));
                Assert.That(
                    diagnostic.ToString(),
                    Is.EqualTo("gate.sgw:5: warning: exit 'north' to room 3002 is one-way")
                );
            }
        );
    }

    [Test]
    public void ParseWorldFromDirectory_IgnoresFilesWithOtherExtensions()
    {
        _directory.Write(
            "rooms.sgw",
            "@room 3001 \"A Street\"",
            "sector: city",
            "",
            "The only room.",
            "@end"
        );
        _directory.Write("notes.txt", "this is not a world file at all");
        _directory.Write("backup.sgw.bak", "@room 9999 \"Should Be Ignored\"");

        var world = _parser.ParseWorldFromDirectory(_directory.RootPath, out var diagnostics);

        Assert.Multiple(
            () =>
            {
                Assert.That(world.Count, Is.EqualTo(1));
                Assert.That(world.Contains(9999), Is.False);
                Assert.That(CodesOf(diagnostics), Does.Not.Contain(SGWDiagnosticCode.UnexpectedTextOutsideRoom));
            }
        );
    }

    // ------------------------------------------------------ error collection

    [Test]
    public void ParseWorldFromDirectory_InvalidRoom_DoesNotThrowAndKeepsTheValidOnes()
    {
        _directory.Write("a.sgw", "@room 3001 \"Broken\"", "sector: nowhere", "", "Bad sector.", "@end");
        _directory.Write("b.sgw", "@room 3002 \"Fine\"", "sector: city", "", "Good room.", "@end");

        var world = _parser.ParseWorldFromDirectory(_directory.RootPath, out var diagnostics);

        Assert.Multiple(
            () =>
            {
                Assert.That(world.Count, Is.EqualTo(1));
                Assert.That(world.Contains(3002), Is.True);
                Assert.That(CodesOf(diagnostics), Does.Contain(SGWDiagnosticCode.UnknownSector));
            }
        );
    }

    // ------------------------------------------------------------- discovery

    [Test]
    public void ParseWorldFromDirectory_LoadsRoomsFromEverySgwFile()
    {
        _directory.Write(
            "north.sgw",
            "@room 3001 \"A Street\"",
            "sector: city",
            "",
            "The first room.",
            "@end"
        );
        _directory.Write(
            "south.sgw",
            "@room 3002 \"Another Street\"",
            "sector: city",
            "",
            "The second room.",
            "@end"
        );

        var world = _parser.ParseWorldFromDirectory(_directory.RootPath, out _);

        Assert.Multiple(
            () =>
            {
                Assert.That(world.Count, Is.EqualTo(2));
                Assert.That(world.Contains(3001), Is.True);
                Assert.That(world.Contains(3002), Is.True);
            }
        );
    }

    [Test]
    public void ParseWorldFromDirectory_MissingDirectory_ThrowsDirectoryNotFound()
    {
        var missing = Path.Combine(_directory.RootPath, "does-not-exist");

        Assert.Throws<DirectoryNotFoundException>(() => _parser.ParseWorldFromDirectory(missing, out _));
    }

    [Test]
    public void ParseWorldFromDirectory_MultipleRoomsInOneFile_LoadsEveryRoom()
    {
        WriteReciprocalPair();

        var world = _parser.ParseWorldFromDirectory(_directory.RootPath, out _);

        Assert.That(world.Count, Is.EqualTo(2));
    }

    [Test]
    public void ParseWorldFromDirectory_ReadsRoomContentIntoTheModel()
    {
        _directory.Write(
            "temple.sgw",
            "@room 3001 \"The Temple Altar\"",
            "sector: indoor",
            "flags: dark, no_recall",
            "",
            "A plain stone altar stands here.",
            "extra \"altar stone\": Wax has pooled across its face.",
            "> north 3001",
            "@script enter Kawoosh.Scripts.Temple.OnEnter",
            "@end"
        );

        var world = _parser.ParseWorldFromDirectory(_directory.RootPath, out _);
        var room = world.GetRoom(3001)!;

        Assert.Multiple(
            () =>
            {
                Assert.That(room.Name, Is.EqualTo("The Temple Altar"));
                Assert.That(room.Sector, Is.EqualTo("indoor"));
                Assert.That(room.Flags, Is.EquivalentTo(new[] { RoomFlag.Dark, RoomFlag.NoRecall }));
                Assert.That(room.Description, Is.EqualTo("A plain stone altar stands here."));
                Assert.That(room.Extras[0].Keywords, Is.EqualTo(new[] { "altar", "stone" }));
                Assert.That(room.Scripts["enter"], Is.EqualTo("Kawoosh.Scripts.Temple.OnEnter"));
                Assert.That(room.SourceFile, Is.EqualTo("temple.sgw"));
            }
        );
    }

    [Test]
    public void ParseWorldFromDirectory_ReciprocalExits_ReportNoOneWayWarning()
    {
        WriteReciprocalPair();

        _parser.ParseWorldFromDirectory(_directory.RootPath, out var diagnostics);

        Assert.That(CodesOf(diagnostics), Does.Not.Contain(SGWDiagnosticCode.OneWayExit));
    }

    [Test]
    public void ParseWorldFromDirectory_RoomWithNoInboundExit_ReportsUnreachableWarning()
    {
        _directory.Write(
            "gate.sgw",
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
            "@room 3003 \"An Island\"",
            "sector: city",
            "",
            "Nothing links here.",
            "@end"
        );

        _parser.ParseWorldFromDirectory(_directory.RootPath, out var diagnostics);
        var unreachable = diagnostics.Where(d => d.Code == SGWDiagnosticCode.UnreachableRoom).ToList();

        Assert.Multiple(
            () =>
            {
                Assert.That(unreachable, Has.Count.EqualTo(1));
                Assert.That(unreachable[0].Message, Is.EqualTo("room 3003 is unreachable, no room links to it"));
            }
        );
    }

    [Test]
    public void ParseWorldFromDirectory_SelfLoopExit_ReportsSelfLoopWarning()
    {
        _directory.Write(
            "gate.sgw",
            "@room 3001 \"A Street\"",
            "sector: city",
            "",
            "The only room.",
            "> down 3001",
            "@end"
        );

        _parser.ParseWorldFromDirectory(_directory.RootPath, out var diagnostics);

        Assert.Multiple(
            () =>
            {
                Assert.That(CodesOf(diagnostics), Does.Contain(SGWDiagnosticCode.SelfLoopExit));
                Assert.That(CodesOf(diagnostics), Does.Not.Contain(SGWDiagnosticCode.OneWayExit));
            }
        );
    }

    [Test]
    public void ParseWorld_FileWithErrors_ThrowsParseException()
    {
        var path = _directory.Write(
            "gate.sgw",
            "@room 3001 \"A Street\"",
            "sector: nowhere",
            "",
            "The prose.",
            "@end"
        );

        var exception = Assert.Throws<SGWParseException>(() => _parser.ParseWorld(path));

        Assert.Multiple(
            () =>
            {
                Assert.That(CodesOf(exception!.Diagnostics), Does.Contain(SGWDiagnosticCode.UnknownSector));
                Assert.That(exception.FilePath, Is.EqualTo(path));
            }
        );
    }

    [Test]
    public void ParseWorld_MissingFile_ThrowsFileNotFound()
    {
        var missing = Path.Combine(_directory.RootPath, "nope.sgw");

        Assert.Throws<FileNotFoundException>(() => _parser.ParseWorld(missing));
    }

    // ------------------------------------------------------- single-file load

    [Test]
    public void ParseWorld_ValidFile_ReturnsWorldWithItsRooms()
    {
        var path = _directory.Write(
            "gate.sgw",
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
            "@end"
        );

        var world = _parser.ParseWorld(path);

        Assert.That(world.Count, Is.EqualTo(2));
    }

    [Test]
    public void ParseWorld_WarningsOnly_DoesNotThrow()
    {
        var path = _directory.Write(
            "gate.sgw",
            "@room 3001 \"A Lonely Street\"",
            "sector: city",
            "",
            "Nothing links here, which is only a warning.",
            "@end"
        );

        var world = _parser.ParseWorld(path);

        Assert.That(world.Count, Is.EqualTo(1));
    }

    [SetUp]
    public void SetUp()
    {
        _parser = new();
        _directory = new();
    }

    [TearDown]
    public void TearDown()
        => _directory.Dispose();

    private static IEnumerable<SGWDiagnosticCode> CodesOf(IEnumerable<SGWDiagnostic> diagnostics)
        => diagnostics.Select(d => d.Code);

    /// <summary>Two rooms wired to each other, so no one-way or unreachable warning fires.</summary>
    private void WriteReciprocalPair(string fileName = "gate.sgw")
        => _directory.Write(
            fileName,
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
            "@end"
        );
}
