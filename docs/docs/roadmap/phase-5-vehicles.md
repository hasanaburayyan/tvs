---
sidebar_position: 7
title: "Phase 5: Vehicles + Artillery"
---

# Phase 5 -- Vehicles + Artillery (Week 10)

**Goal:** Introduce primitive tanks and artillery as high-value combined-arms units.

**Dependencies:** [Phase 3](./phase-3-terrain.md) and [Phase 4](./phase-4-magic.md) -- terrain features should exist for cover interactions, and the combat tick should handle different unit categories.

---

## Backend Tasks (5A)

### Vehicle system

Extend the existing `Unit` table or create a separate **Vehicle** table. The vehicle approach keeps the schema cleaner since vehicles have distinct properties:

**Vehicle**
- `ulong` auto-increment primary key
- `ulong` commander ID (indexed)
- `ulong` game session ID
- Vehicle type enum: `Tank`, `Artillery`, `Transport`
- Position (x, y, z), rotation
- `int` health, `int` armor
- `int` ammo (finite, not replenished automatically)

### Vehicle stats

| Type | Health | Armor | Speed | Range | Damage | Ammo |
|------|--------|-------|-------|-------|--------|------|
| Tank | 500 | 50 | 1.5 | 25.0 | 80 | 30 |
| Artillery | 200 | 10 | 0.5 | 100.0 | 120 | 15 |
| Transport | 300 | 30 | 3.0 | -- | -- | -- |

- **Armor** reduces incoming damage by a flat amount (damage - armor, minimum 1)
- **Transport** carries up to 6 infantry units, protecting them during movement

### Reducers

| Reducer | Parameters | Behavior |
|---------|-----------|----------|
| `SpawnVehicle` | `ulong gameId, byte vehicleType` | Create a vehicle at the commander's position; limit 1--2 per commander |
| `MoveVehicle` | `ulong vehicleId, float x, float y, float z` | Move vehicle (slow); validate speed and bounds |
| `FireArtillery` | `ulong vehicleId, float targetX, float targetZ` | Validate vehicle is Artillery type and has ammo; schedule impact |

### Artillery mechanics

Artillery fire is **indirect** with a delay:

1. `FireArtillery` reducer records the target and sets an impact timestamp (e.g. `ctx.Timestamp + 3 seconds`)
2. The game tick checks for pending artillery impacts each cycle
3. On impact: deal damage in a large radius at the target position, destroy temporary terrain features in the blast zone
4. Artillery cannot fire while moving (enforce a "deployed" state)

### Game tick additions

- Process vehicle attacks (tanks fire at nearest enemy in range, consuming ammo)
- Check artillery impact timers
- Vehicles in cover (behind terrain features) get armor bonus

### Schema sketch

```csharp
[SpacetimeDB.Type]
public enum VehicleType : byte { Tank, Artillery, Transport }

[SpacetimeDB.Table(Accessor = "Vehicle", Public = true)]
[SpacetimeDB.Index.BTree(Accessor = "by_commander_v", Columns = new[] { "CommanderId" })]
public partial struct Vehicle
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    public ulong CommanderId;
    public ulong GameId;
    public VehicleType Type;
    public float X;
    public float Y;
    public float Z;
    public float Rotation;
    public int Health;
    public int Armor;
    public int Ammo;
    public bool Deployed;
}
```

---

## Client Tasks (5B)

### Vehicle models

- Placeholder tank model (box with a turret)
- Placeholder artillery model (wheeled cannon)
- Placeholder transport (truck or halftrack)
- Team-colored markings

### Vehicle controls

- Select a vehicle like a unit; right-click to move
- Artillery targeting: select artillery, press fire key, place targeting reticle
- Show a mortar arc preview line from artillery to target
- Deploy/undeploy toggle for artillery (must be stationary to fire)

### Effects

- Shell travel arc for artillery (visible projectile over 3 seconds)
- Impact explosion with screen shake
- Tank firing effect (muzzle flash, recoil)
- Destruction animation when vehicle health reaches zero (smoke, fire, wreck model)

### HUD additions

- Ammo counter when a vehicle is selected
- Armor indicator
- Artillery cooldown / reload timer

---

## Deliverable

Tanks advance slowly with heavy armor and firepower. Artillery provides devastating long-range indirect fire with a delay. Transports move infantry safely. Combined arms -- infantry, magic, and vehicles -- all interact on the same battlefield.
