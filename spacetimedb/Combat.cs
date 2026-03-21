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

  static List<int> ComputeDamageShares(ReducerContext ctx, DamageDistribution distribution, int totalPower, List<EntityRef> targets, DbVector3 targetPos)
  {
    var shares = new List<int>(targets.Count);

    switch (distribution)
    {
      case DamageDistribution.EvenSplit:
        int perTarget = totalPower / targets.Count;
        int remainder = totalPower % targets.Count;
        for (int i = 0; i < targets.Count; i++)
          shares.Add(perTarget + (i < remainder ? 1 : 0));
        break;

      case DamageDistribution.ProximityFalloff:
        float totalWeight = 0;
        var weights = new float[targets.Count];
        for (int i = 0; i < targets.Count; i++)
        {
          var pos = GetEntityPositionFromRef(ctx, targets[i]);
          float dx = pos.x - targetPos.x;
          float dz = pos.z - targetPos.z;
          float dist = (float)Math.Sqrt(dx * dx + dz * dz);
          float w = 1f / (1f + dist);
          weights[i] = w;
          totalWeight += w;
        }
        int assigned = 0;
        for (int i = 0; i < targets.Count; i++)
        {
          int share = (int)(totalPower * (weights[i] / totalWeight));
          shares.Add(share);
          assigned += share;
        }
        if (shares.Count > 0)
          shares[0] += totalPower - assigned;
        break;

      default:
        for (int i = 0; i < targets.Count; i++)
          shares.Add(i == 0 ? totalPower : 0);
        break;
    }

    return shares;
  }

  static DbVector3 GetEntityPositionFromRef(ReducerContext ctx, EntityRef entity)
  {
    if (entity.GamePlayerId != 0)
    {
      var g = ctx.Db.game_player.Id.Find(entity.GamePlayerId);
      if (g is GamePlayer gp) return gp.Position;
    }
    if (entity.SoldierId != 0)
    {
      var s = ctx.Db.soldier.Id.Find(entity.SoldierId);
      if (s is Soldier sol) return sol.Position;
    }
    return new DbVector3(0, 0, 0);
  }

  static bool ApplyDamageToEntity(ReducerContext ctx, EntityRef entity, int damage, ulong gameId, List<ulong> affectedTargetIds)
  {
    bool killed = false;

    if (entity.GamePlayerId != 0)
    {
      var target = ctx.Db.game_player.Id.Find(entity.GamePlayerId);
      if (target is GamePlayer t && !t.Dead)
      {
        int effectiveDamage = Math.Max(0, damage - t.Armor);
        int newHealth = Math.Max(0, t.Health - effectiveDamage);
        var updated = t with { Health = newHealth };

        if (newHealth <= 0)
        {
          updated = updated with { Dead = true, DiedAt = ctx.Timestamp };
          killed = true;
          ClearTargetsForGamePlayer(ctx, t.Id, t.GameSessionId);

          ctx.Db.corpse.Insert(new Corpse
          {
            Id = 0,
            GameSessionId = gameId,
            GamePlayerId = t.Id,
            PlayerId = t.PlayerId,
            Position = t.Position,
            RotationY = t.RotationY,
          });
        }

        ctx.Db.game_player.Id.Update(updated);

        if (killed)
        {
          var leafSquad = FindLeafSquadForGamePlayer(ctx, entity.GamePlayerId);
          if (leafSquad is Squad ls)
            UpdateSquadCenters(ctx, ls.Id);
        }

        affectedTargetIds.Add(entity.GamePlayerId);
      }
    }
    else if (entity.SoldierId != 0)
    {
      var target = ctx.Db.soldier.Id.Find(entity.SoldierId);
      if (target is Soldier s && !s.Dead)
      {
        int effectiveDamage = Math.Max(0, damage - s.Armor);
        int newHealth = Math.Max(0, s.Health - effectiveDamage);
        var updated = s with { Health = newHealth };

        if (newHealth <= 0)
        {
          updated = updated with { Dead = true, DiedAt = ctx.Timestamp };
          killed = true;
        }

        ctx.Db.soldier.Id.Update(updated);

        if (killed)
        {
          var leafSquad = FindLeafSquadForSoldier(ctx, entity.SoldierId);
          if (leafSquad is Squad ls)
            UpdateSquadCenters(ctx, ls.Id);

          ctx.Db.corpse.Insert(new Corpse
          {
            Id = 0,
            GameSessionId = gameId,
            SoldierId = entity.SoldierId,
            GamePlayerId = null,
            PlayerId = s.OwnerPlayerId ?? 0,
            Position = s.Position,
            RotationY = s.RotationY,
          });
        }
      }
    }

    return killed;
  }

  [SpacetimeDB.Reducer]
  public static void UseAbility(ReducerContext ctx, ulong gameId, ulong abilityId, ulong? targetGamePlayerId, ulong? targetSoldierId, ulong? targetTerrainFeatureId, DbVector3? targetPosition, float? targetRotationY)
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

    Soldier? soldierTarget = null;
    if (targetSoldierId is ulong soldierId)
    {
      if (!ability.AllowSubSquadTargeting)
        throw new Exception("This ability cannot target individual soldiers");

      soldierTarget = ctx.Db.soldier.Id.Find(soldierId)
        ?? throw new Exception("Soldier target not found");
      if (soldierTarget.Value.GameSessionId != gameId)
        throw new Exception("Soldier target is not in the same game");
      if (soldierTarget.Value.Dead)
        throw new Exception("Soldier target is dead");

      if (!HasLineOfSight(ctx, gp.Position, soldierTarget.Value.Position, gameId))
        throw new Exception("Soldier target is not in line of sight");
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
      && targetSoldierId is null
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
    var affectedTargetIds = new List<ulong>();

    if (resolved.Power > 0 && targetGamePlayerId is ulong dmgTargetId)
    {
      var dmgTarget = ctx.Db.game_player.Id.Find(dmgTargetId)!.Value;

      if (ability.Type == AbilityType.Damage && !dmgTarget.Dead)
      {
        bool useSquadDistribution = ability.Distribution != DamageDistribution.Single
                                    && !ability.AllowSubSquadTargeting;

        if (useSquadDistribution)
        {
          var targetLeaf = FindLeafSquadForGamePlayer(ctx, dmgTargetId);
          if (targetLeaf is Squad leaf)
          {
            var root = GetRootSquad(ctx, leaf.Id);
            var entities = GetAllEntities(ctx, root.Id);
            var aliveEntities = new List<EntityRef>();

            foreach (var e in entities)
            {
              if (e.GamePlayerId != 0)
              {
                var g = ctx.Db.game_player.Id.Find(e.GamePlayerId);
                if (g is GamePlayer gp2 && !gp2.Dead) aliveEntities.Add(e);
              }
              else if (e.SoldierId != 0)
              {
                var s = ctx.Db.soldier.Id.Find(e.SoldierId);
                if (s is Soldier sol2 && !sol2.Dead) aliveEntities.Add(e);
              }
            }

            if (aliveEntities.Count > 0)
            {
              var shares = ComputeDamageShares(ctx, ability.Distribution, resolved.Power, aliveEntities, dmgTarget.Position);
              for (int i = 0; i < aliveEntities.Count; i++)
              {
                int share = shares[i];
                if (share <= 0) continue;
                bool entityDied = ApplyDamageToEntity(ctx, aliveEntities[i], share, gameId, affectedTargetIds);
                if (entityDied && aliveEntities[i].GamePlayerId == dmgTargetId)
                  killed = true;
              }
              effectivePower = resolved.Power;
            }
          }
        }
        else
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

          if (killed)
          {
            var playerLeafSquad = FindLeafSquadForGamePlayer(ctx, dmgTargetId);
            if (playerLeafSquad is Squad pls)
              UpdateSquadCenters(ctx, pls.Id);
          }

          affectedTargetIds.Add(dmgTargetId);
        }
      }
      else if (ability.Type == AbilityType.Heal && !dmgTarget.Dead)
      {
        int newHealth = Math.Min(dmgTarget.MaxHealth, dmgTarget.Health + resolved.Power);
        ctx.Db.game_player.Id.Update(dmgTarget with { Health = newHealth });
        affectedTargetIds.Add(dmgTargetId);
      }
    }
    else if (resolved.Power > 0 && ability.Type == AbilityType.Damage && soldierTarget is Soldier solTarget && !solTarget.Dead)
    {
      int effectiveDamage = Math.Max(0, resolved.Power - solTarget.Armor);
      effectivePower = effectiveDamage;
      int newHealth = Math.Max(0, solTarget.Health - effectiveDamage);
      var updated = solTarget with { Health = newHealth };

      if (newHealth <= 0)
      {
        updated = updated with { Dead = true, DiedAt = ctx.Timestamp };
        killed = true;
      }

      ctx.Db.soldier.Id.Update(updated);

      if (killed)
      {
        var leafSquad = FindLeafSquadForSoldier(ctx, solTarget.Id);
        if (leafSquad is Squad ls)
          UpdateSquadCenters(ctx, ls.Id);

        ctx.Db.corpse.Insert(new Corpse
        {
          Id = 0,
          GameSessionId = gameId,
          SoldierId = solTarget.Id,
          GamePlayerId = null,
          PlayerId = solTarget.OwnerPlayerId ?? 0,
          Position = solTarget.Position,
          RotationY = solTarget.RotationY,
        });
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

    var logTargetIds = affectedTargetIds.Count > 0
      ? affectedTargetIds
      : (targetGamePlayerId is ulong tid ? new List<ulong> { tid } : new List<ulong>());

    ctx.Db.battle_log.Insert(new BattleLogEntry
    {
      Id = 0,
      GameSessionId = gameId,
      OccurredAt = ctx.Timestamp,
      EventType = eventType,
      ActorGamePlayerId = gp.Id,
      AbilityId = abilityId,
      TargetGamePlayerIds = logTargetIds,
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
        TargetGamePlayerIds = logTargetIds,
        ResolvedPower = effectivePower,
      });
    }

    Log.Info($"Player {player.Name} used {ability.Name} (power: {effectivePower}, event: {eventType}, target: {targetGamePlayerId?.ToString() ?? "none"}, soldier: {targetSoldierId?.ToString() ?? "none"}, terrain: {targetTerrainFeatureId?.ToString() ?? "none"})");
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
