# Defensie CTD - Elias en Rick

A tactical terrain simulation and pathfinding system built in Unity. The project features procedurally generated terrain, real-time A* pathfinding for military unit movement, and a central terrain data store for querying height, slope, and terrain type per cell.

---

## Project Structure

```
Assets/
├── *.cs                  — Core gameplay scripts (pathfinding, units, terrain grid)
├── TerrainScripts/       — Terrain generation, biome maps, slope analysis, data store
├── Scenes/               — Unity scene files
└── Settings/             — URP render pipeline configuration
```

---

## Scenes

| Scene | Description |
|---|---|
| `Main scene.unity` | Primary gameplay scene with Voronoi terrain, units, and pathfinding |
| `TerrainTest.unity` | Testing scene for 3D Perlin noise + marching cubes terrain |
| `SampleScene.unity` | Default Unity template scene |

---

## Core Scripts (`Assets/`)

### Terrain Grid

**`TerrainCell.cs`**
Data model for a single terrain cell. Defines three terrain types with movement costs:
- `Grass` — cost 2
- `Dirt` — cost 3
- `Sand` — cost 5

**`VoronoiMap.cs`**
Generates a terrain grid using a Voronoi diagram. Grid size is derived from `PlaneConfig`. Regions are placed on a jittered grid controlled by `regionsX` and `regionsZ` — e.g. `regionsX=10, regionsZ=5` creates 50 regions. Provides coordinate conversion between grid and world space.

**`MapRenderer.cs`**
Renders the terrain grid as a single combined mesh. Uses vertex colors to encode terrain type. Accepts both `TerrainCell[,]` and `MilitaryTerrainCell[,]` grids.

---

### Pathfinding & Units

**`AStarPathfinder.cs`**
8-directional A* pathfinder with diagonal movement cost (√2 × terrain cost). Supports squad footprints (e.g. 5×5 tiles) and rejects paths where the squad doesn't fully fit.

**`UnitMover.cs`**
Moves a unit along a path from `AStarPathfinder`. Speed scales with terrain cost. Automatically recalculates the path when the goal flag moves. Draws the current path as a yellow line via `LineRenderer`.

**`UnitFacer.cs`**
Rotates the unit's visual to face its movement direction. Attached as a child of `UnitMover`.

---

### Demo-only Scripts (not part of the main game)

**`CameraController.cs`**, **`FlagMover.cs`**, **`ObstaclePlacer.cs`**
Built for the old demo scene. Not part of the main game.

---

## Terrain Scripts (`Assets/TerrainScripts/`)

### Configuration

**`PlaneConfig.cs` / `PlaneConfig.asset`**
ScriptableObject holding all terrain generation parameters: extents (X, Z), noise scale, seed, and height multiplier. All terrain systems read from this single asset via `TerrainConfigHolder.cs`.

---

### Military Terrain Types

**`MilitaryTerrainCell.cs`**
Defines `MilitaryTerrainType` and `MilitaryTerrainCell` used by all biome map scripts. Movement costs per type:

| Type | Cost | Description |
|---|---|---|
| `Grass` | 3 | Open grassy area |
| `GrassyPlain` | 3 | Wide flat grass |
| `Snow` | 5 | Flat snow |
| `SnowyHill` | 8 | Elevated snow |
| `Mud` | 6 | Wet and slow |
| `MuddyMountain` | 9 | Steep and wet |
| `DenseForest` | 7 | Dense vegetation |
| `RockyTerrain` | 7 | Uneven rocky ground |

---

### Biome Maps (pick one per scene)

Both scripts read grid size from `PlaneConfig` (`extentX * 2`, `extentZ * 2`) and seed from `config.seed`. Both fire `OnGenerated` when done — `MarchingCubesTerrain` listens to this to recolor its 3D mesh with terrain type colors.

**`BiomeVoronoiMap.cs`** — Option A
Voronoi map using military terrain types. Seeds are placed on a jittered grid controlled by `regionsX` and `regionsZ`. Each region gets a randomly assigned `MilitaryTerrainType`.

**`NoiseBiomeMap.cs`** — Option B
Two Perlin noise maps (height + moisture) are combined to determine terrain type per cell. Thresholds are configurable in the Inspector.

```
Height \ Moisture |  Dry          |  Moderate     |  Wet
──────────────────┼───────────────┼───────────────┼──────────────
High (mountain)   | SnowyHill     | MuddyMountain | MuddyMountain
Mid  (hills)      | RockyTerrain  | GrassyPlain   | Mud
Low  (plains)     | GrassyPlain   | Grass         | DenseForest
```

---

### Terrain Analysis

**`PerlinNoisePlane.cs`**
Generates a 2D float array of terrain heights using Perlin noise. Fires `OnGenerated` when complete.

**`MarchingCubesTerrain.cs`**
Builds a 3D volumetric mesh from the height field using the marching cubes algorithm. Listens to `PerlinNoisePlane.OnGenerated`.

**`SlopeMap.cs`**
Computes slope angles per vertex using central-difference gradients. Exposes `GetSlope(x, z)` and `GetDirectionalSlope(x1,z1, x2,z2)`. Generates a white-to-red visualization mesh.

**`StartEndPoints.cs`**
Places a red sphere (start) and green sphere (end) on the terrain at reproducible seeded positions.

---

### Central Data Store

**`TerrainDataStore.cs`**
Single queryable source for all per-cell terrain data. Aggregates terrain type and movement cost from the active biome map, height from `PerlinNoisePlane`, and slope from `SlopeMap`.

```csharp
// Query by world position
TerrainPoint data = terrainDataStore.GetData(worldPos);

// Query by grid coordinate
TerrainPoint data = terrainDataStore.GetData(gridX, gridZ);

// TerrainPoint fields:
data.terrainType    // MilitaryTerrainType
data.movementCost   // int
data.height         // float (world units)
data.slopeDegrees   // float (0–90°)
```

Assign `biomeMap` to whichever of the three biome map scripts is active. Rebuilds automatically when `PerlinNoisePlane` fires `OnGenerated`.

---

## Data Flow

```
PlaneConfig (single source of settings)
    ├─> PerlinNoisePlane (height field)
    │       ├─> MarchingCubesTerrain (3D mesh)
    │       ├─> SlopeMap (slope analysis)
    │       └─> StartEndPoints (path markers)
    │
    └─> BiomeVoronoiMap / NoiseBiomeMap (terrain type grid)
            └─> MapRenderer (flat mesh with vertex colors)

TerrainDataStore
    ├─ reads: active biome map  → terrainType, movementCost
    ├─ reads: PerlinNoisePlane  → height
    └─ reads: SlopeMap          → slopeDegrees

AStarPathfinder (path from A to B, uses movementCost)
    └─> UnitMover (smooth movement)
            └─> UnitFacer (unit rotation)
```

---

## Technology

- **Unity** with URP (Universal Render Pipeline)
- **Procedural generation**: Voronoi diagrams, Perlin noise, marching cubes
- **Pathfinding**: Custom A* with squad footprint support
- **Rendering**: Runtime mesh generation with vertex color baking
