namespace Kawoosh.Server.Data.Text;

/// <summary>
/// One piece of output and how long to wait before it.
/// </summary>
/// <param name="DelayMilliseconds">Wait after the previous step, before this one is sent.</param>
/// <param name="Text">What to send.</param>
/// <param name="Terminate">Whether to end the line, or leave the cursor where it is.</param>
public readonly record struct TextStep(int DelayMilliseconds, string Text, bool Terminate);
