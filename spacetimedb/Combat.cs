using SpacetimeDB;

public static partial class Module
{
  static void ApplyMods(ref int power, ref float range, ref float radius, ref ulong cooldownMs, List<AbilityMod> mods)
  {
    foreach (var mod in mods)
    {
      switch (mod.Type)
      {
        case ModType.DamageFlat:
          power += (int)mod.Value;
          break;
        case ModType.DamagePercent:
          power = (int)(power * (1f + mod.Value));
          break;
        case ModType.RangeFlat:
          range += mod.Value;
          break;
        case ModType.RangePercent:
          range *= (1f + mod.Value);
          break;
        case ModType.CooldownPercent:
          cooldownMs = (ulong)(cooldownMs * (1f + mod.Value));
          break;
        case ModType.RadiusPercent:
          radius *= (1f + mod.Value);
          break;
      }
    }
  }

  static (int Power, float Range, float Radius, ulong CooldownMs) ResolveAbility(
    ReducerContext ctx, AbilityDef ability, ulong entityId)
  {
    int power = ability.BasePower;
    float range = ability.BaseRange;
    float radius = ability.BaseRadius;
    ulong cooldownMs = ability.CooldownMs;

    foreach (var effect in ctx.Db.active_effect.EntityId.Filter(entityId))
    {
      if (effect.ExpiresAt.MicrosecondsSinceUnixEpoch < ctx.Timestamp.MicrosecondsSinceUnixEpoch)
        continue;

      if (effect.AffectedAbilityIds.Count == 0 || effect.AffectedAbilityIds.Contains(ability.Id))
        ApplyMods(ref power, ref range, ref radius, ref cooldownMs, effect.Mods);
    }

    return (power, range, radius, cooldownMs);
  }

  static bool IsAbilityInLoadout(ReducerContext ctx, ulong abilityId, Loadout loadout)
  {
    if (ctx.Db.weapon_def.Id.Find(loadout.WeaponDefId) is WeaponDef w)
    {
      if (w.PrimaryAbilityId == abilityId)
        return true;
    }

    if (ctx.Db.skill_def.Id.Find(loadout.SkillDefId) is SkillDef s)
    {
      if (s.AbilityIds.Contains(abilityId))
        return true;
    }

    if (ctx.Db.archetype_def.Id.Find(loadout.ArchetypeDefId) is ArchetypeDef a)
    {
      if (a.InnateAbilityIds.Contains(abilityId))
        return true;
    }

    return false;
  }

  static Loadout? FindLoadout(ReducerContext ctx, ulong gameSessionId, ulong playerId)
  {
    foreach (var lo in ctx.Db.loadout.PlayerId.Filter(playerId))
    {
      if (lo.GameSessionId == gameSessionId) return lo;
    }
    return null;
  }

  static ResourcePool? FindResourcePool(ReducerContext ctx, ulong entityId, ResourceKind kind)
  {
    foreach (var pool in ctx.Db.resource_pool.EntityId.Filter(entityId))
    {
      if (pool.Kind == kind) return pool;
    }
    return null;
  }

  static AbilityCooldown? FindCooldown(ReducerContext ctx, ulong entityId, ulong abilityId)
  {
    foreach (var cd in ctx.Db.ability_cooldown.EntityId.Filter(entityId))
    {
      if (cd.AbilityId == abilityId) return cd;
    }
    return null;
  }

  static void ClearResourcePools(ReducerContext ctx, ulong entityId)
  {
    foreach (var pool in ctx.Db.resource_pool.EntityId.Filter(entityId))
      ctx.Db.resource_pool.Id.Delete(pool.Id);
  }

  static HashSet<ResourceKind> CollectRequiredResources(ReducerContext ctx, ArchetypeDef archetype, WeaponDef weapon, SkillDef skill)
  {
    var kinds = new HashSet<ResourceKind>();
    var abilityIds = new List<ulong>();
    abilityIds.AddRange(archetype.InnateAbilityIds);
    abilityIds.Add(weapon.PrimaryAbilityId);
    abilityIds.AddRange(skill.AbilityIds);

    foreach (var abilityId in abilityIds)
    {
      if (ctx.Db.ability_def.Id.Find(abilityId) is AbilityDef ability)
      {
        foreach (var cost in ability.ResourceCosts)
          kinds.Add(cost.Kind);
      }
    }

    kinds.Remove(ResourceKind.Health);
    kinds.Add(ResourceKind.Stamina);

    if (weapon.ClipSize > 0)
      kinds.Add(ResourceKind.Supplies);

    return kinds;
  }

  static float GetMaxForResource(ResourceKind kind, SkillDef skill)
  {
    switch (kind)
    {
      case ResourceKind.Supplies:
        if (skill.Name == "Commando") return 15f;
        if (skill.Name == "Fire Support") return 50f;
        return 30f;
      case ResourceKind.Stamina: return 100f;
      case ResourceKind.Mana: return 100f;
      case ResourceKind.Command: return 100f;
      default: return 100f;
    }
  }

  static void SeedResourcePools(ReducerContext ctx, ulong entityId, ArchetypeDef archetype, WeaponDef weapon, SkillDef skill)
  {
    var needed = CollectRequiredResources(ctx, archetype, weapon, skill);
    foreach (var kind in needed)
    {
      float max = GetMaxForResource(kind, skill);
      ctx.Db.resource_pool.Insert(new ResourcePool { Id = 0, EntityId = entityId, Kind = kind, Current = max, Max = max });
    }
  }

  static List<int> ComputeDamageShares(ReducerContext ctx, DamageDistribution distribution, int totalPower, List<ulong> targetEntityIds, DbVector3 targetPos)
  {
    var shares = new List<int>(targetEntityIds.Count);

    switch (distribution)
    {
      case DamageDistribution.EvenSplit:
        int perTarget = totalPower / targetEntityIds.Count;
        int remainder = totalPower % targetEntityIds.Count;
        for (int i = 0; i < targetEntityIds.Count; i++)
          shares.Add(perTarget + (i < remainder ? 1 : 0));
        break;

      case DamageDistribution.ProximityFalloff:
        float totalWeight = 0;
        var weights = new float[targetEntityIds.Count];
        for (int i = 0; i < targetEntityIds.Count; i++)
        {
          var pos = GetEntityPosition(ctx, targetEntityIds[i]);
          float dx = pos.x - targetPos.x;
          float dz = pos.z - targetPos.z;
          float dist = (float)Math.Sqrt(dx * dx + dz * dz);
          float w = 1f / (1f + dist);
          weights[i] = w;
          totalWeight += w;
        }
        int assigned = 0;
        for (int i = 0; i < targetEntityIds.Count; i++)
        {
          int share = (int)(totalPower * (weights[i] / totalWeight));
          shares.Add(share);
          assigned += share;
        }
        if (shares.Count > 0)
          shares[0] += totalPower - assigned;
        break;

      default:
        for (int i = 0; i < targetEntityIds.Count; i++)
          shares.Add(i == 0 ? totalPower : 0);
        break;
    }

    return shares;
  }

  static bool ApplyDamageToEntity(ReducerContext ctx, ulong entityId, int damage, ulong gameId, List<ulong> affectedTargetIds, ulong? attackerEntityId = null)
  {
    bool killed = false;

    var target = ctx.Db.targetable.EntityId.Find(entityId);
    if (target is not Targetable t || t.Dead) return false;

    var ent = ctx.Db.entity.EntityId.Find(entityId);
    if (ent is not Entity e) return false;

    int effectiveDamage = Math.Max(0, damage - t.Armor);
    if (effectiveDamage == 0 && damage > 0)
      effectiveDamage = 1;
    int newHealth = Math.Max(0, t.Health - effectiveDamage);
    var updatedTarget = t with { Health = newHealth };

    if (newHealth <= 0)
    {
      updatedTarget = updatedTarget with { Dead = true, DiedAt = ctx.Timestamp };
      killed = true;

      if (e.Type == EntityType.GamePlayer)
      {
        ClearTargetsForEntity(ctx, entityId, e.GameSessionId);
        var gp = ctx.Db.game_player.EntityId.Find(entityId);
        ulong playerId = (gp is GamePlayer g) ? g.PlayerId : 0;

        var corpseEnt = CreateEntity(ctx, e.GameSessionId, EntityType.Corpse, e.Position, e.RotationY, e.TeamSlot);
        ctx.Db.corpse.Insert(new Corpse
        {
          EntityId = corpseEnt.EntityId,
          SourceEntityId = entityId,
          PlayerId = playerId,
        });
      }
      else if (e.Type == EntityType.Soldier)
      {
        var soldier = ctx.Db.soldier.EntityId.Find(entityId);
        ulong playerId = (soldier is Soldier s && s.OwnerPlayerId.HasValue) ? s.OwnerPlayerId.Value : 0;

        var corpseEnt = CreateEntity(ctx, e.GameSessionId, EntityType.Corpse, e.Position, e.RotationY, e.TeamSlot);
        ctx.Db.corpse.Insert(new Corpse
        {
          EntityId = corpseEnt.EntityId,
          SourceEntityId = entityId,
          PlayerId = playerId,
        });
      }
    }

    ctx.Db.targetable.EntityId.Update(updatedTarget);

    if (killed)
    {
      if (e.Type == EntityType.Terrain)
      {
        if (ctx.Db.terrain_feature.EntityId.Find(entityId) is TerrainFeature tf)
        {
          if (tf.Type == TerrainType.Road)
            OnRoadDestroyed(ctx, entityId);
          else if (tf.Type == TerrainType.CommandCenter)
            CheckCommandCenterWin(ctx, e);
        }
      }

      var leafSquad = FindLeafSquadForEntity(ctx, entityId);
      if (leafSquad is Squad ls)
        UpdateSquadCenters(ctx, ls.Id);
    }

    affectedTargetIds.Add(entityId);

    if (!killed && e.Type == EntityType.Soldier && attackerEntityId is ulong attackerId)
      TriggerSoldierReturnFire(ctx, entityId, attackerId, gameId);

    return killed;
  }

  static void CheckCommandCenterWin(ReducerContext ctx, Entity destroyedEntity)
  {
    var session = ctx.Db.game_session.Id.Find(destroyedEntity.GameSessionId);
    if (session is not GameSession gs || gs.State == SessionState.Ended) return;

    byte winnerTeam = destroyedEntity.TeamSlot == 1 ? (byte)2 : (byte)1;
    ctx.Db.game_session.Id.Update(gs with { State = SessionState.Ended, WinnerTeamSlot = winnerTeam });
    Log.Info($"Game {gs.Id} ended — team {winnerTeam} wins (enemy command center destroyed)");
  }

  const float SOLDIER_DAMAGE_FRACTION = 0.15f;
  const float SOLDIER_PROXIMITY_RANGE = 15f;

  static int GetSoldierDamage(int basePower)
  {
    return Math.Max(1, (int)(basePower * SOLDIER_DAMAGE_FRACTION));
  }

  static int GetOwnerWeaponBasePower(ReducerContext ctx, ulong ownerPlayerId, ulong gameSessionId)
  {
    var loadout = FindLoadout(ctx, gameSessionId, ownerPlayerId);
    if (loadout is not Loadout lo) return 0;
    var weapon = ctx.Db.weapon_def.Id.Find(lo.WeaponDefId);
    if (weapon is not WeaponDef w) return 0;
    var ability = ctx.Db.ability_def.Id.Find(w.PrimaryAbilityId);
    if (ability is not AbilityDef a) return 0;
    return a.BasePower;
  }

  static float GetOwnerWeaponSupplyCost(ReducerContext ctx, ulong ownerPlayerId, ulong gameSessionId)
  {
    var loadout = FindLoadout(ctx, gameSessionId, ownerPlayerId);
    if (loadout is not Loadout lo) return 0f;
    var weapon = ctx.Db.weapon_def.Id.Find(lo.WeaponDefId);
    if (weapon is not WeaponDef w) return 0f;
    var ability = ctx.Db.ability_def.Id.Find(w.PrimaryAbilityId);
    if (ability is not AbilityDef a) return 0f;
    foreach (var cost in a.ResourceCosts)
    {
      if (cost.Kind == ResourceKind.Supplies) return cost.Amount;
    }
    return 0f;
  }

  static bool DeductSoldierSupplyCost(ReducerContext ctx, ulong ownerPlayerId, ulong gameSessionId, float weaponSupplyCost)
  {
    float soldierCost = weaponSupplyCost * SOLDIER_DAMAGE_FRACTION;
    if (soldierCost <= 0f) return true;

    var gp = FindGamePlayer(ctx, ownerPlayerId, gameSessionId);
    if (gp is not GamePlayer ownerGp) return false;

    var pool = FindResourcePool(ctx, ownerGp.EntityId, ResourceKind.Supplies);
    if (pool is not ResourcePool p) return false;
    if (p.Current < soldierCost) return false;

    ctx.Db.resource_pool.Id.Update(p with { Current = p.Current - soldierCost });
    return true;
  }

  static bool IsOwnedBySamePlayer(ReducerContext ctx, ulong entityId, ulong ownerPlayerId)
  {
    var gp = ctx.Db.game_player.EntityId.Find(entityId);
    if (gp is GamePlayer g && g.PlayerId == ownerPlayerId) return true;

    var sol = ctx.Db.soldier.EntityId.Find(entityId);
    if (sol is Soldier s && s.OwnerPlayerId == ownerPlayerId) return true;

    return false;
  }

  static void SoldierFireAtTarget(ReducerContext ctx, ulong ownerPlayerId, ulong targetEntityId, int basePower, ulong gameSessionId)
  {
    if (IsOwnedBySamePlayer(ctx, targetEntityId, ownerPlayerId)) return;

    var targetT = ctx.Db.targetable.EntityId.Find(targetEntityId);
    if (targetT is not Targetable tt || tt.Dead) return;

    var targetEnt = ctx.Db.entity.EntityId.Find(targetEntityId);
    if (targetEnt is not Entity te) return;

    int soldierDamage = GetSoldierDamage(basePower);
    float weaponSupplyCost = GetOwnerWeaponSupplyCost(ctx, ownerPlayerId, gameSessionId);

    foreach (var soldier in ctx.Db.soldier.Iter())
    {
      if (soldier.OwnerPlayerId != ownerPlayerId) continue;

      var soldierT = ctx.Db.targetable.EntityId.Find(soldier.EntityId);
      if (soldierT is not Targetable st || st.Dead) continue;

      var soldierEnt = ctx.Db.entity.EntityId.Find(soldier.EntityId);
      if (soldierEnt is not Entity se) continue;
      if (se.GameSessionId != gameSessionId) continue;

      if (se.TeamSlot != 0 && te.TeamSlot != 0 && se.TeamSlot == te.TeamSlot) continue;

      targetT = ctx.Db.targetable.EntityId.Find(targetEntityId);
      if (targetT is not Targetable ttCheck || ttCheck.Dead) return;
      targetEnt = ctx.Db.entity.EntityId.Find(targetEntityId);
      if (targetEnt is not Entity teCheck) return;

      if (!HasLineOfSight(ctx, se.Position, teCheck.Position, gameSessionId)) continue;

      if (!DeductSoldierSupplyCost(ctx, ownerPlayerId, gameSessionId, weaponSupplyCost)) continue;

      var soldierAffected = new List<ulong>();
      bool killed = ApplyDamageToEntity(ctx, targetEntityId, soldierDamage, gameSessionId, soldierAffected);

      ctx.Db.battle_log.Insert(new BattleLogEntry
      {
        Id = 0,
        GameSessionId = gameSessionId,
        OccurredAt = ctx.Timestamp,
        EventType = BattleLogEventType.Attack,
        ActorEntityId = soldier.EntityId,
        AbilityId = null,
        TargetEntityIds = soldierAffected.Count > 0 ? soldierAffected : new List<ulong> { targetEntityId },
        ResolvedPower = soldierDamage,
      });

      if (killed)
      {
        ctx.Db.battle_log.Insert(new BattleLogEntry
        {
          Id = 0,
          GameSessionId = gameSessionId,
          OccurredAt = ctx.Timestamp,
          EventType = BattleLogEventType.Kill,
          ActorEntityId = soldier.EntityId,
          AbilityId = null,
          TargetEntityIds = new List<ulong> { targetEntityId },
          ResolvedPower = soldierDamage,
        });
        return;
      }
    }
  }

  static void SingleSoldierFireAtTarget(ReducerContext ctx, ulong soldierEntityId, ulong targetEntityId, int basePower, ulong gameSessionId)
  {
    var targetT = ctx.Db.targetable.EntityId.Find(targetEntityId);
    if (targetT is not Targetable tt || tt.Dead) return;

    var targetEnt = ctx.Db.entity.EntityId.Find(targetEntityId);
    if (targetEnt is not Entity te) return;

    var soldierT = ctx.Db.targetable.EntityId.Find(soldierEntityId);
    if (soldierT is not Targetable st || st.Dead) return;

    var soldierEnt = ctx.Db.entity.EntityId.Find(soldierEntityId);
    if (soldierEnt is not Entity se) return;

    if (se.TeamSlot != 0 && te.TeamSlot != 0 && se.TeamSlot == te.TeamSlot) return;

    if (!HasLineOfSight(ctx, se.Position, te.Position, gameSessionId)) return;

    var soldier = ctx.Db.soldier.EntityId.Find(soldierEntityId);
    if (soldier is Soldier sol && sol.OwnerPlayerId.HasValue)
    {
      float weaponSupplyCost = GetOwnerWeaponSupplyCost(ctx, sol.OwnerPlayerId.Value, gameSessionId);
      if (!DeductSoldierSupplyCost(ctx, sol.OwnerPlayerId.Value, gameSessionId, weaponSupplyCost)) return;
    }

    int soldierDamage = GetSoldierDamage(basePower);
    var soldierAffected = new List<ulong>();
    bool killed = ApplyDamageToEntity(ctx, targetEntityId, soldierDamage, gameSessionId, soldierAffected);

    ctx.Db.battle_log.Insert(new BattleLogEntry
    {
      Id = 0,
      GameSessionId = gameSessionId,
      OccurredAt = ctx.Timestamp,
      EventType = BattleLogEventType.Attack,
      ActorEntityId = soldierEntityId,
      AbilityId = null,
      TargetEntityIds = soldierAffected.Count > 0 ? soldierAffected : new List<ulong> { targetEntityId },
      ResolvedPower = soldierDamage,
    });

    if (killed)
    {
      ctx.Db.battle_log.Insert(new BattleLogEntry
      {
        Id = 0,
        GameSessionId = gameSessionId,
        OccurredAt = ctx.Timestamp,
        EventType = BattleLogEventType.Kill,
        ActorEntityId = soldierEntityId,
        AbilityId = null,
        TargetEntityIds = new List<ulong> { targetEntityId },
        ResolvedPower = soldierDamage,
      });
    }
  }

  static void TriggerSoldierReturnFire(ReducerContext ctx, ulong damagedSoldierEntityId, ulong attackerEntityId, ulong gameSessionId)
  {
    var soldier = ctx.Db.soldier.EntityId.Find(damagedSoldierEntityId);
    if (soldier is not Soldier s || !s.OwnerPlayerId.HasValue) return;

    int basePower = GetOwnerWeaponBasePower(ctx, s.OwnerPlayerId.Value, gameSessionId);
    if (basePower <= 0) return;

    SoldierFireAtTarget(ctx, s.OwnerPlayerId.Value, attackerEntityId, basePower, gameSessionId);
  }

  [SpacetimeDB.Table(Accessor = "soldier_combat_tick", Scheduled = nameof(SoldierCombatTick))]
  public partial struct SoldierCombatTickSchedule
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public ulong GameSessionId;
    public ScheduleAt ScheduledAt;
  }

  [SpacetimeDB.Reducer]
  public static void SoldierCombatTick(ReducerContext ctx, SoldierCombatTickSchedule tick)
  {
    var session = ctx.Db.game_session.Id.Find(tick.GameSessionId);
    if (session is not GameSession gs || gs.State == SessionState.Ended)
      return;

    float rangeSq = SOLDIER_PROXIMITY_RANGE * SOLDIER_PROXIMITY_RANGE;

    foreach (var soldierEnt in ctx.Db.entity.GameSessionId.Filter(tick.GameSessionId))
    {
      if (soldierEnt.Type != EntityType.Soldier) continue;

      var soldier = ctx.Db.soldier.EntityId.Find(soldierEnt.EntityId);
      if (soldier is not Soldier s || !s.OwnerPlayerId.HasValue) continue;

      var soldierT = ctx.Db.targetable.EntityId.Find(soldierEnt.EntityId);
      if (soldierT is not Targetable st || st.Dead) continue;

      ulong? closestEnemyId = null;
      float closestDistSq = rangeSq;

      foreach (var candidate in ctx.Db.entity.GameSessionId.Filter(tick.GameSessionId))
      {
        bool isUnit = candidate.Type == EntityType.GamePlayer || candidate.Type == EntityType.Soldier;
        bool isDamageableTerr = candidate.Type == EntityType.Terrain
          && ctx.Db.targetable.EntityId.Find(candidate.EntityId) is Targetable terrT && !terrT.Dead && terrT.MaxHealth > 0;
        if (!isUnit && !isDamageableTerr) continue;
        if (candidate.EntityId == soldierEnt.EntityId) continue;
        if (candidate.TeamSlot != 0 && soldierEnt.TeamSlot != 0 && candidate.TeamSlot == soldierEnt.TeamSlot) continue;
        if (isUnit && IsOwnedBySamePlayer(ctx, candidate.EntityId, s.OwnerPlayerId.Value)) continue;

        var candidateT = ctx.Db.targetable.EntityId.Find(candidate.EntityId);
        if (candidateT is not Targetable ct || ct.Dead) continue;

        float dx = candidate.Position.x - soldierEnt.Position.x;
        float dz = candidate.Position.z - soldierEnt.Position.z;
        float distSq = dx * dx + dz * dz;
        if (distSq < closestDistSq)
        {
          closestDistSq = distSq;
          closestEnemyId = candidate.EntityId;
        }
      }

      if (closestEnemyId is not ulong enemyId) continue;

      int basePower = GetOwnerWeaponBasePower(ctx, s.OwnerPlayerId.Value, tick.GameSessionId);
      if (basePower <= 0) continue;

      SingleSoldierFireAtTarget(ctx, soldierEnt.EntityId, enemyId, basePower, tick.GameSessionId);
    }

    ctx.Db.soldier_combat_tick.Insert(new SoldierCombatTickSchedule
    {
      Id = 0,
      GameSessionId = tick.GameSessionId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(500)),
    });
  }

  static void ScheduleSoldierCombatTick(ReducerContext ctx, ulong gameSessionId)
  {
    ctx.Db.soldier_combat_tick.Insert(new SoldierCombatTickSchedule
    {
      Id = 0,
      GameSessionId = gameSessionId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(500)),
    });
  }

  static void PayResourceCosts(ReducerContext ctx, AbilityDef ability, ulong entityId, ref Targetable gpTarget)
  {
    foreach (var cost in ability.ResourceCosts)
    {
      if (cost.Kind == ResourceKind.Health)
      {
        if (gpTarget.Health < (int)cost.Amount)
          throw new Exception($"Insufficient Health (need {cost.Amount}, have {gpTarget.Health})");
      }
      else
      {
        var pool = FindResourcePool(ctx, entityId, cost.Kind)
          ?? throw new Exception($"No {cost.Kind} pool found");
        if (pool.Current < cost.Amount)
          throw new Exception($"Insufficient {cost.Kind} (need {cost.Amount}, have {pool.Current})");
      }
    }

    foreach (var cost in ability.ResourceCosts)
    {
      if (cost.Kind == ResourceKind.Health)
      {
        gpTarget = ctx.Db.targetable.EntityId.Find(entityId)!.Value;
        ctx.Db.targetable.EntityId.Update(gpTarget with { Health = gpTarget.Health - (int)cost.Amount });
        gpTarget = ctx.Db.targetable.EntityId.Find(entityId)!.Value;
      }
      else
      {
        var pool = FindResourcePool(ctx, entityId, cost.Kind)!.Value;
        ctx.Db.resource_pool.Id.Update(pool with { Current = pool.Current - cost.Amount });
      }
    }
  }

  static void SetAbilityCooldown(ReducerContext ctx, ulong entityId, ulong abilityId, ulong cooldownMs)
  {
    if (FindCooldown(ctx, entityId, abilityId) is AbilityCooldown cd)
    {
      ctx.Db.ability_cooldown.Id.Update(cd with
      {
        ReadyAt = ctx.Timestamp + TimeSpan.FromMilliseconds(cooldownMs),
      });
    }
    else
    {
      ctx.Db.ability_cooldown.Insert(new AbilityCooldown
      {
        Id = 0,
        EntityId = entityId,
        AbilityId = abilityId,
        ReadyAt = ctx.Timestamp + TimeSpan.FromMilliseconds(cooldownMs),
      });
    }
  }

  static void ValidateAllyTarget(ReducerContext ctx, Entity gpEnt, Entity tEnt, AbilityDef ability, ulong gameId, float resolvedRange)
  {
    if (tEnt.GameSessionId != gameId)
      throw new Exception("Target is not in the same game");

    if (tEnt.Type == EntityType.GamePlayer)
    {
      var targetGp = ctx.Db.game_player.EntityId.Find(tEnt.EntityId);
      if (targetGp is GamePlayer tgp && !tgp.Active)
        throw new Exception("Target is not active");
      var tTarget = ctx.Db.targetable.EntityId.Find(tEnt.EntityId);
      if (tTarget is Targetable tt && tt.Dead && ability.Type != AbilityType.Utility)
        throw new Exception("Target is dead");
      if (gpEnt.TeamSlot != 0 && tEnt.TeamSlot != 0 && gpEnt.TeamSlot != tEnt.TeamSlot)
        throw new Exception("Cannot use allied abilities on enemies");
      if (!HasLineOfSight(ctx, gpEnt.Position, tEnt.Position, gameId))
        throw new Exception("Target is not in line of sight");
    }

    float dx = tEnt.Position.x - gpEnt.Position.x;
    float dz = tEnt.Position.z - gpEnt.Position.z;
    float dist = (float)Math.Sqrt(dx * dx + dz * dz);
    if (resolvedRange > 0 && dist > resolvedRange)
      throw new Exception("Target is out of range");
  }

  [SpacetimeDB.Reducer]
  public static void UseAbility(ReducerContext ctx, ulong gameId, ulong abilityId, ulong? targetEntityId, DbVector3? targetPosition, float? targetRotationY, DbVector3? spawnPosition)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    var gpEnt = ctx.Db.entity.EntityId.Find(gp.EntityId)!.Value;
    var gpTarget = ctx.Db.targetable.EntityId.Find(gp.EntityId)!.Value;

    if (gpTarget.Dead)
      throw new Exception("Cannot use abilities while dead");

    if (gpEnt.GameSessionId != gameId)
      throw new Exception("Game session mismatch");

    var loadout = FindLoadout(ctx, gameId, player.Id)
      ?? throw new Exception("No loadout set for this session");

    if (!IsAbilityInLoadout(ctx, abilityId, loadout))
      throw new Exception("Ability is not in your loadout");

    var ability = ctx.Db.ability_def.Id.Find(abilityId)
      ?? throw new Exception("Ability not found");

    if (FindCooldown(ctx, gp.EntityId, abilityId) is AbilityCooldown existing
        && existing.ReadyAt.MicrosecondsSinceUnixEpoch > ctx.Timestamp.MicrosecondsSinceUnixEpoch)
      throw new Exception("Ability is on cooldown");

    var resolved = ResolveAbility(ctx, ability, gp.EntityId);

    switch (ability.Targeting)
    {
      case TargetingMode.Projectile:
      {
        if (targetPosition is not DbVector3 aimPoint)
          throw new Exception("Projectile ability requires an aim point");

        var weapon = ctx.Db.weapon_def.Id.Find(loadout.WeaponDefId);
        bool isWeaponPrimary = weapon is WeaponDef w && w.PrimaryAbilityId == abilityId;

        if (isWeaponPrimary && weapon is WeaponDef wpn && wpn.ClipSize > 0)
        {
          var ws = ctx.Db.weapon_state.EntityId.Find(gp.EntityId);
          if (ws is WeaponState state)
          {
            if (state.ReloadingUntil is Timestamp reloadEnd
                && reloadEnd.MicrosecondsSinceUnixEpoch > ctx.Timestamp.MicrosecondsSinceUnixEpoch)
              throw new Exception("Cannot fire while reloading");
            if (state.AmmoInClip <= 0)
              throw new Exception("Clip empty — reload required");
          }
        }

        PayResourceCosts(ctx, ability, gp.EntityId, ref gpTarget);
        SetAbilityCooldown(ctx, gp.EntityId, abilityId, (ulong)resolved.CooldownMs);

        if (isWeaponPrimary && weapon is WeaponDef wpn2 && wpn2.ClipSize > 0)
        {
          var ws = ctx.Db.weapon_state.EntityId.Find(gp.EntityId);
          if (ws is WeaponState state)
          {
            int newAmmo = state.AmmoInClip - 1;
            if (newAmmo <= 0)
            {
              var reloadPool = FindResourcePool(ctx, gp.EntityId, ResourceKind.Supplies);
              int affordable = reloadPool != null ? Math.Min(wpn2.ClipSize, (int)reloadPool.Value.Current) : 0;
              if (affordable > 0)
              {
                ctx.Db.resource_pool.Id.Update(reloadPool!.Value with { Current = reloadPool.Value.Current - affordable });
                ctx.Db.weapon_state.EntityId.Update(state with
                {
                  AmmoInClip = affordable,
                  ReloadingUntil = ctx.Timestamp + TimeSpan.FromMilliseconds(wpn2.ReloadTimeMs),
                });
              }
              else
              {
                ctx.Db.weapon_state.EntityId.Update(state with { AmmoInClip = 0 });
              }
            }
            else
            {
              ctx.Db.weapon_state.EntityId.Update(state with { AmmoInClip = newAmmo });
            }
          }
        }

        const float MAX_BARREL_OFFSET = 5f;
        var origin = gpEnt.Position;
        if (spawnPosition is DbVector3 barrel)
        {
          float bx = barrel.x - gpEnt.Position.x;
          float by = barrel.y - gpEnt.Position.y;
          float bz = barrel.z - gpEnt.Position.z;
          float distSq = bx * bx + by * by + bz * bz;
          if (distSq <= MAX_BARREL_OFFSET * MAX_BARREL_OFFSET)
            origin = barrel;
          else
            Log.Warn($"Barrel rejected: dist={Math.Sqrt(distSq):F2} > {MAX_BARREL_OFFSET}, barrel=({barrel.x:F2},{barrel.y:F2},{barrel.z:F2}) entity=({gpEnt.Position.x:F2},{gpEnt.Position.y:F2},{gpEnt.Position.z:F2})");
        }
        else
        {
          Log.Warn($"No barrel position received, using entity pos=({gpEnt.Position.x:F2},{gpEnt.Position.y:F2},{gpEnt.Position.z:F2})");
        }

        float dx = aimPoint.x - origin.x;
        float dy = aimPoint.y - origin.y;
        float dz = aimPoint.z - origin.z;
        float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (len < 0.001f) { dx = 0f; dy = 0f; dz = 1f; len = 1f; }
        float inv = 1f / len;
        dx *= inv; dy *= inv; dz *= inv;

        float speed = ability.ProjectileSpeed > 0 ? ability.ProjectileSpeed : 60f;
        bool detonateAtRange = ability.BaseRadius > 0;
        float maxRange = detonateAtRange ? resolved.Range : 500f;
        float dropRate = ability.DropRate;

        int pelletCount = (isWeaponPrimary && weapon is WeaponDef wpn3 && wpn3.Mode == FireMode.Spread)
          ? Math.Max(1, (int)wpn3.PelletCount)
          : 1;
        float spreadAngle = (isWeaponPrimary && weapon is WeaponDef wpn4)
          ? wpn4.SpreadAngleDeg
          : 0f;

        int powerPerPellet = pelletCount > 1 ? resolved.Power / pelletCount : resolved.Power;
        int powerRemainder = pelletCount > 1 ? resolved.Power % pelletCount : 0;

        for (int i = 0; i < pelletCount; i++)
        {
          float pdx = dx, pdy = dy, pdz = dz;
          if (spreadAngle > 0f && pelletCount > 1)
          {
            float angleRad = spreadAngle * ((float)Math.PI / 180f);
            ulong tickSeed = (ulong)ctx.Timestamp.MicrosecondsSinceUnixEpoch;
            float hash1 = (projectileSpreadSeed(proj: (ulong)i, tick: tickSeed) % 10000) / 10000f;
            float hash2 = (projectileSpreadSeed(proj: (ulong)(i + pelletCount), tick: tickSeed) % 10000) / 10000f;
            float offYaw = (hash1 * 2f - 1f) * angleRad;
            float offPitch = (hash2 * 2f - 1f) * angleRad * 0.5f;

            float cosYaw = (float)Math.Cos(offYaw);
            float sinYaw = (float)Math.Sin(offYaw);
            float newDx = pdx * cosYaw + pdz * sinYaw;
            float newDz = -pdx * sinYaw + pdz * cosYaw;
            pdx = newDx; pdz = newDz;

            float cosPitch = (float)Math.Cos(offPitch);
            float sinPitch = (float)Math.Sin(offPitch);
            float newDy = pdy * cosPitch - (float)Math.Sqrt(pdx * pdx + pdz * pdz) * sinPitch;
            pdy = newDy;

            float pMag = (float)Math.Sqrt(pdx * pdx + pdy * pdy + pdz * pdz);
            if (pMag > 1e-6f) { float pInv = 1f / pMag; pdx *= pInv; pdy *= pInv; pdz *= pInv; }
          }

          int pelletPower = powerPerPellet + (i < powerRemainder ? 1 : 0);
          SpawnProjectile(ctx, gameId, gp.EntityId, abilityId,
            origin, pdx, pdy, pdz,
            speed, ability.BaseRadius, maxRange,
            gpEnt.TeamSlot, pelletPower,
            ability.AllowSubSquadTargeting, ability.Distribution,
            dropRate, detonateAtRange);
        }

        var eventType = ability.Type switch
        {
          AbilityType.Damage => BattleLogEventType.Attack,
          AbilityType.Debuff => BattleLogEventType.Debuff,
          _ => BattleLogEventType.Utility,
        };
        ctx.Db.battle_log.Insert(new BattleLogEntry
        {
          Id = 0,
          GameSessionId = gameId,
          OccurredAt = ctx.Timestamp,
          EventType = eventType,
          ActorEntityId = gp.EntityId,
          AbilityId = abilityId,
          TargetEntityIds = new List<ulong>(),
          ResolvedPower = resolved.Power,
        });

        Log.Info($"Player {player.Name} fired {ability.Name} (power: {resolved.Power}, speed: {ability.ProjectileSpeed}, pellets: {pelletCount})");
        break;
      }

      case TargetingMode.GroundTarget:
      {
        if (targetPosition is not DbVector3 pos)
          throw new Exception("Ground target ability requires a position");

        float dx = pos.x - gpEnt.Position.x;
        float dz = pos.z - gpEnt.Position.z;
        float dist = (float)Math.Sqrt(dx * dx + dz * dz);
        if (resolved.Range > 0 && dist > resolved.Range)
          throw new Exception("Target position is out of range");

        PayResourceCosts(ctx, ability, gp.EntityId, ref gpTarget);
        SetAbilityCooldown(ctx, gp.EntityId, abilityId, (ulong)resolved.CooldownMs);

        if (ability.Type == AbilityType.Terrain && ability.SpawnedTerrainType is TerrainType terrainType)
        {
          if (terrainType == TerrainType.Road)
          {
            PlaceRoad(ctx, gameId, gp.EntityId, gpEnt.TeamSlot, pos, targetRotationY ?? gpEnt.RotationY, ability);
          }
          else
          {
            Timestamp? expiresAt = ability.EffectDurationMs > 0
              ? ctx.Timestamp + TimeSpan.FromMilliseconds(ability.EffectDurationMs)
              : null;

            var terrainEnt = CreateEntity(ctx, gameId, EntityType.Terrain, new DbVector3(pos.x, 0f, pos.z), targetRotationY ?? gpEnt.RotationY, gpEnt.TeamSlot);
            CreateTargetable(ctx, terrainEnt.EntityId, ability.TerrainMaxHealth, ability.TerrainMaxHealth, 0);
            ctx.Db.terrain_feature.Insert(new TerrainFeature
            {
              EntityId = terrainEnt.EntityId,
              Type = terrainType,
              SizeX = ability.TerrainSizeX,
              SizeY = ability.TerrainSizeY,
              SizeZ = ability.TerrainSizeZ,
              CasterEntityId = gp.EntityId,
              ExpiresAt = expiresAt,
            });

            if (expiresAt is Timestamp expiry)
            {
              ctx.Db.terrain_expiry_check.Insert(new TerrainExpiryCheck
              {
                Id = 0,
                EntityId = terrainEnt.EntityId,
                ScheduledAt = new ScheduleAt.Time(expiry),
              });
            }

            if (terrainType == TerrainType.Outpost || terrainType == TerrainType.CommandCenter)
              ScheduleOutpostRegen(ctx, terrainEnt.EntityId);
          }
        }

        if (ability.GrantedMods.Count > 0 && ability.EffectDurationMs > 0)
        {
          var expiresAt = ctx.Timestamp + TimeSpan.FromMilliseconds(ability.EffectDurationMs);
          float radiusSq = resolved.Radius * resolved.Radius;

          foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameId))
          {
            if (ent.Type != EntityType.GamePlayer && ent.Type != EntityType.Soldier) continue;
            if (ability.Type == AbilityType.Buff && gpEnt.TeamSlot != 0 && ent.TeamSlot != gpEnt.TeamSlot) continue;
            var tgt = ctx.Db.targetable.EntityId.Find(ent.EntityId);
            if (tgt is not Targetable t || t.Dead) continue;

            float edx = ent.Position.x - pos.x;
            float edz = ent.Position.z - pos.z;
            if (radiusSq > 0 && edx * edx + edz * edz > radiusSq) continue;

            ctx.Db.active_effect.Insert(new ActiveEffect
            {
              Id = 0,
              EntityId = ent.EntityId,
              CasterEntityId = gp.EntityId,
              SourceAbilityId = abilityId,
              Mods = ability.GrantedMods,
              AffectedAbilityIds = ability.AffectedAbilityIds,
              ExpiresAt = expiresAt,
            });
          }
        }

        if (ability.Type == AbilityType.Heal && resolved.Power > 0)
        {
          float radiusSq = resolved.Radius * resolved.Radius;
          foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameId))
          {
            if (ent.Type != EntityType.GamePlayer && ent.Type != EntityType.Soldier) continue;
            if (gpEnt.TeamSlot != 0 && ent.TeamSlot != gpEnt.TeamSlot) continue;
            var tgt = ctx.Db.targetable.EntityId.Find(ent.EntityId);
            if (tgt is not Targetable t || t.Dead) continue;

            float edx = ent.Position.x - pos.x;
            float edz = ent.Position.z - pos.z;
            if (radiusSq > 0 && edx * edx + edz * edz > radiusSq) continue;

            int newHealth = Math.Min(t.MaxHealth, t.Health + resolved.Power);
            if (newHealth != t.Health)
              ctx.Db.targetable.EntityId.Update(t with { Health = newHealth });
          }
        }

        var groundEventType = ability.Type switch
        {
          AbilityType.Terrain => BattleLogEventType.TerrainSpawn,
          AbilityType.Heal => BattleLogEventType.Heal,
          AbilityType.Buff => BattleLogEventType.Buff,
          AbilityType.Utility => BattleLogEventType.Utility,
          _ => BattleLogEventType.Utility,
        };

        ctx.Db.battle_log.Insert(new BattleLogEntry
        {
          Id = 0,
          GameSessionId = gameId,
          OccurredAt = ctx.Timestamp,
          EventType = groundEventType,
          ActorEntityId = gp.EntityId,
          AbilityId = abilityId,
          TargetEntityIds = new List<ulong>(),
          ResolvedPower = resolved.Power,
        });

        Log.Info($"Player {player.Name} used {ability.Name} at ground ({pos.x:F1}, {pos.z:F1})");
        break;
      }

      case TargetingMode.UpgradeTarget:
      {
        if (targetPosition is not DbVector3 upgradePos)
          throw new Exception("Upgrade target requires a position");

        float udx = upgradePos.x - gpEnt.Position.x;
        float udz = upgradePos.z - gpEnt.Position.z;
        float udist = (float)Math.Sqrt(udx * udx + udz * udz);
        if (resolved.Range > 0 && udist > resolved.Range)
          throw new Exception("Target position is out of range");

        PayResourceCosts(ctx, ability, gp.EntityId, ref gpTarget);
        SetAbilityCooldown(ctx, gp.EntityId, abilityId, (ulong)resolved.CooldownMs);
        HandleUpgradeFeature(ctx, gameId, gp.EntityId, upgradePos);

        Log.Info($"Player {player.Name} used {ability.Name} at ({upgradePos.x:F1}, {upgradePos.z:F1})");
        break;
      }

      case TargetingMode.AllyTarget:
      {
        if (targetEntityId is not ulong allyId)
          throw new Exception("Ally-targeted ability requires a target entity");

        var tEnt = ctx.Db.entity.EntityId.Find(allyId)
          ?? throw new Exception("Target not found");

        ValidateAllyTarget(ctx, gpEnt, tEnt, ability, gameId, resolved.Range);

        PayResourceCosts(ctx, ability, gp.EntityId, ref gpTarget);
        SetAbilityCooldown(ctx, gp.EntityId, abilityId, (ulong)resolved.CooldownMs);

        if (ability.GrantedMods.Count > 0 && ability.EffectDurationMs > 0 && ability.Type == AbilityType.Buff)
        {
          var expiresAt = ctx.Timestamp + TimeSpan.FromMilliseconds(ability.EffectDurationMs);
          ctx.Db.active_effect.Insert(new ActiveEffect
          {
            Id = 0,
            EntityId = allyId,
            CasterEntityId = gp.EntityId,
            SourceAbilityId = abilityId,
            Mods = ability.GrantedMods,
            AffectedAbilityIds = ability.AffectedAbilityIds,
            ExpiresAt = expiresAt,
          });
        }

        var affectedTargetIds = new List<ulong>();
        if (ability.Type == AbilityType.Heal && resolved.Power > 0)
        {
          var tgt = ctx.Db.targetable.EntityId.Find(allyId);
          if (tgt is Targetable t && !t.Dead)
          {
            int newHealth = Math.Min(t.MaxHealth, t.Health + resolved.Power);
            ctx.Db.targetable.EntityId.Update(t with { Health = newHealth });
            affectedTargetIds.Add(allyId);
          }
        }

        var allyEventType = ability.Type switch
        {
          AbilityType.Heal => BattleLogEventType.Heal,
          AbilityType.Buff => BattleLogEventType.Buff,
          AbilityType.Utility => BattleLogEventType.Utility,
          _ => BattleLogEventType.Utility,
        };

        ctx.Db.battle_log.Insert(new BattleLogEntry
        {
          Id = 0,
          GameSessionId = gameId,
          OccurredAt = ctx.Timestamp,
          EventType = allyEventType,
          ActorEntityId = gp.EntityId,
          AbilityId = abilityId,
          TargetEntityIds = affectedTargetIds.Count > 0 ? affectedTargetIds : new List<ulong> { allyId },
          ResolvedPower = resolved.Power,
        });

        Log.Info($"Player {player.Name} used {ability.Name} on ally entity {allyId}");
        break;
      }

      case TargetingMode.SelfCast:
      {
        PayResourceCosts(ctx, ability, gp.EntityId, ref gpTarget);
        SetAbilityCooldown(ctx, gp.EntityId, abilityId, (ulong)resolved.CooldownMs);

        if (ability.GrantedMods.Count > 0 && ability.EffectDurationMs > 0 && ability.Type == AbilityType.Buff)
        {
          var expiresAt = ctx.Timestamp + TimeSpan.FromMilliseconds(ability.EffectDurationMs);
          ctx.Db.active_effect.Insert(new ActiveEffect
          {
            Id = 0,
            EntityId = gp.EntityId,
            CasterEntityId = gp.EntityId,
            SourceAbilityId = abilityId,
            Mods = ability.GrantedMods,
            AffectedAbilityIds = ability.AffectedAbilityIds,
            ExpiresAt = expiresAt,
          });
        }

        if (ability.Type == AbilityType.Heal && resolved.Power > 0)
        {
          gpTarget = ctx.Db.targetable.EntityId.Find(gp.EntityId)!.Value;
          if (!gpTarget.Dead)
          {
            int newHealth = Math.Min(gpTarget.MaxHealth, gpTarget.Health + resolved.Power);
            ctx.Db.targetable.EntityId.Update(gpTarget with { Health = newHealth });
          }
        }

        var selfEventType = ability.Type switch
        {
          AbilityType.Buff => BattleLogEventType.Buff,
          AbilityType.Heal => BattleLogEventType.Heal,
          AbilityType.Utility => BattleLogEventType.Utility,
          _ => BattleLogEventType.Utility,
        };

        ctx.Db.battle_log.Insert(new BattleLogEntry
        {
          Id = 0,
          GameSessionId = gameId,
          OccurredAt = ctx.Timestamp,
          EventType = selfEventType,
          ActorEntityId = gp.EntityId,
          AbilityId = abilityId,
          TargetEntityIds = new List<ulong>(),
          ResolvedPower = resolved.Power,
        });

        Log.Info($"Player {player.Name} used {ability.Name} (self-cast)");
        break;
      }

      default:
        throw new Exception($"Unknown targeting mode: {ability.Targeting}");
    }
  }

  [SpacetimeDB.Reducer]
  public static void SetLoadout(ReducerContext ctx, ulong gameId, ulong archetypeDefId, ulong weaponDefId, ulong skillDefId)
  {
    var player = GetPlayerForSender(ctx);

    var session = ctx.Db.game_session.Id.Find(gameId)
      ?? throw new Exception("Game session not found");

    if (session.State != SessionState.Lobby)
      throw new Exception("Can only set loadout during lobby phase");

    var gp = FindGamePlayer(ctx, player.Id, gameId)
      ?? throw new Exception("You are not in this game");

    var archetype = ctx.Db.archetype_def.Id.Find(archetypeDefId)
      ?? throw new Exception("Archetype not found");

    var weapon = ctx.Db.weapon_def.Id.Find(weaponDefId)
      ?? throw new Exception("Weapon not found");

    var skill = ctx.Db.skill_def.Id.Find(skillDefId)
      ?? throw new Exception("Skillset not found");

    if (skill.ArchetypeDefId != archetypeDefId)
      throw new Exception("Skillset does not belong to the selected archetype");

    if (FindLoadout(ctx, gameId, player.Id) is Loadout existing2)
    {
      ctx.Db.loadout.Id.Update(existing2 with
      {
        ArchetypeDefId = archetypeDefId,
        WeaponDefId = weaponDefId,
        SkillDefId = skillDefId,
      });
    }
    else
    {
      ctx.Db.loadout.Insert(new Loadout
      {
        Id = 0,
        GameSessionId = gameId,
        PlayerId = player.Id,
        ArchetypeDefId = archetypeDefId,
        WeaponDefId = weaponDefId,
        SkillDefId = skillDefId,
      });
    }

    int baseHealth = 100;
    int baseArmor = 0;
    int newMaxHealth = baseHealth + archetype.BonusHealth;
    int newArmor = baseArmor + archetype.BonusArmor;

    var target = ctx.Db.targetable.EntityId.Find(gp.EntityId);
    if (target is Targetable t)
    {
      ctx.Db.targetable.EntityId.Update(t with
      {
        Health = newMaxHealth,
        MaxHealth = newMaxHealth,
        Armor = newArmor,
      });
    }

    ClearResourcePools(ctx, gp.EntityId);
    SeedResourcePools(ctx, gp.EntityId, archetype, weapon, skill);

    if (weapon.ClipSize > 0)
    {
      var existingWs = ctx.Db.weapon_state.EntityId.Find(gp.EntityId);
      if (existingWs is WeaponState ws)
        ctx.Db.weapon_state.EntityId.Update(ws with { AmmoInClip = weapon.ClipSize, ReloadingUntil = null });
      else
        ctx.Db.weapon_state.Insert(new WeaponState { EntityId = gp.EntityId, AmmoInClip = weapon.ClipSize, ReloadingUntil = null });
    }
    else
    {
      var existingWs = ctx.Db.weapon_state.EntityId.Find(gp.EntityId);
      if (existingWs is WeaponState ws)
        ctx.Db.weapon_state.EntityId.Delete(ws.EntityId);
    }

    Log.Info($"Player {player.Name} set loadout for game {gameId}: {archetype.Name} / {weapon.Name} / {skill.Name}");
  }

  static ulong projectileSpreadSeed(ulong proj, ulong tick)
  {
    ulong h = proj * 2654435761UL + tick;
    h ^= h >> 16;
    h *= 0x45d9f3bUL;
    h ^= h >> 16;
    return h;
  }

  [SpacetimeDB.Reducer]
  public static void Reload(ReducerContext ctx, ulong gameId)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    var gpEnt = ctx.Db.entity.EntityId.Find(gp.EntityId)!.Value;
    if (gpEnt.GameSessionId != gameId)
      throw new Exception("Game session mismatch");

    var gpTarget = ctx.Db.targetable.EntityId.Find(gp.EntityId)!.Value;
    if (gpTarget.Dead)
      throw new Exception("Cannot reload while dead");

    var loadout = FindLoadout(ctx, gameId, player.Id)
      ?? throw new Exception("No loadout set");
    var weapon = ctx.Db.weapon_def.Id.Find(loadout.WeaponDefId)
      ?? throw new Exception("Weapon not found");

    if (weapon.ClipSize <= 0)
      throw new Exception("Weapon does not use magazines");

    var ws = ctx.Db.weapon_state.EntityId.Find(gp.EntityId);
    if (ws is not WeaponState state)
      throw new Exception("No weapon state");

    if (state.ReloadingUntil is Timestamp reloadEnd
        && reloadEnd.MicrosecondsSinceUnixEpoch > ctx.Timestamp.MicrosecondsSinceUnixEpoch)
      throw new Exception("Already reloading");

    if (state.AmmoInClip >= weapon.ClipSize)
      throw new Exception("Clip is already full");

    int roundsNeeded = weapon.ClipSize - state.AmmoInClip;
    var pool = FindResourcePool(ctx, gp.EntityId, ResourceKind.Supplies);
    int affordable = pool != null ? Math.Min(roundsNeeded, (int)pool.Value.Current) : 0;
    if (affordable <= 0)
      throw new Exception("No ammo available");

    if (pool != null)
      ctx.Db.resource_pool.Id.Update(pool.Value with { Current = pool.Value.Current - affordable });

    ctx.Db.weapon_state.EntityId.Update(state with
    {
      ReloadingUntil = ctx.Timestamp + TimeSpan.FromMilliseconds(weapon.ReloadTimeMs),
      AmmoInClip = state.AmmoInClip + affordable,
    });
  }
}
