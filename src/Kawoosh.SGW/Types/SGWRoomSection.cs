namespace Kawoosh.SGW.Types;

/// <summary>
/// Ordered sections of a room block, per docs/sgw-format.md section 2.3.
/// </summary>
internal enum SGWRoomSection : byte
{
    BeforeRoom = 0,
    Attributes,
    Description,
    Elements
}
