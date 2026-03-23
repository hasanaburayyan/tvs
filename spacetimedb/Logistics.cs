using SpacetimeDB;

public static partial class Module
{
  const float RoadConnectionRange = 2f;
  const float ResupplyRange = 10f;
  const ulong LogisticsTickIntervalMs = 1000;
  const byte MaxRoadLevel = 3;
  const byte MaxBaseLevel = 3;
  const int HomeBaseSuppliesMax = 500;
  const float HomeBaseGenerationPerSecond = 5f;
  const int FobSuppliesMax = 100;
  const int FobInitialStash = 50;

  static float ThroughputForLevel(byte level)
  {
    return level switch
    {
      1 => 0.10f,
      2 => 0.20f,
      3 => 0.35f,
      _ => 0f,
    };
  }

  static int RoadUpgradeCost(byte currentLevel) => 10 * currentLevel;
  static int BaseUpgradeCost(byte currentLevel) => 15 * currentLevel;

  // --- Connectivity ---

  struct PathResult
  {
    public bool Connected;
    public byte MinRoadLevel;
  }

  static PathResult IsConnectedToHomeBase(ReducerContext ctx, ulong entityId, ulong gameSessionId, byte teamSlot)
  {
    var logisticsNodes = new Dictionary<ulong, LogisticsNode>();
    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameSessionId))
    {
      if (ent.Type != EntityType.Terrain) continue;
      if (ent.TeamSlot != teamSlot && ent.TeamSlot != 0) continue;

      var tf = ctx.Db.terrain_feature.EntityId.Find(ent.EntityId);
      if (tf is not TerrainFeature feat) continue;

      bool isRoad = ctx.Db.road_segment.EntityId.Find(ent.EntityId) is RoadSegment;
      bool isBase = feat.Type == TerrainType.CommandCenter || feat.Type == TerrainType.Outpost;
      if (!isRoad && !isBase) continue;

      var (hw, hd) = WorldExtents(feat.SizeX, feat.SizeZ, ent.RotationY);
      logisticsNodes[ent.EntityId] = new LogisticsNode
      {
        EntityId = ent.EntityId,
        CenterX = ent.Position.x, CenterZ = ent.Position.z,
        HalfW = hw, HalfD = hd,
        IsCommandCenter = feat.Type == TerrainType.CommandCenter,
      };
    }

    if (!logisticsNodes.ContainsKey(entityId))
      return new PathResult { Connected = false, MinRoadLevel = 0 };

    var visited = new HashSet<ulong>();
    return TraceToHomeBase(ctx, entityId, logisticsNodes, visited, byte.MaxValue);
  }

  struct LogisticsNode
  {
    public ulong EntityId;
    public float CenterX, CenterZ, HalfW, HalfD;
    public bool IsCommandCenter;
  }

  static PathResult TraceToHomeBase(ReducerContext ctx, ulong entityId,
    Dictionary<ulong, LogisticsNode> nodes, HashSet<ulong> visited, byte minLevel)
  {
    if (!visited.Add(entityId)) return new PathResult { Connected = false, MinRoadLevel = 0 };

    var node = nodes[entityId];
    if (node.IsCommandCenter)
      return new PathResult { Connected = true, MinRoadLevel = minLevel };

    byte effectiveMin = minLevel;
    if (ctx.Db.road_segment.EntityId.Find(entityId) is RoadSegment road)
      effectiveMin = minLevel == byte.MaxValue ? road.Level : Math.Min(minLevel, road.Level);

    foreach (var neighbor in nodes.Values)
    {
      if (neighbor.EntityId == entityId) continue;
      if (visited.Contains(neighbor.EntityId)) continue;

      float gap = EdgeToEdgeGap(node.CenterX, node.CenterZ, node.HalfW, node.HalfD,
                                neighbor.CenterX, neighbor.CenterZ, neighbor.HalfW, neighbor.HalfD);
      if (gap > RoadConnectionRange) continue;

      var result = TraceToHomeBase(ctx, neighbor.EntityId, nodes, visited, effectiveMin);
      if (result.Connected) return result;
    }

    return new PathResult { Connected = false, MinRoadLevel = 0 };
  }

  static (float halfW, float halfD) WorldExtents(float sizeX, float sizeZ, float rotDeg)
  {
    float rad = rotDeg * MathF.PI / 180f;
    float cos = MathF.Abs(MathF.Cos(rad));
    float sin = MathF.Abs(MathF.Sin(rad));
    float halfW = sizeX / 2f * cos + sizeZ / 2f * sin;
    float halfD = sizeX / 2f * sin + sizeZ / 2f * cos;
    return (halfW, halfD);
  }

  static float EdgeToEdgeGap(float cx1, float cz1, float hw1, float hd1,
                              float cx2, float cz2, float hw2, float hd2)
  {
    float gapX = MathF.Max(0f, MathF.Abs(cx1 - cx2) - hw1 - hw2);
    float gapZ = MathF.Max(0f, MathF.Abs(cz1 - cz2) - hd1 - hd2);
    return MathF.Sqrt(gapX * gapX + gapZ * gapZ);
  }

  static ulong? FindNearestConnectable(ReducerContext ctx, ulong gameSessionId, byte teamSlot,
                                        float posX, float posZ, float newRoadSizeX, float newRoadSizeZ, float newRoadRotY)
  {
    var (roadHW, roadHD) = WorldExtents(newRoadSizeX, newRoadSizeZ, newRoadRotY);

    ulong? bestId = null;
    float bestGap = float.MaxValue;

    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameSessionId))
    {
      if (ent.Type != EntityType.Terrain) continue;
      if (ent.TeamSlot != teamSlot && ent.TeamSlot != 0) continue;

      bool isValid = false;
      if (ctx.Db.road_segment.EntityId.Find(ent.EntityId) is RoadSegment)
        isValid = true;
      else if (ctx.Db.terrain_feature.EntityId.Find(ent.EntityId) is TerrainFeature tf)
      {
        if (tf.Type == TerrainType.CommandCenter || tf.Type == TerrainType.Outpost)
        {
          var tgt = ctx.Db.targetable.EntityId.Find(ent.EntityId);
          if (tgt is Targetable t && !t.Dead && t.Health > 0)
            isValid = true;
        }
      }

      if (!isValid) continue;

      var parentTf = ctx.Db.terrain_feature.EntityId.Find(ent.EntityId);
      if (parentTf is not TerrainFeature ptf) continue;

      var (parentHW, parentHD) = WorldExtents(ptf.SizeX, ptf.SizeZ, ent.RotationY);
      float gap = EdgeToEdgeGap(posX, posZ, roadHW, roadHD, ent.Position.x, ent.Position.z, parentHW, parentHD);

      if (gap <= RoadConnectionRange && gap < bestGap)
      {
        bestGap = gap;
        bestId = ent.EntityId;
      }
    }

    return bestId;
  }

  // --- Road Placement (called from UseAbility) ---

  static void PlaceRoad(ReducerContext ctx, ulong gameSessionId, ulong casterEntityId, byte teamSlot, DbVector3 pos, float rotationY, AbilityDef ability)
  {
    var parentId = FindNearestConnectable(ctx, gameSessionId, teamSlot, pos.x, pos.z, ability.TerrainSizeX, ability.TerrainSizeZ, rotationY)
      ?? throw new Exception("No road, base, or outpost within connection range");

    var terrainEnt = CreateEntity(ctx, gameSessionId, EntityType.Terrain, new DbVector3(pos.x, 0f, pos.z), rotationY, teamSlot);
    CreateTargetable(ctx, terrainEnt.EntityId, ability.TerrainMaxHealth, ability.TerrainMaxHealth, 0);
    ctx.Db.terrain_feature.Insert(new TerrainFeature
    {
      EntityId = terrainEnt.EntityId,
      Type = TerrainType.Road,
      SizeX = ability.TerrainSizeX,
      SizeY = ability.TerrainSizeY,
      SizeZ = ability.TerrainSizeZ,
      CasterEntityId = casterEntityId,
    });

    ctx.Db.road_segment.Insert(new RoadSegment
    {
      EntityId = terrainEnt.EntityId,
      GameSessionId = gameSessionId,
      TeamSlot = teamSlot,
      ConnectedToEntityId = parentId,
      Level = 1,
    });

    Log.Info($"Road placed at ({pos.x:F1}, {pos.z:F1}) connected to entity {parentId}");
  }

  // --- Upgrade Feature (called from UseAbility) ---

  static void HandleUpgradeFeature(ReducerContext ctx, ulong gameSessionId, ulong playerEntityId, DbVector3 targetPos)
  {
    var gpEnt = ctx.Db.entity.EntityId.Find(playerEntityId)!.Value;

    ulong? bestId = null;
    float bestDistSq = float.MaxValue;

    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameSessionId))
    {
      if (ent.Type != EntityType.Terrain) continue;
      if (ent.TeamSlot != gpEnt.TeamSlot && ent.TeamSlot != 0) continue;

      bool upgradable = false;
      if (ctx.Db.road_segment.EntityId.Find(ent.EntityId) is RoadSegment rs && rs.Level < MaxRoadLevel)
        upgradable = true;
      else if (ctx.Db.base_resource_store.EntityId.Find(ent.EntityId) is BaseResourceStore brs && brs.Level < MaxBaseLevel)
        upgradable = true;

      if (!upgradable) continue;

      float dx = ent.Position.x - targetPos.x;
      float dz = ent.Position.z - targetPos.z;
      float distSq = dx * dx + dz * dz;
      if (distSq < bestDistSq)
      {
        bestDistSq = distSq;
        bestId = ent.EntityId;
      }
    }

    if (bestId is not ulong upgradeTarget)
      throw new Exception("No upgradable feature near target position");

    if (ctx.Db.road_segment.EntityId.Find(upgradeTarget) is RoadSegment road)
    {
      int cost = RoadUpgradeCost(road.Level);
      var pool = FindResourcePool(ctx, playerEntityId, ResourceKind.Supplies)
        ?? throw new Exception("No supplies pool");
      if (pool.Current < cost)
        throw new Exception($"Insufficient supplies for road upgrade (need {cost}, have {pool.Current})");

      ctx.Db.resource_pool.Id.Update(pool with { Current = pool.Current - cost });
      ctx.Db.road_segment.EntityId.Update(road with { Level = (byte)(road.Level + 1) });
      Log.Info($"Road {upgradeTarget} upgraded to level {road.Level + 1}");
    }
    else if (ctx.Db.base_resource_store.EntityId.Find(upgradeTarget) is BaseResourceStore store)
    {
      int cost = BaseUpgradeCost(store.Level);
      var pool = FindResourcePool(ctx, playerEntityId, ResourceKind.Supplies)
        ?? throw new Exception("No supplies pool");
      if (pool.Current < cost)
        throw new Exception($"Insufficient supplies for base upgrade (need {cost}, have {pool.Current})");

      ctx.Db.resource_pool.Id.Update(pool with { Current = pool.Current - cost });

      int newMaxStorage = store.GenerationPerSecond > 0
        ? HomeBaseSuppliesMax + (store.Level * 100)
        : FobSuppliesMax + (store.Level * 50);

      ctx.Db.base_resource_store.EntityId.Update(store with
      {
        Level = (byte)(store.Level + 1),
        SuppliesMax = newMaxStorage,
      });
      Log.Info($"Base {upgradeTarget} upgraded to level {store.Level + 1}");
    }
  }

  // --- Resupply ---

  [SpacetimeDB.Reducer]
  public static void StartResupply(ReducerContext ctx, ulong gameId)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    var gpEnt = ctx.Db.entity.EntityId.Find(gp.EntityId)!.Value;
    if (gpEnt.GameSessionId != gameId)
      throw new Exception("Game session mismatch");

    var gpTarget = ctx.Db.targetable.EntityId.Find(gp.EntityId)!.Value;
    if (gpTarget.Dead)
      throw new Exception("Cannot resupply while dead");

    if (ctx.Db.resupply_session.EntityId.Find(gp.EntityId) is ResupplySession)
      throw new Exception("Already resupplying");

    float rangeSq = ResupplyRange * ResupplyRange;
    ulong? nearestBase = null;
    float nearestDistSq = float.MaxValue;

    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameId))
    {
      if (ent.Type != EntityType.Terrain) continue;
      if (ent.TeamSlot != gpEnt.TeamSlot && ent.TeamSlot != 0) continue;

      if (ctx.Db.base_resource_store.EntityId.Find(ent.EntityId) is not BaseResourceStore)
        continue;

      var tgt = ctx.Db.targetable.EntityId.Find(ent.EntityId);
      if (tgt is Targetable t && (t.Dead || t.Health <= 0))
        continue;

      float dx = ent.Position.x - gpEnt.Position.x;
      float dz = ent.Position.z - gpEnt.Position.z;
      float distSq = dx * dx + dz * dz;
      if (distSq <= rangeSq && distSq < nearestDistSq)
      {
        nearestDistSq = distSq;
        nearestBase = ent.EntityId;
      }
    }

    if (nearestBase is not ulong baseId)
      throw new Exception("No friendly base within range");

    ctx.Db.resupply_session.Insert(new ResupplySession
    {
      EntityId = gp.EntityId,
      BaseEntityId = baseId,
    });

    Log.Info($"Player {player.Name} started resupplying from base {baseId}");
  }

  [SpacetimeDB.Reducer]
  public static void StopResupply(ReducerContext ctx, ulong gameId)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    ctx.Db.resupply_session.EntityId.Delete(gp.EntityId);
    Log.Info($"Player {player.Name} stopped resupplying");
  }

  // --- Logistics Tick ---

  [SpacetimeDB.Reducer]
  public static void LogisticsTick(ReducerContext ctx, LogisticsTickSchedule tick)
  {
    if (ctx.Db.game_session.Id.Find(tick.GameSessionId) is not GameSession session)
      return;
    if (session.State == SessionState.Ended)
      return;

    // Phase 1: Home base generation
    foreach (var store in ctx.Db.base_resource_store.GameSessionId.Filter(tick.GameSessionId))
    {
      if (store.GenerationPerSecond <= 0) continue;
      float generated = store.GenerationPerSecond * store.Level;
      int newSupplies = Math.Min(store.SuppliesMax, store.Supplies + (int)generated);
      if (newSupplies != store.Supplies)
        ctx.Db.base_resource_store.EntityId.Update(store with { Supplies = newSupplies });
    }

    // Phase 2: Transfer to connected FOBs
    foreach (var store in ctx.Db.base_resource_store.GameSessionId.Filter(tick.GameSessionId))
    {
      if (store.GenerationPerSecond > 0) continue;
      if (store.Supplies >= store.SuppliesMax) continue;

      var path = IsConnectedToHomeBase(ctx, store.EntityId, tick.GameSessionId, store.TeamSlot);
      if (!path.Connected) continue;

      float throughput = ThroughputForLevel(path.MinRoadLevel);
      var homeStore = FindHomeBaseStore(ctx, tick.GameSessionId, store.TeamSlot);
      if (homeStore is not BaseResourceStore home || home.Supplies <= 0) continue;

      float baseGen = home.GenerationPerSecond * home.Level;
      int transferAmount = Math.Max(1, (int)(baseGen * throughput));
      transferAmount = Math.Min(transferAmount, home.Supplies);
      transferAmount = Math.Min(transferAmount, store.SuppliesMax - store.Supplies);

      if (transferAmount > 0)
      {
        ctx.Db.base_resource_store.EntityId.Update(home with { Supplies = home.Supplies - transferAmount });
        ctx.Db.base_resource_store.EntityId.Update(store with { Supplies = store.Supplies + transferAmount });
      }
    }

    // Phase 3: Resupply players holding F
    foreach (var rs in ctx.Db.resupply_session.Iter())
    {
      var gpEnt = ctx.Db.entity.EntityId.Find(rs.EntityId);
      if (gpEnt is not Entity pe || pe.GameSessionId != tick.GameSessionId) continue;

      var gpTarget = ctx.Db.targetable.EntityId.Find(rs.EntityId);
      if (gpTarget is Targetable gt && gt.Dead)
      {
        ctx.Db.resupply_session.EntityId.Delete(rs.EntityId);
        continue;
      }

      var baseEnt = ctx.Db.entity.EntityId.Find(rs.BaseEntityId);
      if (baseEnt is not Entity be)
      {
        ctx.Db.resupply_session.EntityId.Delete(rs.EntityId);
        continue;
      }

      float dx = pe.Position.x - be.Position.x;
      float dz = pe.Position.z - be.Position.z;
      if (dx * dx + dz * dz > ResupplyRange * ResupplyRange)
      {
        ctx.Db.resupply_session.EntityId.Delete(rs.EntityId);
        continue;
      }

      var baseStore = ctx.Db.base_resource_store.EntityId.Find(rs.BaseEntityId);
      if (baseStore is not BaseResourceStore bs || bs.Supplies <= 0) continue;

      var supplyPool = FindResourcePool(ctx, rs.EntityId, ResourceKind.Supplies);
      if (supplyPool is not ResourcePool pool || pool.Current >= pool.Max) continue;

      int transferRate = Math.Max(1, pool.Max / 10);
      int transfer = Math.Min(transferRate, pool.Max - pool.Current);
      transfer = Math.Min(transfer, bs.Supplies);

      if (transfer > 0)
      {
        ctx.Db.resource_pool.Id.Update(pool with { Current = pool.Current + transfer });
        ctx.Db.base_resource_store.EntityId.Update(bs with { Supplies = bs.Supplies - transfer });
      }
    }

    ctx.Db.logistics_tick.Insert(new LogisticsTickSchedule
    {
      Id = 0,
      GameSessionId = tick.GameSessionId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(LogisticsTickIntervalMs)),
    });
  }

  static BaseResourceStore? FindHomeBaseStore(ReducerContext ctx, ulong gameSessionId, byte teamSlot)
  {
    foreach (var store in ctx.Db.base_resource_store.GameSessionId.Filter(gameSessionId))
    {
      if (store.GenerationPerSecond > 0 && store.TeamSlot == teamSlot)
        return store;
    }
    return null;
  }

  static void ScheduleLogisticsTick(ReducerContext ctx, ulong gameSessionId)
  {
    ctx.Db.logistics_tick.Insert(new LogisticsTickSchedule
    {
      Id = 0,
      GameSessionId = gameSessionId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(LogisticsTickIntervalMs)),
    });
  }

  // --- Road Destruction ---

  static void OnRoadDestroyed(ReducerContext ctx, ulong entityId)
  {
    ctx.Db.road_segment.EntityId.Delete(entityId);
  }
}
