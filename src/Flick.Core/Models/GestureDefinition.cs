namespace Flick.Core.Models;

/// <summary>
/// Defines a named gesture template as an ordered sequence of 2D points.
/// </summary>
public sealed class GestureDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<GesturePoint> Points { get; set; } = [];
    public List<List<GesturePoint>>? FingerStrokes { get; set; }
    public bool IsBuiltIn { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
