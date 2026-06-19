namespace Tagster.Tests;

/// <summary>A deterministic <see cref="TimeProvider"/> for tests.</summary>
internal sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
{
    private DateTimeOffset _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now = _now.Add(delta);
}
