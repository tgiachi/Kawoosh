using Kawoosh.Server.Data.Screens;
using Kawoosh.Server.Interfaces;

namespace Kawoosh.Tests.Support;

/// <summary>
/// A screen that says which hook ran by sending it to the client. Written to the socket
/// rather than to a list, because the socket is already how these tests synchronise with the
/// loop's thread — a list would need a wait of its own.
/// </summary>
public sealed class RecordingScreen : IScreen
{
    public string Name { get; }

    /// <summary>Extra behaviour for OnEnter, run after the marker is sent.</summary>
    public Action<ScreenContext>? OnEnterAction { get; set; }

    /// <summary>Extra behaviour for OnInput, run after the marker is sent.</summary>
    public Action<ScreenContext, string>? OnInputAction { get; set; }

    /// <summary>Extra behaviour for OnExit, run after the marker is sent.</summary>
    public Action<ScreenContext>? OnExitAction { get; set; }

    public RecordingScreen(string name)
    {
        Name = name;
    }

    public void OnEnter(ScreenContext context)
    {
        context.Session.Send($"{Name}:enter");
        OnEnterAction?.Invoke(context);
    }

    public void OnInput(ScreenContext context, string line)
    {
        context.Session.Send($"{Name}:input:{line}");
        OnInputAction?.Invoke(context, line);
    }

    public void OnExit(ScreenContext context)
    {
        context.Session.Send($"{Name}:exit");
        OnExitAction?.Invoke(context);
    }
}
