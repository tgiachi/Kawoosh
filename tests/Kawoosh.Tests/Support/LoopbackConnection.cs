using System.Net;
using System.Net.Sockets;

namespace Kawoosh.Tests.Support;

/// <summary>
/// A real connected TCP pair on an ephemeral loopback port: <see cref="Client" /> is the
/// end a MUD player would hold, <see cref="Server" /> is the end a session reads from.
/// </summary>
public sealed class LoopbackConnection : IDisposable
{
    private readonly TcpListener _listener;

    public TcpClient Client { get; }
    public TcpClient Server { get; }

    public LoopbackConnection()
    {
        _listener = new(IPAddress.Loopback, 0);
        _listener.Start();

        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        Client = new();
        Client.Connect(IPAddress.Loopback, port);
        Server = _listener.AcceptTcpClient();
    }

    public void Dispose()
    {
        Client.Dispose();
        Server.Dispose();
        _listener.Stop();
    }

    /// <summary>Writes raw bytes as the player's client would.</summary>
    public void SendRaw(params byte[] payload)
    {
        Client.GetStream().Write(payload, 0, payload.Length);
        Client.GetStream().Flush();
    }
}
