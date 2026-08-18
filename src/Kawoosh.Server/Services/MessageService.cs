using System.Diagnostics.CodeAnalysis;
using Serilog;
using Kawoosh.Server.Exceptions;
using Kawoosh.Server.Interfaces;
using Kawoosh.Server.Internal;

namespace Kawoosh.Server.Services;

/// <summary>
/// Owns the message directory. A load either replaces every message or changes nothing, so a
/// broken edit on a live server cannot leave half the game speechless.
/// </summary>
public sealed class MessageService : IMessageService
{
    private const string MessageSearchPattern = "*.sgm";

    private readonly ILogger _logger = Log.ForContext<MessageService>();
    private readonly IVariableService _variables;

    private Dictionary<string, string> _messages = new(StringComparer.OrdinalIgnoreCase);
    private string? _directoryPath;
    private IReadOnlyCollection<string> _requiredKeys = [];

    public MessageService(IVariableService variables)
    {
        _variables = variables;
    }

    public IReadOnlyCollection<string> MessageKeys => _messages.Keys;

    public void Load(string directoryPath, IReadOnlyCollection<string> requiredKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(requiredKeys);

        var loaded = ReadDirectory(directoryPath, requiredKeys);

        _messages = loaded;
        _directoryPath = directoryPath;
        _requiredKeys = requiredKeys;

        _logger.Information("Loaded {MessageCount} messages from {DirectoryPath}", loaded.Count, directoryPath);
    }

    public void Reload()
    {
        var directoryPath = _directoryPath ??
                            throw new InvalidOperationException("Messages must be loaded before they can be reloaded.");

        // Built fully before anything is swapped: a throw here leaves the old set serving.
        var loaded = ReadDirectory(directoryPath, _requiredKeys);

        _messages = loaded;

        _logger.Information("Reloaded {MessageCount} messages from {DirectoryPath}", loaded.Count, directoryPath);
    }

    public bool TryGetTemplate(string key, [MaybeNullWhen(false)] out string template)
    {
        return _messages.TryGetValue(key, out template);
    }

    public string Render(string key, params (string Name, object Value)[] arguments)
    {
        if (!_messages.TryGetValue(key, out var template))
        {
            throw new KeyNotFoundException($"No message with key '{key}' is loaded.");
        }

        return _variables.TranslateText(template, arguments);
    }

    private static Dictionary<string, string> ReadDirectory(
        string directoryPath,
        IReadOnlyCollection<string> requiredKeys
    )
    {
        if (!Directory.Exists(directoryPath))
        {
            throw new DirectoryNotFoundException($"Message directory not found: {directoryPath}");
        }

        var messages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var problems = new List<string>();

        // Ordered so a duplicate across two files always names the same one as the winner.
        var files = Directory
            .EnumerateFiles(directoryPath, MessageSearchPattern)
            .OrderBy(file => file, StringComparer.Ordinal);

        foreach (var file in files)
        {
            MessageParser.Parse(File.ReadAllText(file), Path.GetFileName(file), messages, problems);
        }

        foreach (var required in requiredKeys.Where(required => !messages.ContainsKey(required)))
        {
            problems.Add($"{directoryPath}:0: required message '{required}' is missing");
        }

        if (problems.Count > 0)
        {
            throw new ContentLoadException(problems);
        }

        return messages;
    }
}
