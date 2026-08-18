using System.Diagnostics.CodeAnalysis;
using Serilog;
using Kawoosh.Server.Data.Screens;
using Kawoosh.Server.Exceptions;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Internal;

namespace Kawoosh.Server.Services;

/// <summary>
/// Owns the screen directory. A load either replaces every screen or changes nothing, so a
/// broken edit on a live server cannot leave the game half-served.
/// </summary>
public sealed class ScreenService : IScreenService
{
    private const string ScreenSearchPattern = "*.sgs";

    private readonly ILogger _logger = Log.ForContext<ScreenService>();
    private readonly IVariableService _variables;

    private Dictionary<string, Screen> _screens = new(StringComparer.OrdinalIgnoreCase);
    private string? _directoryPath;
    private IReadOnlyCollection<string> _requiredScreens = [];

    public ScreenService(IVariableService variables)
    {
        _variables = variables;
    }

    public IReadOnlyCollection<string> ScreenNames => _screens.Keys;

    public void Load(string directoryPath, IReadOnlyCollection<string> requiredScreens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(requiredScreens);

        var loaded = ReadDirectory(directoryPath, requiredScreens);

        _screens = loaded;
        _directoryPath = directoryPath;
        _requiredScreens = requiredScreens;

        _logger.Information("Loaded {ScreenCount} screens from {DirectoryPath}", loaded.Count, directoryPath);
    }

    public void Reload()
    {
        var directoryPath = _directoryPath ??
                            throw new InvalidOperationException("Screens must be loaded before they can be reloaded.");

        // Built fully before anything is swapped: a throw here leaves the old set serving.
        var loaded = ReadDirectory(directoryPath, _requiredScreens);

        _screens = loaded;

        _logger.Information("Reloaded {ScreenCount} screens from {DirectoryPath}", loaded.Count, directoryPath);
    }

    public bool TryGetScreen(string name, [MaybeNullWhen(false)] out Screen screen)
    {
        return _screens.TryGetValue(name, out screen);
    }

    public string Render(string name)
    {
        if (!_screens.TryGetValue(name, out var screen))
        {
            throw new KeyNotFoundException($"No screen named '{name}' is loaded.");
        }

        return _variables.TranslateText(screen.Body);
    }

    private static Dictionary<string, Screen> ReadDirectory(
        string directoryPath,
        IReadOnlyCollection<string> requiredScreens
    )
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Screen directory not found: {directoryPath}");
        }

        var screens = new Dictionary<string, Screen>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        var files = Directory
            .EnumerateFiles(directoryPath, ScreenSearchPattern)
            .OrderBy(file => file, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var name = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();

            screens[name] = ScreenParser.Parse(name, File.ReadAllText(file), fileName, problems);
        }

        foreach (var required in requiredScreens.Where(required => !screens.ContainsKey(required)))
        {
            problems.Add($"{directoryPath}:0: required screen '{required}' is missing");
        }

        if (problems.Count > 0)
        {
            throw new ScreenLoadException(problems);
        }

        return screens;
    }
}
