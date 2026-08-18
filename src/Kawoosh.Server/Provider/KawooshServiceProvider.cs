using Jab;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Services;
using Kawoosh.SGW.Interfaces;
using Kawoosh.SGW.Services;

namespace Kawoosh.Server.Provider;

/// <summary>
/// The server's service graph. Everything with state or a socket is a singleton: a second
/// listener would bind a second socket, and a second game loop would be a second world.
/// The parser is stateless, so a new one per resolution costs nothing.
/// </summary>
[ServiceProvider,
 Transient(typeof(ISGWFileParser), typeof(SGWFileParser)),
 Singleton(typeof(IVariableService), typeof(VariableService)),
 Singleton(typeof(IScreenService), typeof(ScreenService)),
 Singleton(typeof(IMessageService), typeof(MessageService)),
 Singleton(typeof(ISessionFlowService), typeof(SessionFlowService)),
 Singleton(typeof(IGameLoopService), typeof(GameLoopService)),
 Singleton(typeof(ISessionInputRouter), typeof(SessionInputRouter)),
 Singleton(typeof(ITelnetListener), typeof(TelnetListener))
]
public partial class KawooshServiceProvider { }
