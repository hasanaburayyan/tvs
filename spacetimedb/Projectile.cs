using SpacetimeDB;

public static partial class Module
{
  const float PROJECTILE_TICK_MS = 50f;
  const float ENTITY_HIT_RADIUS = 1.0f;

  [SpacetimeDB.Table(Accessor = "projectile", Public = true)]
  public partial struct Projectile
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;
    public ulong CasterEntityId;
    public ulong AbilityId;
    public DbVector3 Position;
    public float DirectionX;
    public float DirectionY;
    public float DirectionZ;
    public float Speed;
    public float Radius;
    public float MaxRange;
    public float DistanceTraveled;
    public byte CasterTeamSlot;
    public int ResolvedPower;
    public bool AllowSubSquadTargeting;
    public DamageDistribution Distribution;
    public float DropRate;
    public bool DetonateAtMaxRange;
  }

  [SpacetimeDB.Table(Accessor = "projectile_tick", Scheduled = nameof(TickProjectile))]
  public partial struct ProjectileTickSchedule
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public ulong ProjectileId;
    public ScheduleAt ScheduledAt;
  }

  static float PointToSegmentDistanceSq(DbVector3 point, DbVector3 segA, DbVector3 segB)
  {
    float dx = segB.x - segA.x, dy = segB.y - segA.y, dz = segB.z - segA.z;
    float lenSq = dx * dx + dy * dy + dz * dz;
    if (lenSq < 1e-12f)
    {
      float px = point.x - segA.x, py = point.y - segA.y, pz = point.z - segA.z;
      return px * px + py * py + pz * pz;
    }
    float t = Math.Clamp(
      ((point.x - segA.x) * dx + (point.y - segA.y) * dy + (point.z - segA.z) * dz) / lenSq,
      0f, 1f);
    float cx = segA.x + t * dx - point.x;
    float cy = segA.y + t * dy - point.y;
    float cz = segA.z + t * dz - point.z;
    return cx * cx + cy * cy + cz * cz;
  }

  static float ParameterAlongSegment(DbVector3 point, DbVector3 segA, DbVector3 segB)
  {
    float dx = segB.x - segA.x, dy = segB.y - segA.y, dz = segB.z - segA.z;
    float lenSq = dx * dx + dy * dy + dz * dz;
    if (lenSq < 1e-12f) return 0f;
    return Math.Clamp(
      ((point.x - segA.x) * dx + (point.y - segA.y) * dy + (point.z - segA.z) * dz) / lenSq,
      0f, 1f);
  }

  static bool IsDamageableTerrain(ReducerContext ctx, ulong entityId)
  {
    if (ctx.Db.targetable.EntityId.Find(entityId) is Targetable t && !t.Dead && t.MaxHealth > 0)
      return true;
    return false;
  }

  static bool SegmentBlockedByTerrain(ReducerContext ctx, DbVector3 from, DbVector3 to, ulong gameSessionId, byte casterTeamSlot)
  {
    float dx = to.x - from.x, dy = to.y - from.y, dz = to.z - from.z;
    float len = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
    if (len < 0.001f) return false;
    float inv = 1f / len;
    var dir = new DbVector3(dx * inv, dy * inv, dz * inv);

    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameSessionId))
    {
      if (ent.Type != EntityType.Terrain) continue;
      if (ctx.Db.terrain_feature.EntityId.Find(ent.EntityId) is not TerrainFeature terrain) continue;
      if (terrain.Expired) continue;

      var boxCenter = new DbVector3(ent.Position.x, ent.Position.y + terrain.SizeY * 0.5f, ent.Position.z);
      var boxHalfExtents = new DbVector3(terrain.SizeX * 0.5f, terrain.SizeY * 0.5f, terrain.SizeZ * 0.5f);

      if (RayIntersectsOBB(from, dir, len, boxCenter, boxHalfExtents, ent.RotationY))
      {
        if (ent.TeamSlot != casterTeamSlot && IsDamageableTerrain(ctx, ent.EntityId))
          continue;
        return true;
      }
    }
    return false;
  }

  static void ApplyProjectileHit(ReducerContext ctx, Projectile proj, ulong hitEntityId, DbVector3 impactPos)
  {
    Log.Info($"[PROJ-DEBUG] ApplyProjectileHit: projId={proj.Id} casterEntityId={proj.CasterEntityId} hitEntityId={hitEntityId} power={proj.ResolvedPower} radius={proj.Radius}");

    var ability = ctx.Db.ability_def.Id.Find(proj.AbilityId);
    bool killed = false;
    var affectedTargetIds = new List<ulong>();

    if (proj.Radius > 0)
    {
      float radiusSq = proj.Radius * proj.Radius;
      foreach (var ent in ctx.Db.entity.GameSessionId.Filter(proj.GameSessionId))
      {
        if (ent.TeamSlot != 0 && ent.TeamSlot == proj.CasterTeamSlot) continue;
        bool isUnit = ent.Type == EntityType.GamePlayer || ent.Type == EntityType.Soldier;
        bool isDamageableTerr = ent.Type == EntityType.Terrain && IsDamageableTerrain(ctx, ent.EntityId);
        if (!isUnit && !isDamageableTerr) continue;
        var tgt = ctx.Db.targetable.EntityId.Find(ent.EntityId);
        if (tgt is not Targetable t || t.Dead) continue;
        float dx = ent.Position.x - impactPos.x;
        float dz = ent.Position.z - impactPos.z;
        if (dx * dx + dz * dz <= radiusSq)
        {
          bool entityDied = ApplyDamageToEntity(ctx, ent.EntityId, proj.ResolvedPower, proj.GameSessionId, affectedTargetIds);
          if (entityDied) killed = true;
        }
      }
    }
    else
    {
      var hitEnt = ctx.Db.entity.EntityId.Find(hitEntityId);
      if (hitEnt is Entity he)
      {
        bool useSquadDistribution = proj.Distribution != DamageDistribution.Single
                                    && !proj.AllowSubSquadTargeting
                                    && he.Type == EntityType.GamePlayer;

        if (useSquadDistribution)
        {
          var targetLeaf = FindLeafSquadForEntity(ctx, hitEntityId);
          if (targetLeaf is Squad leaf)
          {
            var root = GetRootSquad(ctx, leaf.Id);
            var entityIds = GetAllEntityIds(ctx, root.Id);
            var aliveEntityIds = new List<ulong>();
            foreach (var eid in entityIds)
            {
              if (ctx.Db.targetable.EntityId.Find(eid) is Targetable et && !et.Dead)
                aliveEntityIds.Add(eid);
            }
            if (aliveEntityIds.Count > 0)
            {
              var shares = ComputeDamageShares(ctx, proj.Distribution, proj.ResolvedPower, aliveEntityIds, he.Position);
              for (int i = 0; i < aliveEntityIds.Count; i++)
              {
                if (shares[i] <= 0) continue;
                bool entityDied = ApplyDamageToEntity(ctx, aliveEntityIds[i], shares[i], proj.GameSessionId, affectedTargetIds);
                if (entityDied && aliveEntityIds[i] == hitEntityId)
                  killed = true;
              }
            }
          }
          else
          {
            killed = ApplyDamageToEntity(ctx, hitEntityId, proj.ResolvedPower, proj.GameSessionId, affectedTargetIds);
          }
        }
        else
        {
          killed = ApplyDamageToEntity(ctx, hitEntityId, proj.ResolvedPower, proj.GameSessionId, affectedTargetIds);
        }
      }
    }

    if (ability is AbilityDef aDef && (aDef.Type == AbilityType.Debuff) && aDef.GrantedMods.Count > 0 && aDef.EffectDurationMs > 0)
    {
      var expiresAt = ctx.Timestamp + TimeSpan.FromMilliseconds(aDef.EffectDurationMs);
      foreach (var targetId in affectedTargetIds)
      {
        ctx.Db.active_effect.Insert(new ActiveEffect
        {
          Id = 0,
          EntityId = targetId,
          CasterEntityId = proj.CasterEntityId,
          SourceAbilityId = proj.AbilityId,
          Mods = aDef.GrantedMods,
          AffectedAbilityIds = aDef.AffectedAbilityIds,
          ExpiresAt = expiresAt,
        });
      }
    }

    var logTargetIds = affectedTargetIds.Count > 0
      ? affectedTargetIds
      : new List<ulong> { hitEntityId };

    ctx.Db.battle_log.Insert(new BattleLogEntry
    {
      Id = 0,
      GameSessionId = proj.GameSessionId,
      OccurredAt = ctx.Timestamp,
      EventType = BattleLogEventType.Attack,
      ActorEntityId = proj.CasterEntityId,
      AbilityId = proj.AbilityId,
      TargetEntityIds = logTargetIds,
      ResolvedPower = proj.ResolvedPower,
    });

    if (killed)
    {
      ctx.Db.battle_log.Insert(new BattleLogEntry
      {
        Id = 0,
        GameSessionId = proj.GameSessionId,
        OccurredAt = ctx.Timestamp,
        EventType = BattleLogEventType.Kill,
        ActorEntityId = proj.CasterEntityId,
        AbilityId = proj.AbilityId,
        TargetEntityIds = logTargetIds,
        ResolvedPower = proj.ResolvedPower,
      });
    }

    if (affectedTargetIds.Count > 0)
    {
      var casterGp = ctx.Db.game_player.EntityId.Find(proj.CasterEntityId);
      if (casterGp is GamePlayer cgp)
      {
        ulong soldierTarget = hitEntityId;
        if (hitEntityId == 0)
        {
          foreach (var tid in affectedTargetIds)
          {
            var t2 = ctx.Db.targetable.EntityId.Find(tid);
            if (t2 is Targetable tt2 && !tt2.Dead) { soldierTarget = tid; break; }
          }
        }
        if (soldierTarget != 0)
          SoldierFireAtTarget(ctx, cgp.PlayerId, soldierTarget, proj.ResolvedPower, proj.GameSessionId);
      }
    }
  }

  static bool IsOutOfMapBounds(ReducerContext ctx, DbVector3 pos, ulong gameSessionId)
  {
    var session = ctx.Db.game_session.Id.Find(gameSessionId);
    if (session is not GameSession gs) return true;
    var mapDef = ctx.Db.map_def.Id.Find(gs.MapDefId);
    if (mapDef is not MapDef map) return true;
    float halfX = map.SizeX * 0.5f;
    float halfZ = map.SizeZ * 0.5f;
    return pos.x < -halfX || pos.x > halfX || pos.z < -halfZ || pos.z > halfZ;
  }

  [SpacetimeDB.Reducer]
  public static void TickProjectile(ReducerContext ctx, ProjectileTickSchedule tick)
  {
    var projOpt = ctx.Db.projectile.Id.Find(tick.ProjectileId);
    if (projOpt is not Projectile proj) return;

    float deltaSec = PROJECTILE_TICK_MS / 1000f;

    float dirX = proj.DirectionX;
    float dirY = proj.DirectionY;
    float dirZ = proj.DirectionZ;

    if (proj.DropRate > 0)
    {
      dirY -= proj.DropRate * deltaSec;
      float mag = (float)Math.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
      if (mag > 1e-6f)
      {
        float inv = 1f / mag;
        dirX *= inv; dirY *= inv; dirZ *= inv;
      }
    }

    float moveDistance = proj.Speed * deltaSec;

    var oldPos = proj.Position;
    var newPos = new DbVector3(
      oldPos.x + dirX * moveDistance,
      oldPos.y + dirY * moveDistance,
      oldPos.z + dirZ * moveDistance
    );
    float newDistTraveled = proj.DistanceTraveled + moveDistance;

    if (newPos.y < 0f)
    {
      ctx.Db.projectile.Id.Delete(proj.Id);
      return;
    }

    if (IsOutOfMapBounds(ctx, newPos, proj.GameSessionId))
    {
      ctx.Db.projectile.Id.Delete(proj.Id);
      return;
    }

    if (SegmentBlockedByTerrain(ctx, oldPos, newPos, proj.GameSessionId, proj.CasterTeamSlot))
    {
      ctx.Db.projectile.Id.Delete(proj.Id);
      return;
    }

    if (proj.DetonateAtMaxRange && newDistTraveled >= proj.MaxRange)
    {
      if (proj.Radius > 0)
      {
        ApplyProjectileHit(ctx, proj, 0, newPos);
      }
      ctx.Db.projectile.Id.Delete(proj.Id);
      return;
    }

    ctx.Db.projectile.Id.Update(proj with
    {
      Position = newPos,
      DirectionX = dirX,
      DirectionY = dirY,
      DirectionZ = dirZ,
      DistanceTraveled = newDistTraveled,
    });

    ctx.Db.projectile_tick.Insert(new ProjectileTickSchedule
    {
      Id = 0,
      ProjectileId = proj.Id,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(PROJECTILE_TICK_MS)),
    });
  }

  static void SpawnProjectile(ReducerContext ctx, ulong gameId, ulong casterEntityId, ulong abilityId,
    DbVector3 origin, float dirX, float dirY, float dirZ,
    float speed, float radius, float maxRange,
    byte casterTeamSlot, int resolvedPower,
    bool allowSubSquadTargeting, DamageDistribution distribution,
    float dropRate = 0f, bool detonateAtMaxRange = false)
  {
    var proj = ctx.Db.projectile.Insert(new Projectile
    {
      Id = 0,
      GameSessionId = gameId,
      CasterEntityId = casterEntityId,
      AbilityId = abilityId,
      Position = origin,
      DirectionX = dirX,
      DirectionY = dirY,
      DirectionZ = dirZ,
      Speed = speed,
      Radius = radius,
      MaxRange = maxRange,
      DistanceTraveled = 0f,
      CasterTeamSlot = casterTeamSlot,
      ResolvedPower = resolvedPower,
      AllowSubSquadTargeting = allowSubSquadTargeting,
      Distribution = distribution,
      DropRate = dropRate,
      DetonateAtMaxRange = detonateAtMaxRange,
    });

    ctx.Db.projectile_tick.Insert(new ProjectileTickSchedule
    {
      Id = 0,
      ProjectileId = proj.Id,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(PROJECTILE_TICK_MS)),
    });
  }

  [SpacetimeDB.Reducer]
  public static void ReportProjectileHit(ReducerContext ctx, ulong gameId, ulong projectileId, ulong hitEntityId, DbVector3 hitPosition)
  {
    var projOpt = ctx.Db.projectile.Id.Find(projectileId);
    if (projOpt is not Projectile proj)
      throw new Exception("Projectile not found");

    if (proj.GameSessionId != gameId)
      throw new Exception("Projectile game session mismatch");

    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id)
      ?? throw new Exception("No active game player");
    if (gp.EntityId != proj.CasterEntityId)
      throw new Exception("You do not own this projectile");

    Log.Info($"[PROJ-DEBUG] ReportProjectileHit: player={player.Name} projId={projectileId} casterEntityId={proj.CasterEntityId} casterTeam={proj.CasterTeamSlot} hitEntityId={hitEntityId}");

    float dx = hitPosition.x - proj.Position.x;
    float dy = hitPosition.y - proj.Position.y;
    float dz = hitPosition.z - proj.Position.z;
    float distSq = dx * dx + dy * dy + dz * dz;
    float tolerance = proj.Speed * (PROJECTILE_TICK_MS / 1000f) * 8f;
    if (distSq > tolerance * tolerance)
      throw new Exception("Hit position too far from projectile");

    var hitEnt = ctx.Db.entity.EntityId.Find(hitEntityId);
    if (hitEnt is not Entity he)
      throw new Exception("Hit entity not found");
    if (he.GameSessionId != gameId)
      throw new Exception("Hit entity not in same game");

    Log.Info($"[PROJ-DEBUG]   hitEntity: type={he.Type} teamSlot={he.TeamSlot} casterTeam={proj.CasterTeamSlot}");

    if (he.TeamSlot != 0 && he.TeamSlot == proj.CasterTeamSlot)
      throw new Exception("Cannot hit friendly entity");

    if (hitEntityId == proj.CasterEntityId)
      throw new Exception("Cannot hit own player entity");

    var casterGp = ctx.Db.game_player.EntityId.Find(proj.CasterEntityId);
    if (casterGp is GamePlayer cgp)
    {
      var hitSoldier = ctx.Db.soldier.EntityId.Find(hitEntityId);
      if (hitSoldier is Soldier hs && hs.OwnerPlayerId == cgp.PlayerId)
      {
        Log.Warn($"[PROJ-DEBUG]   REJECTED: Own soldier hit (soldierEntityId={hitEntityId} ownerPlayerId={hs.OwnerPlayerId} casterPlayerId={cgp.PlayerId})");
        throw new Exception("Cannot hit own soldier");
      }
    }

    var tgt = ctx.Db.targetable.EntityId.Find(hitEntityId);
    if (tgt is not Targetable t || t.Dead)
      throw new Exception("Target is dead or not targetable");

    Log.Info($"[PROJ-DEBUG]   APPLYING HIT: projId={projectileId} hitEntityId={hitEntityId} type={he.Type} power={proj.ResolvedPower}");

    ApplyProjectileHit(ctx, proj, hitEntityId, hitPosition);
    ctx.Db.projectile.Id.Delete(proj.Id);
  }
}
