using System.Buffers;

namespace Kawoosh.Tests.Support;

public static class SequenceFactory
{
    /// <summary>Builds a sequence split across the given segments, in order.</summary>
    public static ReadOnlySequence<byte> FromSegments(params byte[][] segments)
    {
        var first = new MemorySegment(segments[0]);
        var last = first;

        for (var i = 1; i < segments.Length; i++)
        {
            last = last.Append(segments[i]);
        }

        return new(first, 0, last, last.Memory.Length);
    }
}
