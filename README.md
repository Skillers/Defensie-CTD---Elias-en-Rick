# Defensie CTD - Elias en Rick

A tactical terrain simulation and pathfinding system built in Unity. The project features procedurally generated terrain, real-time A* pathfinding for military unit movement, and a central terrain data store for querying height, slope, and biome per cell.

---

## Project Structure

```
Assets/
├── *.cs                  — Core gameplay scripts (pathfinding, units, rendering)
├── TerrainScripts/       — Terrain generation, biome definitions, slope analysis, data store
├── Scenes/               — Unity scene files
└── Settings/             — URP render pipeline configuration
```

---

## Scenes

| Scene | Description |
|---|---|
| `Main scene.unity` | Primary gameplay scene with terrain, units, and pathfinding |
| `TerrainTest.unity` | Testing scene for 3D Perlin noise + marching cubes terrain |
| `SampleScene.unity` | Default Unity template scene |

---

## Core Scripts (`Assets/`)

### Pathfinding & Units

**`AStarPathfinder.cs`**
8-directional A* pathfinder with diagonal movement cost (sqrt(2) x biome cost). Supports squad footprints (e.g. 5x5 tiles) and rejects paths where the squad doesn't fully fit. Accepts an optional `UnitTypeSO` to resolve per-biome movement costs.

**`UnitMover.cs`**
Moves a unit along a path from `AStarPathfinder`. Speed scales with biome movement cost. Automatically recalculates the path when the goal flag moves. References `TerrainDataStore` for all grid/world conversion and terrain queries.

**`UnitFacer.cs`**
Rotates the unit's visual to face its movement direction. Attached as a child of `UnitMover`.

**`MapRenderer.cs`**
Renders the biome grid as a single combined mesh using vertex colors from `BiomeSO.color`.

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

### Biome Definitions (ScriptableObjects)

**`BiomeSO.cs`** — `Create > Config > Biome`
Defines a biome type as a ScriptableObject asset. Fields:
- `biomeName` — display name
- `color` — vertex color used for rendering
- `defaultMovementCost` — base movement cost for all units
- `unitWeights[]` — optional per-unit-type movement cost overrides
- `GetMovementCost(UnitTypeSO)` — resolves the cost for a given unit type

**`UnitTypeSO.cs`** — `Create > Config > Unit Type`
Identity token for a unit type (e.g. Infantry, Vehicle, Helicopter). Referenced by `BiomeSO.unitWeights` for per-unit cost overrides.

**`BiomeCell`** (defined in `BiomeSO.cs`)
Simple wrapper holding a `BiomeSO` reference. Used in the terrain grid. Movement cost is always resolved dynamically via `biome.GetMovementCost(unitType)`.

---

### Terrain Analysis

**`PerlinNoisePlane.cs`**
Generates a 2D float array of terrain heights using Perlin noise. Fires `OnGenerated` when complete.

**`MarchingCubesTerrain.cs`**
Builds a 3D volumetric mesh from the height field using the marching cubes algorithm. Listens to `PerlinNoisePlane.OnGenerated`. Colors vertices using biome data from `TerrainDataStore`.

**`SlopeMap.cs`**
Computes slope angles per vertex using central-difference gradients. Exposes `GetSlope(x, z)` and `GetDirectionalSlope(x1,z1, x2,z2)`.

**`StartEndPoints.cs`**
Places a red sphere (start) and green sphere (end) on the terrain at reproducible seeded positions.

---

### Central Data Store

**`TerrainDataStore.cs`**
Single queryable source for all per-cell terrain data. Aggregates biome type and movement cost from the biome grid, height from `PerlinNoisePlane`, and slope from `SlopeMap`. Also provides grid/world coordinate conversion for all gameplay systems.

```csharp
// Query by world position
TerrainPoint data = terrainDataStore.GetData(worldPos, unitType);

// Query by grid coordinate
TerrainPoint data = terrainDataStore.GetData(gridX, gridZ, unitType);

// TerrainPoint fields:
data.biome          // BiomeSO
data.movementCost   // int (resolved for the given unit type)
data.height         // float (world units)
data.slopeDegrees   // float (0-90)
```

Set the biome grid via `terrainDataStore.SetGrid(biomeGrid)`. Fires `OnGridReady` when the grid is assigned.

---

## Data Flow

```
PlaneConfig (single source of settings)
    |-> PerlinNoisePlane (height field)
    |       |-> MarchingCubesTerrain (3D mesh)
    |       |-> SlopeMap (slope analysis)
    |       +-> StartEndPoints (path markers)
    |
    +-> [Biome generator] (produces BiomeCell[,] grid)
            +-> TerrainDataStore.SetGrid()
                    |-> MapRenderer (flat mesh with vertex colors)
                    +-> MarchingCubesTerrain (vertex coloring)

TerrainDataStore (central aggregator)
    |- holds: BiomeCell[,] grid -> biome, movementCost
    |- reads: PerlinNoisePlane  -> height
    +- reads: SlopeMap          -> slopeDegrees

AStarPathfinder (path from A to B, uses biome movement cost)
    +-> UnitMover (smooth movement via TerrainDataStore)
            +-> UnitFacer (unit rotation)
```

---

## Technology

- **Unity** with URP (Universal Render Pipeline)
- **Procedural generation**: Perlin noise, marching cubes
- **Data-driven biomes**: ScriptableObject-based biome and unit type definitions
- **Pathfinding**: Custom A* with squad footprint support and per-unit-type costs
- **Rendering**: Runtime mesh generation with vertex color baking
