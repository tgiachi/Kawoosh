using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;
using Serilog;
using Kawoosh.Server.Data.Network;
using Kawoosh.Server.Data.World;
using Kawoosh.Server.Networking.Internal;

namespace Kawoosh.Server.Networking;

/// <summary>
/// One connected telnet client. Runs an inbound loop that frames lines into commands on the
/// shared channel, and an outbound loop that drains a per-session queue onto the socket.
/// </summary>
public sealed class TelnetSession : IDisposable
{
    private const int OutboundCapacity = 64;
    private const string LineTerminator = "\r\n";

    private readonly ILogger _logger = Log.ForContext<TelnetSession>();
    private readonly TcpClient _client;
    private readonly ChannelWriter<Command> _commands;
    private readonly Channel<string> _outbound;

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>The logged-in character, null until the session authenticates.</summary>
    public Character? Character { get; set; }

    public TelnetSession(TcpClient client, ChannelWriter<Command> commands)
    {
        _client = client;
        _commands = commands;
        _outbound = Channel.CreateBounded<string>(
            new BoundedChannelOptions(OutboundCapacity)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true
            }
        );
    }

    /// <summary>Runs the inbound and outbound loops until both finish or cancellation arrives.</summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var inbound = RunAsync(linked.Token);
        var outbound = WriterLoopAsync(linked.Token);

        try
        {
            await Task.WhenAll(inbound, outbound);
        }
        finally
        {
            await linked.CancelAsync();
            _logger.Debug("Session {SessionId} closed", Id);
        }
    }

    /// <summary>
    /// Reads framed lines from the socket and publishes them as commands. Network faults are
    /// logged and swallowed: a dropped client is not an error for the server.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var reader = PipeReader.Create(_client.GetStream());

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var result = await reader.ReadAsync(cancellationToken);
                var buffer = result.Buffer;

                while (TelnetLineReader.TryReadLine(ref buffer, out var line))
                {
                    var text = TelnetLineReader.Decode(line);
                    await _commands.WriteAsync(new Command(this, text), cancellationToken);
                }

                // Consumed stops at the last complete line; examined covers everything we
                // looked at, so a partial line is kept and we still wait for more bytes.
                reader.AdvanceTo(buffer.Start, buffer.End);

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
        finally
        {
            await reader.CompleteAsync();
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
        if (!_outbound.Writer.TryWrite(message))
        {
            _logger.Debug("Session {SessionId} is closing, dropped an outbound message", Id);
        }
    }

    private async Task WriterLoopAsync(CancellationToken cancellationToken)
    {
        var stream = _client.GetStream();

        try
        {
            await foreach (var message in _outbound.Reader.ReadAllAsync(cancellationToken))
            {
                var payload = Encoding.UTF8.GetBytes(message + LineTerminator);

                await stream.WriteAsync(payload, cancellationToken);
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
    }

    public void Dispose()
    {
        _outbound.Writer.TryComplete();
        _client.Dispose();
    }
}
