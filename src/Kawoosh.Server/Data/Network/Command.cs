using Kawoosh.Server.Networking;

namespace Kawoosh.Server.Data.Network;

/// <summary>
/// One complete line of input read from a session, carried to whatever consumes the
/// shared command channel.
/// </summary>
public class Command
{
    public TelnetSession Session { get; }
    public string Text { get; }

    public Command(TelnetSession session, string text)
    {
        Session = session;
        Text = text;
    }

    public override string ToString()
        => $"Command(Session: {Session.Id}, Text: {Text})";
}
