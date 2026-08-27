using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Lightweight, side-effect-free reader for level save files. Use from scenes that
/// only need to display saved data (e.g. results screen) — does not touch
/// <see cref="TerrainDataStore"/> or fire any events.
/// </summary>
public static class SaveFileLoader
{
    /// <summary>Loads a save from persistentDataPath. Null on any failure; the cause is logged.</summary>
    public static SaveData LoadSave(string fileName)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            Debug.LogError("SaveFileLoader.LoadSave: fileName is empty.");
            return null;
        }

        string path = Path.Combine(Application.persistentDataPath, fileName);
        if (!File.Exists(path))
        {
            Debug.LogError($"SaveFileLoader.LoadSave: file not found at {path}.");
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<SaveData>(json);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"SaveFileLoader.LoadSave failed: {e.Message}");
            return null;
        }
    }

    /// <summary>Name → BiomeSO map from Resources/Biomes, mirroring TerrainDataStore's lookup.</summary>
    public static IReadOnlyDictionary<string, BiomeSO> LoadBiomeLookup()
    {
        Dictionary<string, BiomeSO> map = new Dictionary<string, BiomeSO>();
        foreach (BiomeSO b in Resources.LoadAll<BiomeSO>("Biomes"))
        {
            if (b != null && !string.IsNullOrEmpty(b.biomeName))
                map[b.biomeName] = b;
        }
        return map;
    }
}
