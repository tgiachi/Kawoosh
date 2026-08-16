namespace Kawoosh.SGW.Types;

/// <summary>
/// Diagnostic codes defined by docs/sgw-format.md section 6.4.
/// Values below 1000 are errors and render as E&lt;nnn&gt;; values from 1001 up are
/// warnings and render as W&lt;nnn&gt; after subtracting 1000.
/// </summary>
public enum SGWDiagnosticCode
{
    UnexpectedTextOutsideRoom = 10,
    EndWithoutRoom = 11,
    RoomInsideRoom = 12,
    TextAfterRoomName = 13,
    UnterminatedRoomBlock = 14,
    UnknownDirective = 15,

    VnumOutOfRange = 20,
    InvalidEscapeSequence = 21,
    UnterminatedQuotedString = 22,
    ExpectedVnum = 23,

    MissingSector = 30,
    DuplicateAttribute = 31,
    UnknownAttribute = 32,
    EmptyAttributeValue = 33,
    UnknownSector = 34,
    SectorExpectsSingleValue = 35,

    EmptyExtraKeywordList = 40,
    DuplicateExtraKeyword = 41,
    EmptyExtraText = 42,
    MalformedExtraHeader = 43,

    UnknownDirection = 50,
    DoorModifierWithoutDoor = 51,
    DuplicateExit = 52,
    DuplicateDoorModifier = 53,
    MalformedKeyModifier = 54,
    UnexpectedTextAfterExit = 55,

    NotFullyQualifiedScriptName = 60,
    UnknownScriptEvent = 61,
    DuplicateScriptEvent = 62,
    TextAfterScriptName = 63,

    UnknownRoomFlag = 70,
    DuplicateRoomFlag = 71,
    NoneFlagCombined = 72,

    DuplicateRoomVnum = 80,
    ExitToUnknownRoom = 81,

    MissingDescription = 1001,
    OneWayExit = 1002,
    UnreachableRoom = 1003,
    SelfLoopExit = 1004
}
