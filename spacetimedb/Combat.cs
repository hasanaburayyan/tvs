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
    ReducerContext ctx, AbilityDef ability, ulong gamePlayerId)
  {
    int power = ability.BasePower;
    float range = ability.BaseRange;
    float radius = ability.BaseRadius;
    ulong cooldownMs = ability.CooldownMs;

    foreach (var effect in ctx.Db.active_effect.GamePlayerId.Filter(gamePlayerId))
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

  static ResourcePool? FindResourcePool(ReducerContext ctx, ulong gamePlayerId, ResourceKind kind)
  {
    foreach (var pool in ctx.Db.resource_pool.GamePlayerId.Filter(gamePlayerId))
    {
      if (pool.Kind == kind) return pool;
    }
    return null;
  }

  static AbilityCooldown? FindCooldown(ReducerContext ctx, ulong gamePlayerId, ulong abilityId)
  {
    foreach (var cd in ctx.Db.ability_cooldown.GamePlayerId.Filter(gamePlayerId))
    {
      if (cd.AbilityId == abilityId) return cd;
    }
    return null;
  }

  static void ClearResourcePools(ReducerContext ctx, ulong gamePlayerId)
  {
    foreach (var pool in ctx.Db.resource_pool.GamePlayerId.Filter(gamePlayerId))
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

    return kinds;
  }

  static int GetMaxForResource(ResourceKind kind, SkillDef skill)
  {
    switch (kind)
    {
      case ResourceKind.Supplies:
        if (skill.Name == "Commando") return 15;
        if (skill.Name == "Fire Support") return 50;
        return 30;
      case ResourceKind.Stamina: return 100;
      case ResourceKind.Mana: return 100;
      case ResourceKind.Command: return 100;
      default: return 100;
    }
  }

  static void SeedResourcePools(ReducerContext ctx, ulong gamePlayerId, ArchetypeDef archetype, WeaponDef weapon, SkillDef skill)
  {
    var needed = CollectRequiredResources(ctx, archetype, weapon, skill);
    foreach (var kind in needed)
    {
      int max = GetMaxForResource(kind, skill);
      ctx.Db.resource_pool.Insert(new ResourcePool { Id = 0, GamePlayerId = gamePlayerId, Kind = kind, Current = max, Max = max });
    }
  }

  [SpacetimeDB.Reducer]
  public static void UseAbility(ReducerContext ctx, ulong gameId, ulong abilityId, ulong? targetGamePlayerId, ulong? targetTerrainFeatureId, DbVector3? targetPosition, float? targetRotationY)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    if (gp.Dead)
      throw new Exception("Cannot use abilities while dead");

    if (gp.GameSessionId != gameId)
      throw new Exception("Game session mismatch");

    var loadout = FindLoadout(ctx, gameId, player.Id)
      ?? throw new Exception("No loadout set for this session");

    if (!IsAbilityInLoadout(ctx, abilityId, loadout))
      throw new Exception("Ability is not in your loadout");

    var ability = ctx.Db.ability_def.Id.Find(abilityId)
      ?? throw new Exception("Ability not found");

    if (FindCooldown(ctx, gp.Id, abilityId) is AbilityCooldown existing
        && existing.ReadyAt.MicrosecondsSinceUnixEpoch > ctx.Timestamp.MicrosecondsSinceUnixEpoch)
      throw new Exception("Ability is on cooldown");

    if (targetGamePlayerId is ulong targetId)
    {
      var target = ctx.Db.game_player.Id.Find(targetId)
        ?? throw new Exception("Target not found");
      if (target.GameSessionId != gameId)
        throw new Exception("Target is not in the same game");
      if (!target.Active)
        throw new Exception("Target is not active");
      if (target.Dead)
        throw new Exception("Target is dead");

      if (ability.Type == AbilityType.Damage || ability.Type == AbilityType.Debuff)
      {
        if (gp.TeamSlot != 0 && target.TeamSlot != 0 && gp.TeamSlot == target.TeamSlot)
          throw new Exception("Cannot use hostile abilities on teammates");
      }
      else if (ability.Type == AbilityType.Heal || ability.Type == AbilityType.Buff)
      {
        if (gp.TeamSlot != 0 && target.TeamSlot != 0 && gp.TeamSlot != target.TeamSlot)
          throw new Exception("Cannot use allied abilities on enemies");
      }

      if (!HasLineOfSight(ctx, gp.Position, target.Position, gameId))
        throw new Exception("Target is not in line of sight");
    }

    TerrainFeature? terrainTarget = null;
    if (targetTerrainFeatureId is ulong terrainTargetId)
    {
      terrainTarget = ctx.Db.terrain_feature.Id.Find(terrainTargetId)
        ?? throw new Exception("Terrain target not found");
      if (terrainTarget.Value.GameSessionId != gameId)
        throw new Exception("Terrain target is not in the same game");
      if (terrainTarget.Value.Expired || terrainTarget.Value.Health <= 0)
        throw new Exception("Terrain target is already destroyed");

      var terrainPos = new DbVector3(terrainTarget.Value.PosX, terrainTarget.Value.PosY, terrainTarget.Value.PosZ);
      float tdx = terrainPos.x - gp.Position.x;
      float tdz = terrainPos.z - gp.Position.z;
      float terrainDist = (float)Math.Sqrt(tdx * tdx + tdz * tdz);
      var resolved2 = ResolveAbility(ctx, ability, gp.Id);
      if (terrainDist > resolved2.Range)
        throw new Exception("Terrain target is out of range");
    }

    bool isDryFire = ability.Type == AbilityType.Damage
      && targetGamePlayerId is null
      && targetTerrainFeatureId is null;

    if (!isDryFire)
    {
      foreach (var cost in ability.ResourceCosts)
      {
        if (cost.Kind == ResourceKind.Health)
        {
          if (gp.Health < cost.Amount)
            throw new Exception($"Insufficient Health (need {cost.Amount}, have {gp.Health})");
        }
        else
        {
          var pool = FindResourcePool(ctx, gp.Id, cost.Kind)
            ?? throw new Exception($"No {cost.Kind} pool found");
          if (pool.Current < cost.Amount)
            throw new Exception($"Insufficient {cost.Kind} (need {cost.Amount}, have {pool.Current})");
        }
      }

      foreach (var cost in ability.ResourceCosts)
      {
        if (cost.Kind == ResourceKind.Health)
        {
          gp = ctx.Db.game_player.Id.Find(gp.Id)!.Value;
          ctx.Db.game_player.Id.Update(gp with { Health = gp.Health - cost.Amount });
          gp = ctx.Db.game_player.Id.Find(gp.Id)!.Value;
        }
        else
        {
          var pool = FindResourcePool(ctx, gp.Id, cost.Kind)!.Value;
          ctx.Db.resource_pool.Id.Update(pool with { Current = pool.Current - cost.Amount });
        }
      }
    }

    var resolved = ResolveAbility(ctx, ability, gp.Id);

    if (ability.Type == AbilityType.Terrain && ability.SpawnedTerrainType is TerrainType terrainType)
    {
      if (targetPosition is not DbVector3 pos)
        throw new Exception("Terrain ability requires a target position");

      float dx = pos.x - gp.Position.x;
      float dz = pos.z - gp.Position.z;
      float dist = (float)Math.Sqrt(dx * dx + dz * dz);
      if (dist > resolved.Range)
        throw new Exception("Target position is out of range");

      Timestamp? expiresAt = ability.EffectDurationMs > 0
        ? ctx.Timestamp + TimeSpan.FromMilliseconds(ability.EffectDurationMs)
        : null;

      var inserted = ctx.Db.terrain_feature.Insert(new TerrainFeature
      {
        Id = 0,
        GameSessionId = gameId,
        Type = terrainType,
        PosX = pos.x,
        PosY = 0f,
        PosZ = pos.z,
        SizeX = ability.TerrainSizeX,
        SizeY = ability.TerrainSizeY,
        SizeZ = ability.TerrainSizeZ,
        RotationY = targetRotationY ?? gp.RotationY,
        TeamIndex = gp.TeamSlot,
        CasterGamePlayerId = gp.Id,
        ExpiresAt = expiresAt,
        Health = ability.TerrainMaxHealth,
        MaxHealth = ability.TerrainMaxHealth,
      });

      if (expiresAt is Timestamp expiry)
      {
        ctx.Db.terrain_expiry_check.Insert(new TerrainExpiryCheck
        {
          Id = 0,
          TerrainFeatureId = inserted.Id,
          ScheduledAt = new ScheduleAt.Time(expiry),
        });
      }

      if (terrainType == TerrainType.Outpost || terrainType == TerrainType.CommandCenter)
        ScheduleOutpostRegen(ctx, inserted.Id);
    }

    if (ability.GrantedMods.Count > 0 && ability.EffectDurationMs > 0)
    {
      var expiresAt = ctx.Timestamp + TimeSpan.FromMilliseconds(ability.EffectDurationMs);

      if (ability.Type == AbilityType.Buff)
      {
        ulong buffTargetId = targetGamePlayerId ?? gp.Id;
        ctx.Db.active_effect.Insert(new ActiveEffect
        {
          Id = 0,
          GamePlayerId = buffTargetId,
          CasterGamePlayerId = gp.Id,
          SourceAbilityId = abilityId,
          Mods = ability.GrantedMods,
          AffectedAbilityIds = ability.AffectedAbilityIds,
          ExpiresAt = expiresAt,
        });
      }
      else if (ability.Type == AbilityType.Debuff && targetGamePlayerId is ulong debuffTarget)
      {
        ctx.Db.active_effect.Insert(new ActiveEffect
        {
          Id = 0,
          GamePlayerId = debuffTarget,
          CasterGamePlayerId = gp.Id,
          SourceAbilityId = abilityId,
          Mods = ability.GrantedMods,
          AffectedAbilityIds = ability.AffectedAbilityIds,
          ExpiresAt = expiresAt,
        });
      }
    }

    if (!isDryFire)
    {
      if (FindCooldown(ctx, gp.Id, abilityId) is AbilityCooldown cd)
      {
        ctx.Db.ability_cooldown.Id.Update(cd with
        {
          ReadyAt = ctx.Timestamp + TimeSpan.FromMilliseconds(resolved.CooldownMs),
        });
      }
      else
      {
        ctx.Db.ability_cooldown.Insert(new AbilityCooldown
        {
          Id = 0,
          GamePlayerId = gp.Id,
          AbilityId = abilityId,
          ReadyAt = ctx.Timestamp + TimeSpan.FromMilliseconds(resolved.CooldownMs),
        });
      }
    }

    int effectivePower = resolved.Power;
    bool killed = false;

    if (resolved.Power > 0 && targetGamePlayerId is ulong dmgTargetId)
    {
      var dmgTarget = ctx.Db.game_player.Id.Find(dmgTargetId)!.Value;
      if (ability.Type == AbilityType.Damage && !dmgTarget.Dead)
      {
        int effectiveDamage = Math.Max(0, resolved.Power - dmgTarget.Armor);
        effectivePower = effectiveDamage;
        int newHealth = Math.Max(0, dmgTarget.Health - effectiveDamage);
        var updated = dmgTarget with { Health = newHealth };

        if (newHealth <= 0 && !dmgTarget.Dead)
        {
          updated = updated with { Dead = true, DiedAt = ctx.Timestamp };
          killed = true;
          ClearTargetsForGamePlayer(ctx, dmgTarget.Id, dmgTarget.GameSessionId);

          ctx.Db.corpse.Insert(new Corpse
          {
            Id = 0,
            GameSessionId = gameId,
            GamePlayerId = dmgTarget.Id,
            PlayerId = dmgTarget.PlayerId,
            Position = dmgTarget.Position,
            RotationY = dmgTarget.RotationY,
          });
        }

        ctx.Db.game_player.Id.Update(updated);
      }
      else if (ability.Type == AbilityType.Heal && !dmgTarget.Dead)
      {
        int newHealth = Math.Min(dmgTarget.MaxHealth, dmgTarget.Health + resolved.Power);
        ctx.Db.game_player.Id.Update(dmgTarget with { Health = newHealth });
      }
    }
    else if (resolved.Power > 0 && ability.Type == AbilityType.Heal && targetGamePlayerId is null)
    {
      gp = ctx.Db.game_player.Id.Find(gp.Id)!.Value;
      if (!gp.Dead)
      {
        int newHealth = Math.Min(gp.MaxHealth, gp.Health + resolved.Power);
        ctx.Db.game_player.Id.Update(gp with { Health = newHealth });
      }
    }

    if (resolved.Power > 0 && ability.Type == AbilityType.Damage && terrainTarget is TerrainFeature tt)
    {
      int newHp = Math.Max(0, tt.Health - resolved.Power);
      var updated = tt with { Health = newHp };
      if (newHp <= 0)
        updated = updated with { Expired = true };
      ctx.Db.terrain_feature.Id.Update(updated);
    }

    var eventType = ability.Type switch
    {
      AbilityType.Damage => BattleLogEventType.Attack,
      AbilityType.Heal => BattleLogEventType.Heal,
      AbilityType.Buff => BattleLogEventType.Buff,
      AbilityType.Debuff => BattleLogEventType.Debuff,
      AbilityType.Terrain => BattleLogEventType.TerrainSpawn,
      AbilityType.Utility => BattleLogEventType.Utility,
      _ => BattleLogEventType.Utility,
    };

    ctx.Db.battle_log.Insert(new BattleLogEntry
    {
      Id = 0,
      GameSessionId = gameId,
      OccurredAt = ctx.Timestamp,
      EventType = eventType,
      ActorGamePlayerId = gp.Id,
      AbilityId = abilityId,
      TargetGamePlayerIds = targetGamePlayerId is ulong tid
        ? new List<ulong> { tid }
        : new List<ulong>(),
      ResolvedPower = effectivePower,
    });

    if (killed)
    {
      ctx.Db.battle_log.Insert(new BattleLogEntry
      {
        Id = 0,
        GameSessionId = gameId,
        OccurredAt = ctx.Timestamp,
        EventType = BattleLogEventType.Kill,
        ActorGamePlayerId = gp.Id,
        AbilityId = abilityId,
        TargetGamePlayerIds = new List<ulong> { (ulong)targetGamePlayerId! },
        ResolvedPower = effectivePower,
      });
    }

    Log.Info($"Player {player.Name} used {ability.Name} (power: {effectivePower}, event: {eventType}, target: {targetGamePlayerId?.ToString() ?? "none"}, terrain: {targetTerrainFeatureId?.ToString() ?? "none"})");
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

    if (FindLoadout(ctx, gameId, player.Id) is Loadout existing)
    {
      ctx.Db.loadout.Id.Update(existing with
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
    ctx.Db.game_player.Id.Update(gp with
    {
      Health = newMaxHealth,
      MaxHealth = newMaxHealth,
      Armor = newArmor,
    });

    ClearResourcePools(ctx, gp.Id);
    SeedResourcePools(ctx, gp.Id, archetype, weapon, skill);

    Log.Info($"Player {player.Name} set loadout for game {gameId}: {archetype.Name} / {weapon.Name} / {skill.Name}");
  }
}
