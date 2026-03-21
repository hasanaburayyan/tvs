using SpacetimeDB;

public static partial class Module
{
  const float DEFAULT_COHESION_RADIUS = 5.0f;
  const float SPLIT_HYSTERESIS = 1.5f;
  const int SOLDIER_HEALTH = 15;
  const int SOLDIER_ARMOR = 0;
  const float FORMATION_DISTANCE = 1.5f;

  public static ulong CreatePlayerSquad(ReducerContext ctx, ulong gameSessionId, ulong playerId, ulong gamePlayerId, DbVector3 spawnPosition)
  {
    var soldier0 = ctx.Db.soldier.Insert(new Soldier
    {
      Id = 0,
      GameSessionId = gameSessionId,
      OwnerPlayerId = playerId,
      Health = SOLDIER_HEALTH,
      MaxHealth = SOLDIER_HEALTH,
      Armor = SOLDIER_ARMOR,
      Position = spawnPosition + DeterministicFormationOffset(0, 0f),
      RotationY = 0f,
      FormationIndex = 0,
    });

    var soldier1 = ctx.Db.soldier.Insert(new Soldier
    {
      Id = 0,
      GameSessionId = gameSessionId,
      OwnerPlayerId = playerId,
      Health = SOLDIER_HEALTH,
      MaxHealth = SOLDIER_HEALTH,
      Armor = SOLDIER_ARMOR,
      Position = spawnPosition + DeterministicFormationOffset(1, 0f),
      RotationY = 0f,
      FormationIndex = 1,
    });

    var leafPlayer = ctx.Db.squad.Insert(new Squad
    {
      Id = 0,
      GameSessionId = gameSessionId,
      ParentSquadId = 0,
      OwnerPlayerId = playerId,
      CohesionRadius = DEFAULT_COHESION_RADIUS,
      CenterPosition = spawnPosition,
      GamePlayerId = gamePlayerId,
      SoldierId = 0,
    });

    var leafSoldier0 = ctx.Db.squad.Insert(new Squad
    {
      Id = 0,
      GameSessionId = gameSessionId,
      ParentSquadId = 0,
      OwnerPlayerId = playerId,
      CohesionRadius = DEFAULT_COHESION_RADIUS,
      CenterPosition = soldier0.Position,
      GamePlayerId = 0,
      SoldierId = soldier0.Id,
    });

    var leafSoldier1 = ctx.Db.squad.Insert(new Squad
    {
      Id = 0,
      GameSessionId = gameSessionId,
      ParentSquadId = 0,
      OwnerPlayerId = playerId,
      CohesionRadius = DEFAULT_COHESION_RADIUS,
      CenterPosition = soldier1.Position,
      GamePlayerId = 0,
      SoldierId = soldier1.Id,
    });

    var composite = ctx.Db.squad.Insert(new Squad
    {
      Id = 0,
      GameSessionId = gameSessionId,
      ParentSquadId = 0,
      OwnerPlayerId = playerId,
      CohesionRadius = DEFAULT_COHESION_RADIUS,
      CenterPosition = spawnPosition,
      GamePlayerId = 0,
      SoldierId = 0,
    });

    ctx.Db.squad.Id.Update(leafPlayer with { ParentSquadId = composite.Id });
    ctx.Db.squad.Id.Update(leafSoldier0 with { ParentSquadId = composite.Id });
    ctx.Db.squad.Id.Update(leafSoldier1 with { ParentSquadId = composite.Id });

    return composite.Id;
  }

  public static ulong CreateAiSquad(ReducerContext ctx, ulong gameSessionId, DbVector3 spawnPosition, int soldierCount)
  {
    var composite = ctx.Db.squad.Insert(new Squad
    {
      Id = 0,
      GameSessionId = gameSessionId,
      ParentSquadId = 0,
      OwnerPlayerId = null,
      CohesionRadius = DEFAULT_COHESION_RADIUS,
      CenterPosition = spawnPosition,
      GamePlayerId = 0,
      SoldierId = 0,
    });

    for (byte i = 0; i < soldierCount; i++)
    {
      var soldierPos = spawnPosition + DeterministicFormationOffset(i, 0f);
      var soldier = ctx.Db.soldier.Insert(new Soldier
      {
        Id = 0,
        GameSessionId = gameSessionId,
        OwnerPlayerId = null,
        Health = SOLDIER_HEALTH,
        MaxHealth = SOLDIER_HEALTH,
        Armor = SOLDIER_ARMOR,
        Position = soldierPos,
        RotationY = 0f,
        FormationIndex = i,
      });

      ctx.Db.squad.Insert(new Squad
      {
        Id = 0,
        GameSessionId = gameSessionId,
        ParentSquadId = composite.Id,
        OwnerPlayerId = null,
        CohesionRadius = DEFAULT_COHESION_RADIUS,
        CenterPosition = soldierPos,
        GamePlayerId = 0,
        SoldierId = soldier.Id,
      });
    }

    return composite.Id;
  }

  public static Squad? FindLeafSquadForGamePlayer(ReducerContext ctx, ulong gamePlayerId)
  {
    foreach (var sq in ctx.Db.squad.GamePlayerId.Filter(gamePlayerId))
    {
      if (sq.GamePlayerId != 0) return sq;
    }
    return null;
  }

  public static Squad? FindLeafSquadForSoldier(ReducerContext ctx, ulong soldierId)
  {
    foreach (var sq in ctx.Db.squad.SoldierId.Filter(soldierId))
    {
      if (sq.SoldierId != 0) return sq;
    }
    return null;
  }

  public static Squad? GetParentComposite(ReducerContext ctx, ulong squadId)
  {
    var sq = ctx.Db.squad.Id.Find(squadId);
    if (sq is not Squad s) return null;
    if (s.ParentSquadId == 0) return null;
    return ctx.Db.squad.Id.Find(s.ParentSquadId);
  }

  public static Squad GetRootSquad(ReducerContext ctx, ulong squadId)
  {
    var current = ctx.Db.squad.Id.Find(squadId) ?? throw new Exception($"Squad {squadId} not found");
    int depth = 0;
    while (current.ParentSquadId != 0)
    {
      current = ctx.Db.squad.Id.Find(current.ParentSquadId) ?? throw new Exception($"Squad {current.ParentSquadId} not found");
      if (++depth > 100) throw new Exception("Squad tree depth exceeded safety limit");
    }
    return current;
  }

  public static List<Squad> GetDirectChildren(ReducerContext ctx, ulong squadId)
  {
    var children = new List<Squad>();
    foreach (var sq in ctx.Db.squad.ParentSquadId.Filter(squadId))
      children.Add(sq);
    return children;
  }

  public static bool IsLeafSquad(Squad sq)
  {
    return sq.GamePlayerId != 0 || sq.SoldierId != 0;
  }

  public static List<Squad> GetLeafSquads(ReducerContext ctx, ulong squadId)
  {
    var leaves = new List<Squad>();
    var squad = ctx.Db.squad.Id.Find(squadId);
    if (squad is not Squad sq) return leaves;

    if (IsLeafSquad(sq))
    {
      leaves.Add(sq);
      return leaves;
    }

    var children = GetDirectChildren(ctx, sq.Id);
    foreach (var child in children)
    {
      leaves.AddRange(GetLeafSquads(ctx, child.Id));
    }
    return leaves;
  }

  public struct EntityRef
  {
    public ulong GamePlayerId;
    public ulong SoldierId;
  }

  public static List<EntityRef> GetAllEntities(ReducerContext ctx, ulong squadId)
  {
    var entities = new List<EntityRef>();
    var leaves = GetLeafSquads(ctx, squadId);
    foreach (var leaf in leaves)
    {
      entities.Add(new EntityRef { GamePlayerId = leaf.GamePlayerId, SoldierId = leaf.SoldierId });
    }
    return entities;
  }

  static DbVector3 GetEntityPosition(ReducerContext ctx, Squad leaf)
  {
    if (leaf.GamePlayerId != 0)
    {
      var gp = ctx.Db.game_player.Id.Find(leaf.GamePlayerId);
      if (gp is GamePlayer g) return g.Position;
    }
    if (leaf.SoldierId != 0)
    {
      var s = ctx.Db.soldier.Id.Find(leaf.SoldierId);
      if (s is Soldier sol) return sol.Position;
    }
    return leaf.CenterPosition;
  }

  public static DbVector3 ComputeSquadCenter(ReducerContext ctx, ulong squadId)
  {
    var leaves = GetLeafSquads(ctx, squadId);
    if (leaves.Count == 0) return new DbVector3(0, 0, 0);

    float sumX = 0, sumY = 0, sumZ = 0;
    int count = 0;
    foreach (var leaf in leaves)
    {
      bool isDead = false;
      if (leaf.GamePlayerId != 0)
      {
        var gp = ctx.Db.game_player.Id.Find(leaf.GamePlayerId);
        if (gp is GamePlayer g && g.Dead) isDead = true;
      }
      if (leaf.SoldierId != 0)
      {
        var s = ctx.Db.soldier.Id.Find(leaf.SoldierId);
        if (s is Soldier sol && sol.Dead) isDead = true;
      }
      if (isDead) continue;

      var pos = GetEntityPosition(ctx, leaf);
      sumX += pos.x;
      sumY += pos.y;
      sumZ += pos.z;
      count++;
    }

    if (count == 0) return new DbVector3(0, 0, 0);
    return new DbVector3(sumX / count, sumY / count, sumZ / count);
  }

  public static void UpdateSquadCenters(ReducerContext ctx, ulong leafSquadId)
  {
    var current = ctx.Db.squad.Id.Find(leafSquadId);
    if (current is not Squad sq) return;

    var center = ComputeSquadCenter(ctx, sq.Id);
    ctx.Db.squad.Id.Update(sq with { CenterPosition = center });

    var cur = sq;
    int depth = 0;
    while (cur.ParentSquadId != 0)
    {
      var parent = ctx.Db.squad.Id.Find(cur.ParentSquadId);
      if (parent is not Squad p) break;
      var parentCenter = ComputeSquadCenter(ctx, p.Id);
      ctx.Db.squad.Id.Update(p with { CenterPosition = parentCenter });
      cur = ctx.Db.squad.Id.Find(cur.ParentSquadId)!.Value;
      if (++depth > 100) break;
    }
  }

  public static DbVector3 DeterministicFormationOffset(byte formationIndex, float leaderRotationY)
  {
    float sin = (float)Math.Sin(leaderRotationY);
    float cos = (float)Math.Cos(leaderRotationY);

    float localX, localZ;
    switch (formationIndex)
    {
      case 0:
        localX = -FORMATION_DISTANCE;
        localZ = -FORMATION_DISTANCE;
        break;
      case 1:
        localX = FORMATION_DISTANCE;
        localZ = -FORMATION_DISTANCE;
        break;
      case 2:
        localX = 0;
        localZ = -FORMATION_DISTANCE * 2;
        break;
      default:
        localX = FORMATION_DISTANCE * ((formationIndex % 2 == 0) ? -1 : 1);
        localZ = -FORMATION_DISTANCE * (1 + formationIndex / 2);
        break;
    }

    float worldX = localX * cos - localZ * sin;
    float worldZ = localX * sin + localZ * cos;

    return new DbVector3(worldX, 0, worldZ);
  }

  public static void MoveSoldiersWithPlayer(ReducerContext ctx, ulong gamePlayerId, DbVector3 playerPosition, float rotationY)
  {
    var leafSquad = FindLeafSquadForGamePlayer(ctx, gamePlayerId);
    if (leafSquad is not Squad playerLeaf) return;

    if (playerLeaf.ParentSquadId == 0)
    {
      ctx.Db.squad.Id.Update(playerLeaf with { CenterPosition = playerPosition });
      return;
    }

    var siblings = GetDirectChildren(ctx, playerLeaf.ParentSquadId);
    foreach (var sibling in siblings)
    {
      if (sibling.SoldierId != 0)
      {
        var soldier = ctx.Db.soldier.Id.Find(sibling.SoldierId);
        if (soldier is not Soldier s) continue;

        var offset = DeterministicFormationOffset(s.FormationIndex, rotationY);
        var newPos = playerPosition + offset;

        if (s.Dead)
        {
          ctx.Db.squad.Id.Update(sibling with { CenterPosition = newPos });
          continue;
        }

        ctx.Db.soldier.Id.Update(s with { Position = newPos, RotationY = rotationY });
        ctx.Db.squad.Id.Update(sibling with { CenterPosition = newPos });
      }
    }

    ctx.Db.squad.Id.Update(playerLeaf with { CenterPosition = playerPosition });
    UpdateSquadCenters(ctx, playerLeaf.Id);
  }

  public static void CleanupSquadsForGame(ReducerContext ctx, ulong gameSessionId)
  {
    foreach (var soldier in ctx.Db.soldier.GameSessionId.Filter(gameSessionId))
      ctx.Db.soldier.Id.Delete(soldier.Id);

    foreach (var squad in ctx.Db.squad.GameSessionId.Filter(gameSessionId))
      ctx.Db.squad.Id.Delete(squad.Id);
  }

  public static void CleanupAllSquadsForPlayer(ReducerContext ctx, ulong playerId)
  {
    var soldiersToDelete = new List<ulong>();
    var squadsToDelete = new List<ulong>();

    foreach (var soldier in ctx.Db.soldier.Iter())
    {
      if (soldier.OwnerPlayerId == playerId)
        soldiersToDelete.Add(soldier.Id);
    }
    foreach (var squad in ctx.Db.squad.Iter())
    {
      if (squad.OwnerPlayerId == playerId)
        squadsToDelete.Add(squad.Id);
    }

    foreach (var id in soldiersToDelete)
      ctx.Db.soldier.Id.Delete(id);
    foreach (var id in squadsToDelete)
      ctx.Db.squad.Id.Delete(id);
  }

  public static void CleanupPlayerSquad(ReducerContext ctx, ulong playerId, ulong gameSessionId)
  {
    var squadsToDelete = new List<ulong>();
    var soldiersToDelete = new List<ulong>();

    foreach (var squad in ctx.Db.squad.GameSessionId.Filter(gameSessionId))
    {
      if (squad.OwnerPlayerId != playerId) continue;

      if (squad.SoldierId != 0)
        soldiersToDelete.Add(squad.SoldierId);

      squadsToDelete.Add(squad.Id);
    }

    ulong compositeParentId = 0;
    foreach (var squad in ctx.Db.squad.GameSessionId.Filter(gameSessionId))
    {
      if (squad.OwnerPlayerId == playerId && !IsLeafSquad(squad) && squad.ParentSquadId != 0)
      {
        compositeParentId = squad.ParentSquadId;
        break;
      }
    }

    foreach (var soldierId in soldiersToDelete)
      ctx.Db.soldier.Id.Delete(soldierId);

    foreach (var squadId in squadsToDelete)
      ctx.Db.squad.Id.Delete(squadId);

    if (compositeParentId != 0)
      TryDissolveIfSingleChild(ctx, compositeParentId);
  }

  static void TryDissolveIfSingleChild(ReducerContext ctx, ulong compositeSquadId)
  {
    var parent = ctx.Db.squad.Id.Find(compositeSquadId);
    if (parent is not Squad p) return;
    if (IsLeafSquad(p)) return;

    var children = GetDirectChildren(ctx, compositeSquadId);
    if (children.Count == 0)
    {
      var grandparentId = p.ParentSquadId;
      ctx.Db.squad.Id.Delete(compositeSquadId);
      if (grandparentId != 0)
        TryDissolveIfSingleChild(ctx, grandparentId);
    }
    else if (children.Count == 1)
    {
      var child = children[0];
      ctx.Db.squad.Id.Update(child with { ParentSquadId = p.ParentSquadId });
      ctx.Db.squad.Id.Delete(compositeSquadId);

      if (p.ParentSquadId != 0)
        TryDissolveIfSingleChild(ctx, p.ParentSquadId);
    }
  }

  public static void RespawnSoldiers(ReducerContext ctx, ulong gamePlayerId, DbVector3 spawnPosition)
  {
    var leafSquad = FindLeafSquadForGamePlayer(ctx, gamePlayerId);
    if (leafSquad is not Squad playerLeaf)
    {
      var gp = ctx.Db.game_player.Id.Find(gamePlayerId);
      if (gp is GamePlayer g)
        CreatePlayerSquad(ctx, g.GameSessionId, g.PlayerId, g.Id, spawnPosition);
      return;
    }

    if (playerLeaf.ParentSquadId == 0)
    {
      var gp = ctx.Db.game_player.Id.Find(gamePlayerId);
      if (gp is not GamePlayer g) return;

      var orphanedSquads = new List<ulong>();
      var orphanedSoldiers = new List<ulong>();
      foreach (var sq in ctx.Db.squad.GameSessionId.Filter(g.GameSessionId))
      {
        if (sq.OwnerPlayerId != g.PlayerId) continue;
        if (sq.SoldierId != 0) orphanedSoldiers.Add(sq.SoldierId);
        orphanedSquads.Add(sq.Id);
      }
      foreach (var sid in orphanedSoldiers)
      {
        ctx.Db.soldier.Id.Delete(sid);
        foreach (var c in ctx.Db.corpse.GameSessionId.Filter(g.GameSessionId))
        {
          if (c.SoldierId == sid) ctx.Db.corpse.Id.Delete(c.Id);
        }
      }
      foreach (var sqId in orphanedSquads)
        ctx.Db.squad.Id.Delete(sqId);

      CreatePlayerSquad(ctx, g.GameSessionId, g.PlayerId, g.Id, spawnPosition);
      return;
    }

    var siblings = GetDirectChildren(ctx, playerLeaf.ParentSquadId);
    foreach (var sibling in siblings)
    {
      if (sibling.SoldierId != 0)
      {
        var soldier = ctx.Db.soldier.Id.Find(sibling.SoldierId);
        if (soldier is not Soldier s) continue;

        var offset = DeterministicFormationOffset(s.FormationIndex, 0f);
        var newPos = spawnPosition + offset;
        ctx.Db.soldier.Id.Update(s with
        {
          Health = s.MaxHealth,
          Dead = false,
          DiedAt = null,
          Position = newPos,
          RotationY = 0f,
        });
        ctx.Db.squad.Id.Update(sibling with { CenterPosition = newPos });

        foreach (var c in ctx.Db.corpse.GameSessionId.Filter(s.GameSessionId))
        {
          if (c.SoldierId == s.Id)
            ctx.Db.corpse.Id.Delete(c.Id);
        }
      }
    }

    ctx.Db.squad.Id.Update(playerLeaf with { CenterPosition = spawnPosition });
    UpdateSquadCenters(ctx, playerLeaf.Id);
  }

  static float DistanceXZ(DbVector3 a, DbVector3 b)
  {
    float dx = a.x - b.x;
    float dz = a.z - b.z;
    return (float)Math.Sqrt(dx * dx + dz * dz);
  }

  public static byte? GetSquadTeamSlot(ReducerContext ctx, Squad squad, ulong gameSessionId)
  {
    var leaves = GetLeafSquads(ctx, squad.Id);
    foreach (var leaf in leaves)
    {
      if (leaf.GamePlayerId != 0)
      {
        var gp = ctx.Db.game_player.Id.Find(leaf.GamePlayerId);
        if (gp is GamePlayer g)
          return g.TeamSlot;
      }
    }
    return null;
  }

  public static void CheckAndMergeCohesion(ReducerContext ctx, ulong movedSquadId, ulong gameSessionId)
  {
    var movedRoot = GetRootSquad(ctx, movedSquadId);

    var rootSquads = new List<Squad>();
    foreach (var sq in ctx.Db.squad.GameSessionId.Filter(gameSessionId))
    {
      if (sq.ParentSquadId == 0 && sq.Id != movedRoot.Id)
        rootSquads.Add(sq);
    }

    foreach (var otherRoot in rootSquads)
    {
      var currentMoved = ctx.Db.squad.Id.Find(movedRoot.Id);
      if (currentMoved is not Squad cm) break;
      if (cm.ParentSquadId != 0) break;

      var currentOther = ctx.Db.squad.Id.Find(otherRoot.Id);
      if (currentOther is not Squad co) continue;
      if (co.ParentSquadId != 0) continue;

      byte? movedTeam = GetSquadTeamSlot(ctx, cm, gameSessionId);
      byte? otherTeam = GetSquadTeamSlot(ctx, co, gameSessionId);
      if (movedTeam != otherTeam) continue;

      float dist = DistanceXZ(cm.CenterPosition, co.CenterPosition);
      float mergeThreshold = cm.CohesionRadius + co.CohesionRadius;

      if (dist < mergeThreshold)
      {
        var center = (cm.CenterPosition + co.CenterPosition) / 2f;
        var newComposite = ctx.Db.squad.Insert(new Squad
        {
          Id = 0,
          GameSessionId = gameSessionId,
          ParentSquadId = 0,
          OwnerPlayerId = null,
          CohesionRadius = DEFAULT_COHESION_RADIUS,
          CenterPosition = center,
          GamePlayerId = 0,
          SoldierId = 0,
        });

        ctx.Db.squad.Id.Update(cm with { ParentSquadId = newComposite.Id });
        ctx.Db.squad.Id.Update(co with { ParentSquadId = newComposite.Id });

        movedRoot = ctx.Db.squad.Id.Find(newComposite.Id)!.Value;
      }
    }
  }

  static bool IsLeafDead(ReducerContext ctx, Squad leaf)
  {
    if (leaf.GamePlayerId != 0)
    {
      var gp = ctx.Db.game_player.Id.Find(leaf.GamePlayerId);
      if (gp is GamePlayer g && g.Dead) return true;
    }
    if (leaf.SoldierId != 0)
    {
      var s = ctx.Db.soldier.Id.Find(leaf.SoldierId);
      if (s is Soldier sol && sol.Dead) return true;
    }
    return false;
  }

  public static void CheckAndSplitCohesion(ReducerContext ctx, ulong squadId)
  {
    var root = GetRootSquad(ctx, squadId);
    CheckAndSplitRecursive(ctx, root.Id);
  }

  static void CheckAndSplitRecursive(ReducerContext ctx, ulong compositeId)
  {
    var composite = ctx.Db.squad.Id.Find(compositeId);
    if (composite is not Squad comp) return;
    if (IsLeafSquad(comp)) return;

    var children = GetDirectChildren(ctx, compositeId);
    if (children.Count <= 1) return;

    var aliveChildren = new List<Squad>();
    foreach (var child in children)
    {
      if (IsLeafSquad(child) && IsLeafDead(ctx, child)) continue;
      aliveChildren.Add(child);
    }
    if (aliveChildren.Count <= 1) return;

    var toDetach = new List<ulong>();
    for (int i = 0; i < aliveChildren.Count; i++)
    {
      for (int j = i + 1; j < aliveChildren.Count; j++)
      {
        var ci = ctx.Db.squad.Id.Find(aliveChildren[i].Id);
        var cj = ctx.Db.squad.Id.Find(aliveChildren[j].Id);
        if (ci is not Squad a || cj is not Squad b) continue;

        float dist = DistanceXZ(a.CenterPosition, b.CenterPosition);
        float splitThreshold = (a.CohesionRadius + b.CohesionRadius) * SPLIT_HYSTERESIS;

        if (dist > splitThreshold)
        {
          if (!toDetach.Contains(a.Id)) toDetach.Add(a.Id);
          if (!toDetach.Contains(b.Id)) toDetach.Add(b.Id);
        }
      }
    }

    foreach (var childId in toDetach)
    {
      var child = ctx.Db.squad.Id.Find(childId);
      if (child is Squad c && c.ParentSquadId == compositeId)
        ctx.Db.squad.Id.Update(c with { ParentSquadId = 0 });
    }

    TryDissolveIfSingleChild(ctx, compositeId);

    foreach (var childId in toDetach)
    {
      var child = ctx.Db.squad.Id.Find(childId);
      if (child is Squad c && !IsLeafSquad(c))
        CheckAndSplitRecursive(ctx, c.Id);
    }
  }

  const ulong AI_TICK_INTERVAL_MS = 1000;
  const float AI_MOVE_SPEED = 2.0f;
  const float AI_MAP_BOUND = 90f;

  public static void SpawnAiSquads(ReducerContext ctx, ulong gameSessionId, int count)
  {
    var session = ctx.Db.game_session.Id.Find(gameSessionId);
    if (session is not GameSession gs) return;
    uint seed = gs.MapSeed;

    for (int i = 0; i < count; i++)
    {
      uint h = seed ^ (uint)(i * 2654435761);
      h = (h ^ (h >> 16)) * 0x45d9f3b;
      h = (h ^ (h >> 16)) * 0x45d9f3b;
      h = h ^ (h >> 16);

      float x = ((h % 1000) / 1000f - 0.5f) * AI_MAP_BOUND * 2f;
      float z = (((h >> 10) % 1000) / 1000f - 0.5f) * AI_MAP_BOUND * 2f;
      float spawnY = 3f;

      int soldierCount = 2 + (int)(h % 2);
      CreateAiSquad(ctx, gameSessionId, new DbVector3(x, spawnY, z), soldierCount);
    }

    ScheduleAiSquadTick(ctx, gameSessionId);
  }

  static void ScheduleAiSquadTick(ReducerContext ctx, ulong gameSessionId)
  {
    ctx.Db.ai_squad_tick.Insert(new AiSquadTickSchedule
    {
      Id = 0,
      GameSessionId = gameSessionId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(AI_TICK_INTERVAL_MS)),
    });
  }

  static uint DeterministicHash(ulong a, ulong b)
  {
    uint h = (uint)(a ^ (b * 2654435761));
    h = (h ^ (h >> 16)) * 0x45d9f3b;
    h = (h ^ (h >> 16)) * 0x45d9f3b;
    return h ^ (h >> 16);
  }

  [SpacetimeDB.Reducer]
  public static void AiSquadTick(ReducerContext ctx, AiSquadTickSchedule schedule)
  {
    var session = ctx.Db.game_session.Id.Find(schedule.GameSessionId);
    if (session is not GameSession gs || gs.State != SessionState.InProgress)
      return;

    ulong timeBucket = (ulong)(ctx.Timestamp.MicrosecondsSinceUnixEpoch / 2_000_000);

    var aiRootSquads = new List<Squad>();
    foreach (var sq in ctx.Db.squad.GameSessionId.Filter(schedule.GameSessionId))
    {
      if (sq.ParentSquadId == 0 && sq.OwnerPlayerId == null && !IsLeafSquad(sq))
        aiRootSquads.Add(sq);
    }

    foreach (var aiRoot in aiRootSquads)
    {
      uint hash = DeterministicHash(aiRoot.Id, timeBucket);
      float angle = (hash % 628) / 100f;
      float dx = (float)Math.Cos(angle) * AI_MOVE_SPEED;
      float dz = (float)Math.Sin(angle) * AI_MOVE_SPEED;

      var leaves = GetLeafSquads(ctx, aiRoot.Id);
      foreach (var leaf in leaves)
      {
        if (leaf.SoldierId != 0)
        {
          var soldier = ctx.Db.soldier.Id.Find(leaf.SoldierId);
          if (soldier is not Soldier s || s.Dead) continue;

          float newX = Math.Clamp(s.Position.x + dx, -AI_MAP_BOUND, AI_MAP_BOUND);
          float newZ = Math.Clamp(s.Position.z + dz, -AI_MAP_BOUND, AI_MAP_BOUND);
          var newPos = new DbVector3(newX, s.Position.y, newZ);
          float newRotY = angle;

          ctx.Db.soldier.Id.Update(s with { Position = newPos, RotationY = newRotY });
          ctx.Db.squad.Id.Update(leaf with { CenterPosition = newPos });
        }
      }

      UpdateSquadCenters(ctx, aiRoot.Id);
      CheckAndMergeCohesion(ctx, aiRoot.Id, schedule.GameSessionId);
    }

    ScheduleAiSquadTick(ctx, schedule.GameSessionId);
  }

  [SpacetimeDB.Reducer]
  public static void SplitOwnedSquads(ReducerContext ctx, ulong gameId)
  {
    var player = GetPlayerForSender(ctx);

    var ownedComposites = new List<Squad>();
    foreach (var sq in ctx.Db.squad.GameSessionId.Filter(gameId))
    {
      if (sq.OwnerPlayerId == player.Id && !IsLeafSquad(sq) && sq.ParentSquadId != 0)
        ownedComposites.Add(sq);
    }

    foreach (var composite in ownedComposites)
    {
      var current = ctx.Db.squad.Id.Find(composite.Id);
      if (current is not Squad c) continue;
      if (c.ParentSquadId == 0) continue;

      ctx.Db.squad.Id.Update(c with { ParentSquadId = 0 });
      TryDissolveIfSingleChild(ctx, c.ParentSquadId);
    }
  }
}
