namespace Kawoosh.Server.Data.Commands;

/// <summary>
/// Base of the closed set of commands the game loop processes. Every command carries its own
/// payload as typed members, so a handler never casts and the compiler catches a switch that
/// forgets a case. Add a case by adding a record here, not by adding a discriminator value.
/// </summary>
public abstract record GameLoopCommand;
