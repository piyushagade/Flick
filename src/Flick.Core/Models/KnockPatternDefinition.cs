namespace Flick.Core.Models;

/// <summary>
/// A calibrated acoustic surface profile (e.g. Touchpad, Laptop Lid, Desk)
/// storing normalized spectral/temporal feature vectors from sample knocks.
/// </summary>
public sealed class KnockSurfaceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Feature vectors recorded during calibration (each vector has length 16).
    /// </summary>
    public List<float[]> SampleFeatures { get; set; } = [];
}

/// <summary>
/// Definition of a knock pattern trigger (knock count, optional rhythm ratios, and target surface).
/// </summary>
public sealed class KnockPatternDefinition
{
    /// <summary>
    /// Number of knocks required (e.g. 2 = double knock, 3 = triple knock).
    /// </summary>
    public int KnockCount { get; set; } = 2;

    /// <summary>
    /// Optional normalized inter-knock interval ratios (e.g. [1.0, 2.0] for knock-knock ... knock).
    /// Empty means any even or uneven cadence with <see cref="KnockCount"/> knocks matches.
    /// </summary>
    public List<double> RhythmRatios { get; set; } = [];

    /// <summary>
    /// Optional <see cref="KnockSurfaceProfile.Id"/>. Empty/null means any surface matches.
    /// </summary>
    public string? SurfaceProfileId { get; set; }

    /// <summary>
    /// Display name of the surface (e.g. "Touchpad", "Laptop Lid", "Desk", or "Any Surface").
    /// </summary>
    public string SurfaceName { get; set; } = "Any Surface";
}
