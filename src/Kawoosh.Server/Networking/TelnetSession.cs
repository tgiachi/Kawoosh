using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Data.World;
using Kawoosh.Server.Networking.Internal;
using Kawoosh.Server.Types;
using Serilog;

namespace Kawoosh.Server.Networking;

/// <summary>
/// One connected telnet client. Runs an inbound loop that frames lines into commands on the
/// shared channel, and an outbound loop that drains a per-session queue onto the socket.
/// </summary>
public sealed class TelnetSession : IDisposable
{
    /// <summary>
    /// The longest line accepted from a client, in bytes. Without a cap the inbound pipe keeps
    /// chaining segments for a client that never sends a line feed, so one connection can
    /// exhaust the process memory. Generous for a MUD: no player types eight kilobytes.
    /// </summary>
    internal const int MaxLineLength = 8 * 1024;

    /// <summary>How many messages may be queued for one client before writes start dropping.</summary>
    internal const int OutboundCapacity = 64;

    private const string LineTerminator = "\r\n";

    private readonly ILogger _logger = Log.ForContext<TelnetSession>();
    private readonly TcpClient _client;
    private readonly ChannelWriter<Command> _commands;
    private readonly Channel<byte[]> _outbound;

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The logged-in character, null until the session authenticates.</summary>
    public Character? Character { get; set; }

    /// <summary>
    /// Where this session is in the conversation. Only ever read and written on the game
    /// loop's thread, which is why it needs no synchronisation.
    /// </summary>
    public SessionState State { get; set; } = SessionState.AwaitingName;

    public TelnetSession(TcpClient client, ChannelWriter<Command> commands)
    {
        _client = client;
        _commands = commands;
        _outbound = Channel.CreateBounded<byte[]>(
            new BoundedChannelOptions(OutboundCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            }
        );
    }

    /// <summary>
    /// Terminates every line, not just the last. A screen arrives as one Send of many lines,
    /// and a client given a bare line feed mid-message staircases the rest of it.
    /// </summary>
    private static byte[] Encode(string message, bool terminate)
    {
        var normalised = message.Replace(LineTerminator, "\n").Replace('\r', '\n').Replace("\n", LineTerminator);

        return Encoding.UTF8.GetBytes(terminate ? normalised + LineTerminator : normalised);
    }

    public void Dispose()
    {
        _outbound.Writer.TryComplete();
        _client.Dispose();
    }

    /// <summary>
    /// Reads framed lines from the socket and publishes them as commands. Network faults are
    /// logged and swallowed: a dropped client is not an error for the server.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        PipeReader? reader = null;

        try
        {
            // GetStream throws once the client has been disposed, so it belongs inside the
            // guarded region rather than ahead of it.
            reader = PipeReader.Create(_client.GetStream(), new(leaveOpen: true));

            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                while (TelnetLineReader.TryReadLine(ref buffer, out var line))
                {
                    var text = TelnetLineReader.Decode(line);
                    await _commands.WriteAsync(new(this, text), cancellationToken);
                }

                // Read before advancing: the sequence must not be touched afterwards.
                var pending = buffer.Length;

                // Consumed stops at the last complete line; examined covers everything we
                // looked at, so a partial line is kept and we still wait for more bytes.
                reader.AdvanceTo(buffer.Start, buffer.End);

                if (pending > MaxLineLength)
                {
                    // A client this far past the cap with no line feed is broken or hostile,
                    // and discarding silently would leave it typing into a void.
                    _logger.Warning(
                        "Session {SessionId} sent {Pending} bytes with no line terminator, closing it",
                        Id,
                        pending
                    );

                    break;
                }

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a fault.
        }
        catch (IOException exception)
        {
            _logger.Debug(exception, "Session {SessionId} lost its connection while reading", Id);
        }
        catch (SocketException exception)
        {
            _logger.Debug(exception, "Session {SessionId} socket failed while reading", Id);
        }
        catch (ObjectDisposedException exception)
        {
            _logger.Debug(exception, "Session {SessionId} socket was already closed while reading", Id);
        }
        finally
        {
            if (reader is not null)
            {
                await reader.CompleteAsync();
            }

            _outbound.Writer.TryComplete();
        }
    }

    /// <summary>
    /// Queues a message for delivery. Never touches the socket: the writer loop owns it.
    /// When the queue is full the message is dropped, which keeps a slow client from
    /// stalling the game loop.
    /// </summary>
    public void Send(string message)
    {
        Queue(Encode(message, true));
    }

    /// <summary>
    /// Queues text without a line terminator, so the cursor stays on the same line. This is
    /// what a prompt needs: "Password: " and then the player types right there.
    /// </summary>
    public void SendPrompt(string prompt)
    {
        Queue(Encode(prompt, false));
    }

    /// <summary>
    /// Queues bytes exactly as given, with no encoding and no line handling. This is how
    /// telnet negotiation reaches the client: byte 255 is a command, not text.
    /// </summary>
    public void SendRaw(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        Queue(payload);
    }

    /// <summary>Asks the client to stop showing what the player types.</summary>
    public void HideInput()
    {
        SendRaw(TelnetProtocol.SuppressEcho());
    }

    /// <summary>Hands echoing back to the client.</summary>
    public void ShowInput()
    {
        SendRaw(TelnetProtocol.RestoreEcho());
    }

    private void Queue(byte[] payload)
    {
        if (!_outbound.Writer.TryWrite(payload))
        {
            _logger.Debug("Session {SessionId} is closing, dropped an outbound message", Id);
        }
    }

    /// <summary>Runs the inbound and outbound loops until both finish or cancellation arrives.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var inbound = RunAsync(linked.Token);
        var outbound = WriterLoopAsync(linked.Token);

        try
        {
            // Whichever loop finishes first tears the other one down. Cancelling here, rather
            // than after both have ended, is what makes it a safety net: it can still unblock
            // a loop parked on a live socket.
            await Task.WhenAny(inbound, outbound);
            await linked.CancelAsync();

            await Task.WhenAll(inbound, outbound);
        }
        finally
        {
            _logger.Debug("Session {SessionId} closed", Id);
        }
    }

    private async Task WriterLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            // GetStream throws once the client has been disposed, so it belongs inside the
            // guarded region rather than ahead of it.
            var stream = _client.GetStream();

            await foreach (var message in _outbound.Reader.ReadAllAsync(cancellationToken))
            {
                await stream.WriteAsync(message, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown, not a fault.
        }
        catch (IOException exception)
        {
            _logger.Debug(exception, "Session {SessionId} lost its connection while writing", Id);
        }
        catch (SocketException exception)
        {
            _logger.Debug(exception, "Session {SessionId} socket failed while writing", Id);
        }
        catch (ObjectDisposedException exception)
        {
            _logger.Debug(exception, "Session {SessionId} socket was already closed while writing", Id);
        }
    }
}
