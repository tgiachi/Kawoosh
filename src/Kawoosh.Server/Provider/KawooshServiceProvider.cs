using Jab;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Networking;
using Kawoosh.Server.Screens;
using Kawoosh.Server.Services;
using Kawoosh.SGW.Interfaces;
using Kawoosh.SGW.Services;

namespace Kawoosh.Server.Provider;

/// <summary>
/// The server's service graph. Everything with state or a socket is a singleton: a second
/// listener would bind a second socket, and a second game loop would be a second world. The
/// parser is stateless, so a new one per resolution costs nothing. Screens are singletons
/// too, but for the opposite reason: they hold no per-session state, since everything that
/// differs per player lives on the session the context carries.
/// </summary>
[ServiceProvider,
 Transient(typeof(ISGWFileParser), typeof(SGWFileParser)),
 Singleton(typeof(IVariableService), typeof(VariableService)),
 Singleton(typeof(IScreenService), typeof(ScreenService)),
 Singleton(typeof(IScreenManager), typeof(ScreenManager)),
 Singleton(typeof(IScreen), typeof(GreetingScreen)),
 Singleton(typeof(IScreen), typeof(NameScreen)),
 Singleton(typeof(IScreen), typeof(PasswordScreen)),
 Singleton(typeof(IScreen), typeof(WorldScreen)),
 Singleton(typeof(IMessageService), typeof(MessageService)),
 Singleton(typeof(IGameLoopService), typeof(GameLoopService)),
 Singleton(typeof(ISessionInputRouter), typeof(SessionInputRouter)),
 Singleton(typeof(ITelnetListener), typeof(TelnetListener))
]
public partial class KawooshServiceProvider { }
