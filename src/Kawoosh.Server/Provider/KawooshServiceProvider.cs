using Jab;
using Kawoosh.Server.Networking;
using Kawoosh.SGW.Interfaces;
using Kawoosh.SGW.Services;

namespace Kawoosh.Server.Provider;

[ServiceProvider,
 Transient(typeof(ISGWFileParser), typeof(SGWFileParser)),
 Transient(typeof(TelnetListener))
]
public partial class KawooshServiceProvider { }
