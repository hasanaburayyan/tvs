---
sidebar_position: 4
title: "Phase 2: Squad Command"
---

# Phase 2 -- Squad Command (Weeks 4--5)

**Goal:** Each commander controls a small squad of NPC soldiers with basic AI and combat.

**Dependencies:** [Phase 1](./phase-1-battlefield.md) -- commanders must be spawnable and movable.

---

## Backend Tasks (2A)

### Order system

Add an order field or **Order** table to drive NPC behavior:

- Order types: `MoveTo`, `Attack`, `Hold`, `Follow`
- Each order carries a target position (x, y, z) or a target unit ID
- Orders are issued per-unit or in bulk for a selection

### Reducers

| Reducer | Parameters | Behavior |
|---------|-----------|----------|
| `IssueOrder` | `ulong unitId, byte orderType, float targetX, float targetY, float targetZ` | Set the unit's current order; validate ownership |
| `IssueBulkOrder` | `ulong[] unitIds, byte orderType, float targetX, float targetY, float targetZ` | Apply the same order to multiple units in one call |

### Game tick (scheduled table)

Create a **GameTick** scheduled table that fires every ~200ms:

1. **Movement** -- advance each unit toward its order waypoint at a speed determined by unit type
2. **Combat detection** -- for each unit, check if any enemy unit is within weapon range
3. **Damage resolution** -- apply damage based on unit type attack values; factor in a simple hit-chance roll (deterministic, seeded from game state)
4. **Cleanup** -- remove units with health at or below zero

```csharp
[SpacetimeDB.Table(Accessor = "GameTick", Scheduled = nameof(ProcessGameTick))]
public partial struct GameTick
{
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    public ulong GameId;
    public ScheduleAt ScheduledAt;
}
```

### Unit stats

Define base stats per `UnitType`:

| Type | Health | Speed | Range | Damage |
|------|--------|-------|-------|--------|
| Rifleman | 100 | 3.0 | 30.0 | 25 |
| Mage | 80 | 2.5 | 20.0 | 35 |
| Grenadier | 120 | 2.0 | 15.0 | 50 |
| Medic | 80 | 3.0 | 10.0 | 10 |

---

## Client Tasks (2B)

### NPC rendering

- Render each `Unit` row as a small soldier model (capsule or placeholder mesh)
- Color-code or badge by team
- Show health bars above units

### Selection and orders

- Click to select individual units; drag-box for multi-select
- Right-click on ground to issue `MoveTo`; right-click on enemy to issue `Attack`
- Selection highlight (circle under selected units)
- Order feedback: waypoint markers, attack lines

### Animations

- Idle, walk, and shoot animation states (placeholder animations are fine)
- Death animation and removal after a short delay

### Tactical view

- Toggle key (e.g. Tab) to switch to a top-down overhead camera
- Minimap in the corner showing unit positions as colored dots

---

## Deliverable

Commanders issue move and attack orders to their squad. NPCs walk toward waypoints, engage enemies in range, and die when health reaches zero. The game tick drives all NPC behavior server-side.
