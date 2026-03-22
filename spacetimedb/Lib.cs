using SpacetimeDB;

public static partial class Module
{
  public static Player GetPlayerForSender(ReducerContext ctx)
  {
    foreach (var p in ctx.Db.player.Iter())
    {
      if (p.ControllerIdentity == ctx.Sender)
        return p;
    }
    throw new Exception("No active player for this connection");
  }

  static GamePlayer? FindGamePlayer(ReducerContext ctx, ulong playerId, ulong gameSessionId)
  {
    foreach (var gp in ctx.Db.game_player.PlayerId.Filter(playerId))
    {
      var ent = ctx.Db.entity.EntityId.Find(gp.EntityId);
      if (ent is Entity e && e.GameSessionId == gameSessionId) return gp;
    }
    return null;
  }

  static GamePlayer? FindActiveGamePlayer(ReducerContext ctx, ulong playerId)
  {
    foreach (var gp in ctx.Db.game_player.PlayerId.Filter(playerId))
    {
      if (gp.Active) return gp;
    }
    return null;
  }

  static void ClearTargetsForEntity(ReducerContext ctx, ulong entityId, ulong gameSessionId)
  {
    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameSessionId))
    {
      if (ent.Type != EntityType.GamePlayer) continue;
      if (ctx.Db.game_player.EntityId.Find(ent.EntityId) is GamePlayer other && other.TargetEntityId == entityId)
        ctx.Db.game_player.EntityId.Update(other with { TargetEntityId = null });
    }
  }

  static void DeactivateGamePlayer(ReducerContext ctx, ulong playerId)
  {
    if (FindActiveGamePlayer(ctx, playerId) is GamePlayer gp)
    {
      var ent = ctx.Db.entity.EntityId.Find(gp.EntityId)!.Value;
      ClearTargetsForEntity(ctx, gp.EntityId, ent.GameSessionId);
      ctx.Db.game_player.EntityId.Update(gp with { Active = false, TargetEntityId = null });
    }
  }

  static void DeselectCurrentPlayer(ReducerContext ctx)
  {
    foreach (var p in ctx.Db.player.Iter())
    {
      if (p.ControllerIdentity != ctx.Sender) continue;
      DeactivateGamePlayer(ctx, p.Id);
      CleanupPositionOverrides(ctx, p.Id);
      ctx.Db.player.Id.Update(p with { Online = false, ControllerIdentity = null });
    }
  }

  [SpacetimeDB.Reducer]
  public static void SelectPlayer(ReducerContext ctx, ulong playerId)
  {
    var player = ctx.Db.player.Id.Find(playerId) ?? throw new Exception("Player not found");

    if (player.OwnerIdentity != ctx.Sender)
      throw new Exception("You do not own this player");

    if (player.Online)
      throw new Exception("Player is already online");

    DeselectCurrentPlayer(ctx);

    ctx.Db.player.Id.Update(player with { Online = true, ControllerIdentity = ctx.Sender });
  }

  [SpacetimeDB.Reducer]
  public static void DeselectPlayer(ReducerContext ctx)
  {
    DeselectCurrentPlayer(ctx);
  }

  [SpacetimeDB.Reducer]
  public static void CreatePlayer(ReducerContext ctx, string name)
  {
    DeselectCurrentPlayer(ctx);

    ctx.Db.player.Insert(new Player
    {
      Id = 0,
      OwnerIdentity = ctx.Sender,
      Name = name,
      Online = true,
      ControllerIdentity = ctx.Sender,
    });
  }

  [SpacetimeDB.Reducer]
  public static void DeletePlayer(ReducerContext ctx, ulong playerId)
  {
    var player = ctx.Db.player.Id.Find(playerId) ?? throw new Exception("Player not found");

    if (player.OwnerIdentity != ctx.Sender)
      throw new Exception("You do not own this player");

    if (player.Online)
      throw new Exception("Cannot delete an online player. Deselect first.");

    ctx.Db.player.Id.Delete(playerId);
  }

  [SpacetimeDB.Reducer]
  public static void CreateGame(ReducerContext ctx, uint maxPlayers, uint? respawnTimerSeconds, ulong mapDefId)
  {
    if (ctx.Db.map_def.Id.Find(mapDefId) is not MapDef)
      throw new Exception($"Map definition not found: {mapDefId}");

    var session = ctx.Db.game_session.Insert(new GameSession
    {
      OwnerIdentity = ctx.Sender,
      State = SessionState.Lobby,
      MaxPlayers = maxPlayers,
      CreatedAt = ctx.Timestamp,
      MapDefId = mapDefId,
      RespawnTimerSeconds = respawnTimerSeconds ?? 15,
    });
    GenerateMap(ctx, session.Id, mapDefId);
    SchedulePassiveRegen(ctx, session.Id);
    ScheduleLosCheck(ctx, session.Id);
  }

  [SpacetimeDB.Reducer]
  public static void CreateGameAndJoin(ReducerContext ctx, uint maxPlayers, uint? respawnTimerSeconds, ulong mapDefId)
  {
    if (ctx.Db.map_def.Id.Find(mapDefId) is not MapDef)
      throw new Exception($"Map definition not found: {mapDefId}");

    var session = ctx.Db.game_session.Insert(new GameSession
    {
      OwnerIdentity = ctx.Sender,
      State = SessionState.Lobby,
      MaxPlayers = maxPlayers,
      CreatedAt = ctx.Timestamp,
      MapDefId = mapDefId,
      RespawnTimerSeconds = respawnTimerSeconds ?? 15,
    });
    GenerateMap(ctx, session.Id, mapDefId);
    SchedulePassiveRegen(ctx, session.Id);
    ScheduleLosCheck(ctx, session.Id);
    JoinGame(ctx, session.Id);
  }

  [SpacetimeDB.Reducer]
  public static void StartGame(ReducerContext ctx, ulong gameId)
  {
    var session = ctx.Db.game_session.Id.Find(gameId)
      ?? throw new Exception("Game session not found");

    if (session.OwnerIdentity != ctx.Sender)
      throw new Exception("Only the game owner can start the game");

    if (session.State != SessionState.Lobby)
      throw new Exception("Game is not in lobby state");

    ctx.Db.game_session.Id.Update(session with { State = SessionState.InProgress });
    SpawnAiSquads(ctx, gameId, 3);
    Log.Info($"Game {gameId} started");
  }

  [SpacetimeDB.Reducer]
  public static void EndGame(ReducerContext ctx, ulong gameId)
  {
    var session = ctx.Db.game_session.Id.Find(gameId)
      ?? throw new Exception("Game session not found");

    if (session.OwnerIdentity != ctx.Sender)
      throw new Exception("Only the game owner can end the game");

    if (session.State == SessionState.Ended)
      throw new Exception("Game is already ended");

    ctx.Db.game_session.Id.Update(session with { State = SessionState.Ended });
    Log.Info($"Game {gameId} ended");
  }

  [SpacetimeDB.Reducer]
  public static void DeleteGame(ReducerContext ctx, ulong gameId)
  {
    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameId))
    {
      if (ent.Type == EntityType.Terrain)
      {
        foreach (var check in ctx.Db.terrain_expiry_check.Iter())
        {
          if (check.EntityId == ent.EntityId)
            ctx.Db.terrain_expiry_check.Id.Delete(check.Id);
        }
        foreach (var regen in ctx.Db.outpost_regen_tick.Iter())
        {
          if (regen.EntityId == ent.EntityId)
            ctx.Db.outpost_regen_tick.Id.Delete(regen.Id);
        }
        ctx.Db.terrain_feature.EntityId.Delete(ent.EntityId);
      }
      else if (ent.Type == EntityType.GamePlayer)
      {
        foreach (var pool in ctx.Db.resource_pool.EntityId.Filter(ent.EntityId))
          ctx.Db.resource_pool.Id.Delete(pool.Id);
        foreach (var cd in ctx.Db.ability_cooldown.EntityId.Filter(ent.EntityId))
          ctx.Db.ability_cooldown.Id.Delete(cd.Id);
        foreach (var effect in ctx.Db.active_effect.EntityId.Filter(ent.EntityId))
          ctx.Db.active_effect.Id.Delete(effect.Id);
        ctx.Db.game_player.EntityId.Delete(ent.EntityId);
      }
      else if (ent.Type == EntityType.Soldier)
      {
        ctx.Db.soldier.EntityId.Delete(ent.EntityId);
      }
      else if (ent.Type == EntityType.Corpse)
      {
        ctx.Db.corpse.EntityId.Delete(ent.EntityId);
      }
      else if (ent.Type == EntityType.CapturePoint)
      {
        ctx.Db.capture_point.EntityId.Delete(ent.EntityId);
      }

      ctx.Db.targetable.EntityId.Delete(ent.EntityId);
      ctx.Db.entity.EntityId.Delete(ent.EntityId);
    }

    foreach (var loadout in ctx.Db.loadout.GameSessionId.Filter(gameId))
      ctx.Db.loadout.Id.Delete(loadout.Id);

    foreach (var log in ctx.Db.battle_log.GameSessionId.Filter(gameId))
      ctx.Db.battle_log.Id.Delete(log.Id);

    foreach (var regen in ctx.Db.passive_regen_tick.Iter())
    {
      if (regen.GameSessionId == gameId)
        ctx.Db.passive_regen_tick.Id.Delete(regen.Id);
    }

    foreach (var los in ctx.Db.los_check_schedule.Iter())
    {
      if (los.GameSessionId == gameId)
        ctx.Db.los_check_schedule.Id.Delete(los.Id);
    }

    foreach (var ct in ctx.Db.capture_tick.Iter())
    {
      if (ct.GameSessionId == gameId)
        ctx.Db.capture_tick.Id.Delete(ct.Id);
    }

    CleanupSquadsForGame(ctx, gameId);

    foreach (var tick in ctx.Db.ai_squad_tick.Iter())
    {
      if (tick.GameSessionId == gameId)
        ctx.Db.ai_squad_tick.Id.Delete(tick.Id);
    }

    ctx.Db.game_session.Id.Delete(gameId);
  }

  const int floor_height = 2;

  static int CountActivePlayers(ReducerContext ctx, ulong gameSessionId)
  {
    int count = 0;
    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameSessionId))
    {
      if (ent.Type != EntityType.GamePlayer) continue;
      if (ctx.Db.game_player.EntityId.Find(ent.EntityId) is GamePlayer gp && gp.Active)
        count++;
    }
    return count;
  }

  [SpacetimeDB.Reducer]
  public static void JoinGame(ReducerContext ctx, ulong gameId)
  {
    var player = GetPlayerForSender(ctx);

    var gameSession = ctx.Db.game_session.Id.Find(gameId)
      ?? throw new Exception("Game session not found!");

    if (gameSession.State != SessionState.Lobby)
      throw new Exception("Joining a game in progress is not yet supported!");

    var existing = FindGamePlayer(ctx, player.Id, gameId);
    if (existing is GamePlayer ex)
    {
      if (ex.Active)
        throw new Exception("Already in this game!");

      if (gameSession.MaxPlayers <= CountActivePlayers(ctx, gameId))
        throw new Exception("Game session is full!");

      DeactivateGamePlayer(ctx, player.Id);
      ctx.Db.game_player.EntityId.Update(ex with { Active = true });
      var exEnt = ctx.Db.entity.EntityId.Find(ex.EntityId)!.Value;
      CreatePlayerSquad(ctx, gameSession.Id, player.Id, ex.EntityId, exEnt.Position);
      return;
    }

    if (gameSession.MaxPlayers <= CountActivePlayers(ctx, gameId))
      throw new Exception("Game session is full!");

    DeactivateGamePlayer(ctx, player.Id);

    float spawnY = floor_height + 1;
    float spawnX = 0;
    float spawnZ = 0;
    float offset = 3f;

    var try_spawn_location = bool () =>
    {
      var desired = new DbVector3(spawnX, spawnY, spawnZ);
      foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameId))
      {
        if (ent.Type != EntityType.GamePlayer) continue;
        if (ctx.Db.game_player.EntityId.Find(ent.EntityId) is GamePlayer gp2 && gp2.Active && ent.Position == desired)
          return false;
      }
      return true;
    };

    while (!try_spawn_location())
    {
      spawnX += offset;
    }

    var desired_spawn_location = new DbVector3(spawnX, spawnY, spawnZ);

    var entity = CreateEntity(ctx, gameSession.Id, EntityType.GamePlayer, desired_spawn_location, 0f, 0);
    CreateTargetable(ctx, entity.EntityId, 100, 100, 0);
    ctx.Db.game_player.Insert(new GamePlayer
    {
      EntityId = entity.EntityId,
      PlayerId = player.Id,
      Active = true,
    });

    CreatePlayerSquad(ctx, gameSession.Id, player.Id, entity.EntityId, desired_spawn_location);
  }

  [SpacetimeDB.Reducer]
  public static void KickPlayerFromGame(ReducerContext ctx, ulong gameId, string playerName)
  {
    var player = ctx.Db.player.Name.Find(playerName) ?? throw new Exception("Cannot find player to kick");
    var gamePlayer = FindGamePlayer(ctx, player.Id, gameId) ?? throw new Exception("Player is not in this game");

    if (gamePlayer.Active)
    {
      var ent = ctx.Db.entity.EntityId.Find(gamePlayer.EntityId)!.Value;
      ClearTargetsForEntity(ctx, gamePlayer.EntityId, ent.GameSessionId);
      ctx.Db.game_player.EntityId.Update(gamePlayer with { Active = false, TargetEntityId = null });
    }
    CleanupPlayerSquad(ctx, player.Id, gameId);
  }

  [SpacetimeDB.Reducer]
  public static void LeaveGame(ReducerContext ctx, ulong gameId)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindGamePlayer(ctx, player.Id, gameId) ?? throw new Exception("cannot find a game to leave");

    if (gp.Active)
    {
      var ent = ctx.Db.entity.EntityId.Find(gp.EntityId)!.Value;
      ClearTargetsForEntity(ctx, gp.EntityId, ent.GameSessionId);
      ctx.Db.game_player.EntityId.Update(gp with { Active = false, TargetEntityId = null });
    }
    CleanupPlayerSquad(ctx, player.Id, gameId);
    CleanupPositionOverrides(ctx, player.Id);
  }

  [SpacetimeDB.Reducer]
  public static void SetTeam(ReducerContext ctx, ulong gameId, byte teamSlot)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    var ent = ctx.Db.entity.EntityId.Find(gp.EntityId)!.Value;
    if (ent.GameSessionId != gameId)
      throw new Exception("Game session mismatch");

    if (teamSlot > 2)
      throw new Exception("Invalid team slot (0=Neutral, 1=Entente, 2=Central)");

    var spawnPos = GetTeamSpawn(ctx, teamSlot);
    ctx.Db.entity.EntityId.Update(ent with { TeamSlot = teamSlot, Position = spawnPos });
    Log.Info($"Player {player.Name} joined team {teamSlot} in game {gameId}");
  }

  static DbVector3 GetTeamSpawn(ReducerContext ctx, byte teamSlot)
  {
    uint hash = (uint)(ctx.Timestamp.MicrosecondsSinceUnixEpoch & 0xFFFFFFFF);
    hash ^= hash >> 16;
    hash *= 0x45d9f3b;
    hash ^= hash >> 16;

    float offsetX = ((hash % 1000) / 1000f - 0.5f) * 30f;
    float offsetZ = (((hash >> 10) % 1000) / 1000f) * 10f;

    return teamSlot switch
    {
      1 => new DbVector3(offsetX, 3, -140f + offsetZ),
      2 => new DbVector3(offsetX, 3, 140f - offsetZ),
      _ => new DbVector3(0, 3, 0),
    };
  }

  [SpacetimeDB.Reducer]
  public static void Respawn(ReducerContext ctx, ulong gameId)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    var ent = ctx.Db.entity.EntityId.Find(gp.EntityId)!.Value;
    if (ent.GameSessionId != gameId)
      throw new Exception("Game session mismatch");

    var target = ctx.Db.targetable.EntityId.Find(gp.EntityId)!.Value;
    if (!target.Dead)
      throw new Exception("Player is not dead");

    var session = ctx.Db.game_session.Id.Find(gameId)
      ?? throw new Exception("Game session not found");

    if (target.DiedAt is Timestamp diedAt)
    {
      var requiredWait = TimeSpan.FromSeconds(session.RespawnTimerSeconds);
      if (ctx.Timestamp < diedAt + requiredWait)
        throw new Exception("Respawn timer has not expired yet");
    }

    var spawnPos = GetTeamSpawn(ctx, ent.TeamSlot);

    ctx.Db.entity.EntityId.Update(ent with { Position = spawnPos });
    ctx.Db.targetable.EntityId.Update(target with
    {
      Health = target.MaxHealth,
      Dead = false,
      DiedAt = null,
    });
    ctx.Db.game_player.EntityId.Update(gp with { TargetEntityId = null });

    ctx.Db.battle_log.Insert(new BattleLogEntry
    {
      Id = 0,
      GameSessionId = gameId,
      OccurredAt = ctx.Timestamp,
      EventType = BattleLogEventType.Revive,
      ActorEntityId = gp.EntityId,
      AbilityId = null,
      TargetEntityIds = new List<ulong> { gp.EntityId },
      ResolvedPower = 0,
    });

    ctx.Db.PositionOverride.Insert(new PositionOverride
    {
      Id = 0,
      PlayerId = player.Id,
      GameSessionId = gameId,
      Position = spawnPos,
    });

    foreach (var c in ctx.Db.entity.GameSessionId.Filter(gameId))
    {
      if (c.Type != EntityType.Corpse) continue;
      if (ctx.Db.corpse.EntityId.Find(c.EntityId) is Corpse corpse && corpse.SourceEntityId == gp.EntityId)
        ctx.Db.corpse.EntityId.Update(corpse with { SourceEntityId = null });
    }

    var loadout = FindLoadout(ctx, gameId, player.Id);
    if (loadout is Loadout lo)
    {
      var archetype = ctx.Db.archetype_def.Id.Find(lo.ArchetypeDefId);
      var weapon = ctx.Db.weapon_def.Id.Find(lo.WeaponDefId);
      var skill = ctx.Db.skill_def.Id.Find(lo.SkillDefId);
      if (archetype is ArchetypeDef a && weapon is WeaponDef w && skill is SkillDef s)
      {
        ClearResourcePools(ctx, gp.EntityId);
        SeedResourcePools(ctx, gp.EntityId, a, w, s);
      }
    }

    RespawnSoldiers(ctx, gp.EntityId, spawnPos);

    Log.Info($"Player {player.Name} respawned at team {ent.TeamSlot} spawn in game {gameId}");
  }

  [SpacetimeDB.Reducer]
  public static void SetTarget(ReducerContext ctx, ulong gameId, ulong? targetEntityId)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    var gpTarget = ctx.Db.targetable.EntityId.Find(gp.EntityId);
    if (gpTarget is Targetable t && t.Dead)
      throw new Exception("Cannot set target while dead");

    var gpEnt = ctx.Db.entity.EntityId.Find(gp.EntityId)!.Value;
    if (gpEnt.GameSessionId != gameId)
      throw new Exception("Game session mismatch");

    if (targetEntityId is ulong targetId)
    {
      var targetEnt = ctx.Db.entity.EntityId.Find(targetId) ?? throw new Exception("Target not found");
      if (targetEnt.GameSessionId != gameId)
        throw new Exception("Target is not in the same game");
      if (targetEnt.Type == EntityType.GamePlayer)
      {
        var targetGp = ctx.Db.game_player.EntityId.Find(targetId);
        if (targetGp is GamePlayer tgp && !tgp.Active)
          throw new Exception("Target is not active");
      }
      if (!HasLineOfSight(ctx, gpEnt.Position, targetEnt.Position, gameId))
        throw new Exception("Target is not in line of sight");
    }

    ctx.Db.game_player.EntityId.Update(gp with { TargetEntityId = targetEntityId });
  }

  [SpacetimeDB.Reducer(ReducerKind.ClientDisconnected)]
  public static void ClientDisconnected(ReducerContext ctx)
  {
    DeselectCurrentPlayer(ctx);
  }

  [SpacetimeDB.Reducer]
  public static void ClearData(ReducerContext ctx)
  {
    foreach (var player in ctx.Db.player.OwnerIdentity.Filter(ctx.Sender))
    {
      foreach (var gp in ctx.Db.game_player.PlayerId.Filter(player.Id))
      {
        foreach (var pool in ctx.Db.resource_pool.EntityId.Filter(gp.EntityId))
          ctx.Db.resource_pool.Id.Delete(pool.Id);
        foreach (var cd in ctx.Db.ability_cooldown.EntityId.Filter(gp.EntityId))
          ctx.Db.ability_cooldown.Id.Delete(cd.Id);
        foreach (var effect in ctx.Db.active_effect.EntityId.Filter(gp.EntityId))
          ctx.Db.active_effect.Id.Delete(effect.Id);
        ctx.Db.game_player.EntityId.Delete(gp.EntityId);
        DestroyEntity(ctx, gp.EntityId);
      }

      CleanupAllSquadsForPlayer(ctx, player.Id);

      foreach (var gameSession in ctx.Db.game_session.OwnerIdentity.Filter(ctx.Sender))
      {
        CleanupSquadsForGame(ctx, gameSession.Id);

        foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameSession.Id))
        {
          if (ent.Type == EntityType.GamePlayer)
          {
            if (ctx.Db.game_player.EntityId.Find(ent.EntityId) is GamePlayer gp2)
            {
              foreach (var pool in ctx.Db.resource_pool.EntityId.Filter(ent.EntityId))
                ctx.Db.resource_pool.Id.Delete(pool.Id);
              foreach (var cd in ctx.Db.ability_cooldown.EntityId.Filter(ent.EntityId))
                ctx.Db.ability_cooldown.Id.Delete(cd.Id);
              foreach (var effect in ctx.Db.active_effect.EntityId.Filter(ent.EntityId))
                ctx.Db.active_effect.Id.Delete(effect.Id);
              ctx.Db.game_player.EntityId.Delete(ent.EntityId);
            }
          }
          else if (ent.Type == EntityType.Terrain)
          {
            foreach (var check in ctx.Db.terrain_expiry_check.Iter())
            {
              if (check.EntityId == ent.EntityId)
                ctx.Db.terrain_expiry_check.Id.Delete(check.Id);
            }
            foreach (var regen in ctx.Db.outpost_regen_tick.Iter())
            {
              if (regen.EntityId == ent.EntityId)
                ctx.Db.outpost_regen_tick.Id.Delete(regen.Id);
            }
            ctx.Db.terrain_feature.EntityId.Delete(ent.EntityId);
          }
          else if (ent.Type == EntityType.Soldier)
          {
            ctx.Db.soldier.EntityId.Delete(ent.EntityId);
          }
          else if (ent.Type == EntityType.Corpse)
          {
            ctx.Db.corpse.EntityId.Delete(ent.EntityId);
          }
          else if (ent.Type == EntityType.CapturePoint)
          {
            ctx.Db.capture_point.EntityId.Delete(ent.EntityId);
          }

          ctx.Db.targetable.EntityId.Delete(ent.EntityId);
          ctx.Db.entity.EntityId.Delete(ent.EntityId);
        }

        foreach (var loadout in ctx.Db.loadout.GameSessionId.Filter(gameSession.Id))
          ctx.Db.loadout.Id.Delete(loadout.Id);
        foreach (var log in ctx.Db.battle_log.GameSessionId.Filter(gameSession.Id))
          ctx.Db.battle_log.Id.Delete(log.Id);

        foreach (var regen in ctx.Db.passive_regen_tick.Iter())
        {
          if (regen.GameSessionId == gameSession.Id)
            ctx.Db.passive_regen_tick.Id.Delete(regen.Id);
        }
        foreach (var los in ctx.Db.los_check_schedule.Iter())
        {
          if (los.GameSessionId == gameSession.Id)
            ctx.Db.los_check_schedule.Id.Delete(los.Id);
        }
        foreach (var tick in ctx.Db.ai_squad_tick.Iter())
        {
          if (tick.GameSessionId == gameSession.Id)
            ctx.Db.ai_squad_tick.Id.Delete(tick.Id);
        }
        foreach (var ct in ctx.Db.capture_tick.Iter())
        {
          if (ct.GameSessionId == gameSession.Id)
            ctx.Db.capture_tick.Id.Delete(ct.Id);
        }
        ctx.Db.game_session.Id.Delete(gameSession.Id);
      }

      CleanupPositionOverrides(ctx, player.Id);

      foreach (var membership in ctx.Db.ChatSessionPlayer.PlayerId.Filter(player.Id))
      {
        ctx.Db.ChatSessionPlayer.Id.Delete(membership.Id);
      }

      foreach (var chatSession in ctx.Db.ChatSession.OwnerPlayerId.Filter(player.Id))
      {
        ctx.Db.ChatSession.Id.Delete(chatSession.Id);
      }

      foreach (var message in ctx.Db.Message.SenderPlayerId.Filter(player.Id))
      {
        ctx.Db.Message.Id.Delete(message.Id);
      }

      ctx.Db.player.Id.Delete(player.Id);
    }
  }
}
