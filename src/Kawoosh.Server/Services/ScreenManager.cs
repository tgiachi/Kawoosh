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

        // An empty name never resolves, per TryGetScreen's contract; a screen registered
        // under one anyway would hijack every session that has not yet switched anywhere.
        if (_screens.ContainsKey(""))
        {
            throw new ArgumentException("A screen cannot register under an empty name.", nameof(screens));
        }

        _logger.Information("Registered {ScreenCount} screens: {ScreenNames}", _screens.Count, _screens.Keys);
    }

    public IReadOnlyCollection<string> ScreenNames => _screens.Keys;

    public bool TryGetScreen(string name, [MaybeNullWhen(false)] out IScreen screen)
    {
        return _screens.TryGetValue(name, out screen);
    }
}
