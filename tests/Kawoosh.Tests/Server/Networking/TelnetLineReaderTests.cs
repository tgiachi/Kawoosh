using System.Buffers;
using System.Text;
using Kawoosh.Server.Networking.Internal;
using Kawoosh.Tests.Support;

namespace Kawoosh.Tests.Server.Networking;

/// <summary>
/// Unit tests for line framing and telnet de-negotiation. No sockets involved.
/// </summary>
public class TelnetLineReaderTests
{
    private const byte Iac = 255;
    private const byte SubnegotiationEnd = 240;
    private const byte AreYouThere = 246;
    private const byte Subnegotiation = 250;
    private const byte Will = 251;
    private const byte OptionEcho = 1;
    private const byte OptionWindowSize = 31;

    private static string Text(ReadOnlySequence<byte> line)
    {
        return Encoding.UTF8.GetString(line.ToArray());
    }

    [Test]
    public void TryReadLine_CompleteLine_ReturnsItWithoutTheLineFeed()
    {
        var buffer = new ReadOnlySequence<byte>("look\n"u8.ToArray());

        var found = TelnetLineReader.TryReadLine(ref buffer, out var line);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(Text(line), Is.EqualTo("look"));
            Assert.That(buffer.Length, Is.Zero);
        });
    }

    [Test]
    public void TryReadLine_IncompleteLine_ReturnsFalseAndLeavesTheBufferUntouched()
    {
        var buffer = new ReadOnlySequence<byte>("loo"u8.ToArray());

        var found = TelnetLineReader.TryReadLine(ref buffer, out _);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.False);
            Assert.That(buffer.Length, Is.EqualTo(3));
        });
    }

    [Test]
    public void TryReadLine_TwoLinesInOneBuffer_ReturnsThemInOrder()
    {
        var buffer = new ReadOnlySequence<byte>("north\nsouth\n"u8.ToArray());

        TelnetLineReader.TryReadLine(ref buffer, out var first);
        TelnetLineReader.TryReadLine(ref buffer, out var second);

        Assert.Multiple(() =>
        {
            Assert.That(Text(first), Is.EqualTo("north"));
            Assert.That(Text(second), Is.EqualTo("south"));
            Assert.That(buffer.Length, Is.Zero);
        });
    }

    [Test]
    public void TryReadLine_TrailingPartialLine_IsLeftInTheBuffer()
    {
        var buffer = new ReadOnlySequence<byte>("north\nsou"u8.ToArray());

        TelnetLineReader.TryReadLine(ref buffer, out _);
        var found = TelnetLineReader.TryReadLine(ref buffer, out _);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.False);
            Assert.That(buffer.Length, Is.EqualTo(3));
        });
    }

    [Test]
    public void TryReadLine_LineSplitAcrossSegments_IsReassembled()
    {
        var buffer = SequenceFactory.FromSegments("no"u8.ToArray(), "rth\n"u8.ToArray());

        var found = TelnetLineReader.TryReadLine(ref buffer, out var line);

        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(Text(line), Is.EqualTo("north"));
        });
    }

    [Test]
    public void Decode_CarriageReturnTerminatedLine_StripsIt()
    {
        var line = new ReadOnlySequence<byte>("look\r"u8.ToArray());

        Assert.That(TelnetLineReader.Decode(line), Is.EqualTo("look"));
    }

    [Test]
    public void Decode_LineFeedOnlyClient_LeavesTextUnchanged()
    {
        var line = new ReadOnlySequence<byte>("look"u8.ToArray());

        Assert.That(TelnetLineReader.Decode(line), Is.EqualTo("look"));
    }

    [Test]
    public void Decode_NegotiationAtLineStart_IsStripped()
    {
        var line = new ReadOnlySequence<byte>(new byte[] { Iac, Will, OptionEcho, (byte)'h', (byte)'i' });

        Assert.That(TelnetLineReader.Decode(line), Is.EqualTo("hi"));
    }

    [Test]
    public void Decode_NegotiationInsideTheLine_IsStripped()
    {
        var line = new ReadOnlySequence<byte>(
            new byte[] { (byte)'h', Iac, Will, OptionEcho, (byte)'i' }
        );

        Assert.That(TelnetLineReader.Decode(line), Is.EqualTo("hi"));
    }

    [Test]
    public void Decode_TruncatedNegotiationAtLineEnd_DoesNotThrow()
    {
        var line = new ReadOnlySequence<byte>(new byte[] { (byte)'h', (byte)'i', Iac });

        Assert.That(TelnetLineReader.Decode(line), Is.EqualTo("hi"));
    }

    [Test]
    public void Decode_TwoByteCommand_IsStripped()
    {
        var line = new ReadOnlySequence<byte>(
            new byte[] { (byte)'h', Iac, AreYouThere, (byte)'i' }
        );

        Assert.That(TelnetLineReader.Decode(line), Is.EqualTo("hi"));
    }

    [Test]
    public void Decode_EscapedIac_EmitsOneLiteralByte()
    {
        var line = new ReadOnlySequence<byte>(new byte[] { (byte)'h', Iac, Iac, (byte)'i' });

        // 0xFF alone is not valid UTF-8, so it decodes to the replacement character.
        // What matters is that it produced one data byte and did not eat the 'i'.
        Assert.That(TelnetLineReader.Decode(line), Is.EqualTo("h�i"));
    }

    [Test]
    public void Decode_Subnegotiation_IsSkippedUpToSubnegotiationEnd()
    {
        var line = new ReadOnlySequence<byte>(
            new byte[]
            {
                (byte)'h', Iac, Subnegotiation, OptionWindowSize, 0, 80, 0, 24, Iac, SubnegotiationEnd, (byte)'i'
            }
        );

        Assert.That(TelnetLineReader.Decode(line), Is.EqualTo("hi"));
    }

    [Test]
    public void Decode_UnterminatedSubnegotiation_SwallowsTheRestOfTheLine()
    {
        var line = new ReadOnlySequence<byte>(
            new byte[] { (byte)'h', Iac, Subnegotiation, OptionWindowSize, (byte)'i' }
        );

        Assert.That(TelnetLineReader.Decode(line), Is.EqualTo("h"));
    }

    [Test]
    public void Decode_MultibyteUtf8_RoundTrips()
    {
        var line = new ReadOnlySequence<byte>("guardà il pozzo"u8.ToArray());

        Assert.That(TelnetLineReader.Decode(line), Is.EqualTo("guardà il pozzo"));
    }

    [Test]
    public void Decode_EmptyLine_ReturnsEmptyString()
    {
        var line = new ReadOnlySequence<byte>(Array.Empty<byte>());

        Assert.That(TelnetLineReader.Decode(line), Is.Empty);
    }

    [Test]
    public void Decode_BareCarriageReturnLine_ReturnsEmptyString()
    {
        var line = new ReadOnlySequence<byte>("\r"u8.ToArray());

        Assert.That(TelnetLineReader.Decode(line), Is.Empty);
    }
}
