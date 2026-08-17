using System.Buffers;

namespace Kawoosh.Tests.Support;

/// <summary>
/// One link of a multi-segment <see cref="ReadOnlySequence{T}" />, used to reproduce the
/// fragmented buffers a real socket hands to a PipeReader.
/// </summary>
public sealed class MemorySegment : ReadOnlySequenceSegment<byte>
{
    public MemorySegment(ReadOnlyMemory<byte> memory)
    {
        Memory = memory;
    }

    public MemorySegment Append(ReadOnlyMemory<byte> memory)
    {
        var segment = new MemorySegment(memory) { RunningIndex = RunningIndex + Memory.Length };
        Next = segment;

        return segment;
    }
}
