using Jab;
using Kawoosh.SGW.Interfaces;
using Kawoosh.SGW.Services;

namespace Kawoosh.Server.Provider;

[ServiceProvider, Transient(typeof(ISGWFileParser), typeof(SGWFileParser))]
public partial class KawooshServiceProvider { }
