---
sidebar_position: 6
title: "Phase 4: Magic System"
---

# Phase 4 -- Magic System (Weeks 8--9)

**Goal:** Introduce the alternate-reality WW1 magic layer -- commanders cast spells that meaningfully affect the battlefield.

**Dependencies:** [Phase 2](./phase-2-squad-command.md) -- combat tick and unit system must exist. Can be developed in parallel with [Phase 3](./phase-3-terrain.md).

---

## Backend Tasks (4A)

### Ability definitions

Define abilities as a custom type (not a table -- these are static data):

```csharp
[SpacetimeDB.Type]
public enum AbilityId : byte
{
    ArcaneBarrage,
    WardOfEarth,
    HealingMist,
    PhantomMarch,
}

[SpacetimeDB.Type]
public enum EffectType : byte { Damage, Heal, Buff, Terrain }
```

Hard-code ability stats in a helper (reducers must be deterministic -- no config files):

| Ability | Mana Cost | Cooldown (s) | Effect | Radius | Value |
|---------|-----------|-------------|--------|--------|-------|
| Arcane Barrage | 30 | 8 | Damage | 10.0 | 60 |
| Ward of Earth | 25 | 15 | Terrain | 5.0 | -- |
| Healing Mist | 20 | 10 | Heal | 8.0 | 40 |
| Phantom March | 35 | 20 | Buff | -- | 10s duration |

### Tables

**AbilityCooldown**
- `ulong` auto-increment primary key
- `ulong` commander ID (indexed)
- `AbilityId` ability
- `Timestamp` ready at

### Reducers

| Reducer | Parameters | Behavior |
|---------|-----------|----------|
| `CastAbility` | `ulong commanderId, byte abilityId, float targetX, float targetY, float targetZ` | Validate mana, cooldown, and range; apply effect; insert cooldown row |

### Spell effects

**Arcane Barrage** -- find all enemy units within radius of target position, apply damage.

**Ward of Earth** -- insert a temporary `TerrainFeature` of type `Trench` at the target position (requires Phase 3 tables; if developing in parallel, add the table early).

**Healing Mist** -- find all friendly units within radius of target position, restore health (capped at max).

**Phantom March** -- set a `Stealthed` flag and a `StealthExpiresAt` timestamp on all friendly units within radius. Stealthed units are excluded from enemy combat checks (similar to tunnel invisibility).

### Mana regeneration

Add mana regen to the game tick: each commander regains a fixed amount of mana (e.g. 5) per tick. Cap at a max value (e.g. 100).

---

## Client Tasks (4B)

### Ability bar

- Horizontal bar at the bottom of the screen with 4 ability slots
- Each slot shows the ability icon, mana cost, and cooldown timer
- Grayed out when on cooldown or insufficient mana
- Keyboard shortcuts (1--4) to select an ability

### Targeting

- After selecting an ability, show a targeting reticle on the ground
- Reticle color indicates range validity (green = in range, red = out of range)
- Click to confirm; sends `CastAbility` reducer call

### Visual effects

- **Arcane Barrage:** explosion particle effect at target, damage numbers on hit units
- **Ward of Earth:** rocks/earth rising from the ground to form a trench barrier
- **Healing Mist:** green mist particle effect, health numbers on healed units
- **Phantom March:** shimmer/transparency effect on stealthed units (visible to owner, invisible to enemies)

### HUD

- Mana bar below the health bar on the commander's HUD
- Floating combat text for damage and healing

---

## Deliverable

Commanders can cast 4 distinct spells that deal damage, heal, reshape terrain, and grant stealth. Mana regenerates over time. Cooldowns prevent spam. Magic is a meaningful tactical tool, not just a cosmetic layer.
