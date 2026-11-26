namespace Flick.Core.Models;

/// <summary>
/// Represents a user-defined application scope in the sidebar (similar to GestureSign's app profiles).
/// </summary>
public sealed class AppScope
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string ProcessFilter { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
