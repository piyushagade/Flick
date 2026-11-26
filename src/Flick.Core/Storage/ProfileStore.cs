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

                    // Ensure newly added built-in gestures (e.g. builtin-tap, pinch) exist in older profiles.
                    var existingIds = new HashSet<string>(loaded.Gestures.Select(g => g.Id), StringComparer.Ordinal);
                    foreach (var builtIn in DefaultProfileFactory.CreateDefaultGestures())
                    {
                        if (existingIds.Add(builtIn.Id))
                        {
                            loaded.Gestures.Add(builtIn);
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
