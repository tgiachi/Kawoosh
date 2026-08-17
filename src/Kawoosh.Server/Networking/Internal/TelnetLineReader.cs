using System.Buffers;
using System.Text;

namespace Kawoosh.Server.Networking.Internal;

/// <summary>
/// Frames telnet input into lines and turns one framed line into text.
/// Pure: no sockets, no state, so every edge case is unit-testable.
/// </summary>
internal static class TelnetLineReader
{
    private const byte LineFeed = 10;
    private const byte CarriageReturn = 13;
    private const byte SubnegotiationEnd = 240;
    private const byte Subnegotiation = 250;
    private const byte Will = 251;
    private const byte Wont = 252;
    private const byte Do = 253;
    private const byte Dont = 254;
    private const byte InterpretAsCommand = 255;

    private const int PlainCommandLength = 2;
    private const int OptionCommandLength = 3;

    /// <summary>
    /// Removes telnet commands per RFC 854, drops the carriage return of a CRLF client, and
    /// decodes UTF-8. ASCII is a subset of UTF-8, so both client styles decode correctly.
    /// </summary>
    public static string Decode(ReadOnlySequence<byte> line)
    {
        var input = line.ToArray();

        // An escaped IAC yields one byte from two, so the output never grows.
        var output = new byte[input.Length];
        var length = 0;
        var index = 0;

        while (index < input.Length)
        {
            if (input[index] != InterpretAsCommand)
            {
                output[length] = input[index];
                length++;
                index++;

                continue;
            }

            if (index + 1 >= input.Length)
            {
                break;
            }

            var command = input[index + 1];

            if (command == InterpretAsCommand)
            {
                output[length] = InterpretAsCommand;
                length++;
                index += PlainCommandLength;

                continue;
            }

            index = SkipCommand(input, index, command);
        }

        if (length > 0 && output[length - 1] == CarriageReturn)
        {
            length--;
        }

        return Encoding.UTF8.GetString(output, 0, length);
    }

    /// <summary>
    /// Reads one line up to the next line feed. On success the buffer is advanced past the
    /// line feed and <paramref name="line" /> excludes it. On failure the buffer is untouched,
    /// so the caller can wait for more bytes.
    /// </summary>
    public static bool TryReadLine(ref ReadOnlySequence<byte> buffer, out ReadOnlySequence<byte> line)
    {
        var reader = new SequenceReader<byte>(buffer);

        if (!reader.TryReadTo(out line, LineFeed))
        {
            return false;
        }

        buffer = buffer.Slice(reader.Position);

        return true;
    }

    /// <summary>
    /// Returns the index just past the command starting at <paramref name="index" />.
    /// The escaped IAC IAC case is handled by the caller, because it emits data.
    /// </summary>
    private static int SkipCommand(byte[] input, int index, byte command)
    {
        if (command == Subnegotiation)
        {
            return SkipSubnegotiation(input, index + PlainCommandLength);
        }

        if (command is Will or Wont or Do or Dont)
        {
            return index + OptionCommandLength;
        }

        return index + PlainCommandLength;
    }

    /// <summary>
    /// Skips subnegotiation payload up to and including IAC SE. An unterminated
    /// subnegotiation swallows the rest of the line, which is the safe reading.
    /// </summary>
    private static int SkipSubnegotiation(byte[] input, int index)
    {
        while (index + 1 < input.Length)
        {
            if (input[index] == InterpretAsCommand && input[index + 1] == SubnegotiationEnd)
            {
                return index + PlainCommandLength;
            }

            index++;
        }

        return input.Length;
    }
}
