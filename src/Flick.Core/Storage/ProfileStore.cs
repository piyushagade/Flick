using System.Text.Json;
using System.Text.Json.Serialization;
using Flick.Core.Models;

namespace Flick.Core.Storage;

/// <summary>
/// Persists and loads <see cref="FlickProfile"/> as JSON in LocalAppData.
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ProfilePath { get; }

    public ProfileStore(string? profilePath = null)
    {
        if (!string.IsNullOrWhiteSpace(profilePath))
        {
            ProfilePath = profilePath;
        }
        else
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            ProfilePath = Path.Combine(appData, "Flick", "profile.json");
        }
    }

    public FlickProfile Load()
    {
        try
        {
            if (File.Exists(ProfilePath))
            {
                string json = File.ReadAllText(ProfilePath);
                var loaded = JsonSerializer.Deserialize<FlickProfile>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.Scopes ??= [];
                    loaded.Gestures ??= [];
                    loaded.Rules ??= [];
                    loaded.Settings ??= new AppSettings();
                    loaded.Settings.HotCornerActions ??= [];
                    loaded.Settings.ScopeTrailColors ??= [];

                    // Ensure newly added built-in gestures (e.g. builtin-tap, pinch) exist and have up-to-date multi-stroke previews in older profiles.
                    var existingById = new Dictionary<string, GestureDefinition>(StringComparer.Ordinal);
                    foreach (var g in loaded.Gestures)
                    {
                        existingById.TryAdd(g.Id, g);
                    }

                    foreach (var builtIn in DefaultProfileFactory.CreateDefaultGestures())
                    {
                        if (!existingById.TryGetValue(builtIn.Id, out var existing))
                        {
                            loaded.Gestures.Add(builtIn);
                        }
                        else if (existing.IsBuiltIn &&
                                 builtIn.FingerStrokes is { Count: > 1 } &&
                                 (existing.FingerStrokes is null || existing.FingerStrokes.Count <= 1))
                        {
                            existing.FingerStrokes = builtIn.FingerStrokes;
                        }
                    }

                    return loaded;
                }
            }
        }
        catch
        {
            // Fall back to default profile if corrupted.
        }

        var defaults = DefaultProfileFactory.CreateDefault();
        Save(defaults);
        return defaults;
    }

    public void Save(FlickProfile profile)
    {
        string? dir = Path.GetDirectoryName(ProfilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempPath = ProfilePath + ".tmp";
        string json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, ProfilePath, overwrite: true);
    }
}
