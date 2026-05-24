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

        // Build lookup of existing per-unit specs so the sync preserves any tuning
        // already done in the inspector.
        var existing = new Dictionary<UnitTypeSO, CellEffectSpec>();
        if (biome.unitEffects != null)
        {
            foreach (var e in biome.unitEffects)
            {
                if (e.unitType != null && !existing.ContainsKey(e.unitType))
                    existing[e.unitType] = new CellEffectSpec
                    {
                        effect         = e.effect,
                        costMultiplier = e.costMultiplier,
                    };
            }
        }

        // Check if already in sync
        bool inSync = biome.unitEffects != null
            && biome.unitEffects.Length == allTypes.Count
            && biome.unitEffects.All(e => e.unitType != null && allTypes.Contains(e.unitType));

        if (inSync) return;

        // Rebuild: keep existing specs, default new entries to the biome's defaultEffect
        // so syncing is behaviour-neutral until the designer tweaks the new row.
        Undo.RecordObject(biome, "Sync Unit Types");

        biome.unitEffects = allTypes.Select(ut =>
        {
            CellEffectSpec spec = existing.TryGetValue(ut, out var e) ? e : biome.defaultEffect;
            return new CellUnitEffect
            {
                unitType       = ut,
                effect         = spec.effect,
                costMultiplier = spec.costMultiplier,
            };
        }).ToArray();

        EditorUtility.SetDirty(biome);
    }
}
