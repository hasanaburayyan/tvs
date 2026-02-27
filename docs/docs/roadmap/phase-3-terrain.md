---
sidebar_position: 5
title: "Phase 3: Terrain + Trenches"
---

# Phase 3 -- Terrain + Trenches (Weeks 6--7)

**Goal:** Add meaningful terrain that affects gameplay -- trenches provide cover, tunnels enable hidden movement.

**Dependencies:** [Phase 2](./phase-2-squad-command.md) -- the combat tick must be running so cover and line-of-sight matter.

---

## Backend Tasks (3A)

### Tables

**TerrainFeature**
- `ulong` auto-increment primary key
- `ulong` game session ID (indexed)
- Type enum: `Trench`, `Tunnel`, `Bunker`, `OpenField`
- Bounding region: min/max x, y, z (axis-aligned box)
- `bool` passable (some features may block movement)
- `bool` temporary (for magic-created terrain in Phase 4)

**TunnelLink**
- `ulong` primary key
- `ulong` entrance feature ID
- `ulong` exit feature ID
- `float` travel time in seconds

### Cover system

Modify the game tick's damage resolution:

1. When resolving an attack, check if the target unit's position is inside any `TerrainFeature` of type `Trench` or `Bunker`
2. If in cover, reduce hit chance by a configurable percentage (e.g. 50% reduction)
3. Add an `InCover` flag to the `Unit` table for client rendering

### Tunnel system

- When a unit is ordered to move to a tunnel entrance, set a `InTunnel` flag and a travel timer
- While `InTunnel` is true, the unit is invisible to enemies (not included in combat checks)
- After the travel time elapses, the unit appears at the exit position

### Line-of-sight (simplified)

- For each attacker-target pair, check if any `TerrainFeature` bounding box intersects the line between them
- If blocked, the attack is invalid (unit must reposition)
- Full raycasting is not needed -- axis-aligned box intersection is sufficient for the prototype

### Schema sketch

```csharp
[SpacetimeDB.Type]
public enum TerrainType : byte { Trench, Tunnel, Bunker, OpenField }

[SpacetimeDB.Table(Accessor = "TerrainFeature", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "by_game_terrain", Columns = new[] { "GameId" })]
public partial struct TerrainFeature
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    public ulong GameId;
    public TerrainType Type;
    public float MinX;
    public float MinY;
    public float MinZ;
    public float MaxX;
    public float MaxY;
    public float MaxZ;
    public bool Passable;
    public bool Temporary;
}
```

---

## Client Tasks (3B)

### Terrain art

- Replace the flat plane with sculpted terrain (Unity Terrain system or ProBuilder meshes)
- Model trench segments that can be placed along the terrain
- Tunnel entrance meshes (doorway into a hillside or dugout)
- Bunker meshes (reinforced positions)

### Visual feedback

- Cover indicator on selected units (shield icon or highlight when inside a trench)
- Tunnel entrance glow or interaction prompt when a unit is nearby
- Units entering tunnels fade out; units exiting tunnels fade in

### Fog of war (stretch goal)

- Hide enemy units that are not in line-of-sight of any friendly unit
- Render unexplored areas with a dark overlay
- This is optional for the prototype but adds significant tactical depth

---

## Deliverable

Trenches reduce incoming damage for units inside them. Tunnels allow squads to move unseen between connected entrances. Line-of-sight blocking forces players to think about positioning. Terrain is no longer cosmetic -- it is a tactical resource.
