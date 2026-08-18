using System.Diagnostics.CodeAnalysis;
using Serilog;
using Kawoosh.Server.Interfaces;

namespace Kawoosh.Server.Services;

/// <summary>
/// Every screen, by name. The dictionary is built once in the constructor, so two screens
/// sharing a name fail the start rather than the first switch that happens to hit them.
/// </summary>
public sealed class ScreenManager : IScreenManager
{
    private readonly ILogger _logger = Log.ForContext<ScreenManager>();
    private readonly Dictionary<string, IScreen> _screens;

    public ScreenManager(IEnumerable<IScreen> screens)
    {
        _screens = screens.ToDictionary(screen => screen.Name, StringComparer.OrdinalIgnoreCase);

        _logger.Information("Registered {ScreenCount} screens: {ScreenNames}", _screens.Count, _screens.Keys);
    }

    public IReadOnlyCollection<string> ScreenNames => _screens.Keys;

    public bool TryGetScreen(string name, [MaybeNullWhen(false)] out IScreen screen)
    {
        return _screens.TryGetValue(name, out screen);
    }
}
