namespace Kawoosh.Server.Data.Commands;

/// <summary>
/// The world pulse, as handlers receive it. Its times are measured when the pulse fires, not
/// when it was scheduled, so a pulse that arrives late reports the time that really passed
/// and simulation stays proportional to it rather than to the number of pulses.
/// </summary>
/// <param name="PulseNumber">Monotonic count, starting at 1.</param>
/// <param name="Uptime">Time since the loop started.</param>
/// <param name="SincePrevious">Time since the previous pulse; the first pulse reports its own delay.</param>
public sealed record WorldTickCommand(long PulseNumber, TimeSpan Uptime, TimeSpan SincePrevious) : GameLoopCommand;
