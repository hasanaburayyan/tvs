---
sidebar_position: 3
title: "Phase 1: The Battlefield"
---

# Phase 1 -- The Battlefield (Weeks 2--3)

**Goal:** A flat map with player-controlled commanders that can move around, proving real-time multiplayer state sync.

**Dependencies:** [Phase 0](./phase-0-foundation.md) -- lobby and player registration must work.

---

## Backend Tasks (1A)

### Tables

**Commander**
- `ulong` primary key (matches the `GamePlayer.Id` that owns this commander)
- `ulong` game session ID (indexed)
- `float` x, y, z position
- `float` rotation (yaw)
- `int` health
- `int` mana

**Unit** (NPC soldier -- placeholder for Phase 2)
- `ulong` auto-increment primary key
- `ulong` commander ID (indexed)
- `ulong` game session ID
- Position (x, y, z floats)
- `float` rotation
- `int` health
- Unit type enum (`Rifleman` / `Mage` / etc.)

### Reducers

| Reducer | Parameters | Behavior |
|---------|-----------|----------|
| `SpawnCommander` | `ulong gameId` | Create a `Commander` for the caller's `GamePlayer` at a spawn point |
| `MoveCommander` | `ulong gameId, float x, float y, float z, float rot` | Validate movement speed against `ctx.Timestamp` delta, update position |
| `SpawnSquad` | `ulong commanderId, byte unitType, byte count` | Insert `Unit` rows around the commander's position |

### Validation

- **Bounds checking** -- reject positions outside the map boundaries
- **Speed cap** -- compute distance between old and new position, divide by time delta from `ctx.Timestamp`, reject if exceeding max speed
- **Game state** -- only allow movement in `InProgress` games

### Schema sketch

```csharp
[SpacetimeDB.Type]
public enum UnitType : byte { Rifleman, Mage, Grenadier, Medic }

[SpacetimeDB.Table(Accessor = "Commander", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "by_game_cmd", Columns = new[] { "GameId" })]
public partial struct Commander
{
    [SpacetimeDB.PrimaryKey]
    public ulong GamePlayerId;

    public ulong GameId;
    public float X;
    public float Y;
    public float Z;
    public float Rotation;
    public int Health;
    public int Mana;
}

[SpacetimeDB.Table(Accessor = "Unit", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "by_commander", Columns = new[] { "CommanderId" })]
public partial struct Unit
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    public ulong CommanderId;
    public ulong GameId;
    public float X;
    public float Y;
    public float Z;
    public float Rotation;
    public int Health;
    public UnitType Type;
}
```

---

## Client Tasks (1B)

### Scene setup

- Create a flat terrain plane (200x200 units or similar)
- Basic skybox and directional light
- Placeholder materials for ground, player capsules

### Commander rendering

- Spawn a capsule or placeholder model for the local player's commander
- Spawn remote commanders from the `Commander` table with different colors per team
- Update positions each frame from table row callbacks

### Camera

- Third-person camera following the local commander (Cinemachine FreeLook or a simple follow script)
- Smooth rotation and zoom

### Input and networking

- WASD / joystick movement input
- Send `MoveCommander` reducer calls at a fixed rate (e.g. 10--20 times per second)
- Interpolate remote commander positions between updates for smooth movement
- Reconcile local prediction with authoritative server state

---

## Deliverable

Multiple players load into the same flat map, see each other's commanders, and move around in real-time with server-authoritative positions.
