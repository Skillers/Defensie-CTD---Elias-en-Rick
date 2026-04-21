using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[CustomEditor(typeof(BiomeSO))]
public class BiomeSOEditor : Editor
{
    private const string UnitTypeFolder = "Assets/UnitTypes";

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var biome = (BiomeSO)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Sync Unit Types from folder"))
            SyncUnitTypes(biome);
    }

    private void OnEnable()
    {
        SyncUnitTypes((BiomeSO)target);
    }

    private void SyncUnitTypes(BiomeSO biome)
    {
        var guids = AssetDatabase.FindAssets("t:UnitTypeSO", new[] { UnitTypeFolder });
        var allTypes = guids
            .Select(g => AssetDatabase.LoadAssetAtPath<UnitTypeSO>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(u => u != null)
            .ToList();

        if (allTypes.Count == 0) return;

        // Build lookup of existing costs
        var existing = new Dictionary<UnitTypeSO, int>();
        if (biome.unitWeights != null)
        {
            foreach (var w in biome.unitWeights)
            {
                if (w.unitType != null && !existing.ContainsKey(w.unitType))
                    existing[w.unitType] = w.movementCost;
            }
        }

        // Check if already in sync
        bool inSync = biome.unitWeights != null
            && biome.unitWeights.Length == allTypes.Count
            && biome.unitWeights.All(w => w.unitType != null && allTypes.Contains(w.unitType));

        if (inSync) return;

        // Rebuild: keep existing costs, default new entries to defaultMovementCost
        Undo.RecordObject(biome, "Sync Unit Types");

        biome.unitWeights = allTypes.Select(ut => new UnitWeight
        {
            unitType = ut,
            movementCost = existing.TryGetValue(ut, out int cost) ? cost : biome.defaultMovementCost
        }).ToArray();

        EditorUtility.SetDirty(biome);
    }
}
