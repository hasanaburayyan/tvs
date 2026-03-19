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
      if (gp.GameSessionId == gameSessionId) return gp;
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

  static void ClearTargetsForGamePlayer(ReducerContext ctx, ulong gamePlayerId, ulong gameSessionId)
  {
    foreach (var other in ctx.Db.game_player.GameSessionId.Filter(gameSessionId))
    {
      if (other.TargetGamePlayerId == gamePlayerId)
        ctx.Db.game_player.Id.Update(other with { TargetGamePlayerId = null });
    }
  }

  static void DeactivateGamePlayer(ReducerContext ctx, ulong playerId)
  {
    if (FindActiveGamePlayer(ctx, playerId) is GamePlayer gp)
    {
      ClearTargetsForGamePlayer(ctx, gp.Id, gp.GameSessionId);
      ctx.Db.game_player.Id.Update(gp with { Active = false, TargetGamePlayerId = null });
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
  public static void CreateGame(ReducerContext ctx, uint maxPlayers, uint? respawnTimerSeconds)
  {
    var seed = (uint)(ctx.Timestamp.MicrosecondsSinceUnixEpoch & 0xFFFFFFFF);
    var session = ctx.Db.game_session.Insert(new GameSession
    {
      OwnerIdentity = ctx.Sender,
      State = SessionState.Lobby,
      MaxPlayers = maxPlayers,
      CreatedAt = ctx.Timestamp,
      MapSeed = seed,
      RespawnTimerSeconds = respawnTimerSeconds ?? 15,
    });
    GenerateMap(ctx, session.Id, seed);
    SchedulePassiveRegen(ctx, session.Id);
    ScheduleLosCheck(ctx, session.Id);
  }

  [SpacetimeDB.Reducer]
  public static void CreateGameAndJoin(ReducerContext ctx, uint maxPlayers, uint? respawnTimerSeconds)
  {
    var seed = (uint)(ctx.Timestamp.MicrosecondsSinceUnixEpoch & 0xFFFFFFFF);
    var session = ctx.Db.game_session.Insert(new GameSession
    {
      OwnerIdentity = ctx.Sender,
      State = SessionState.Lobby,
      MaxPlayers = maxPlayers,
      CreatedAt = ctx.Timestamp,
      MapSeed = seed,
      RespawnTimerSeconds = respawnTimerSeconds ?? 15,
    });
    GenerateMap(ctx, session.Id, seed);
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
    foreach (var feature in ctx.Db.terrain_feature.GameSessionId.Filter(gameId))
    {
      foreach (var check in ctx.Db.terrain_expiry_check.Iter())
      {
        if (check.TerrainFeatureId == feature.Id)
          ctx.Db.terrain_expiry_check.Id.Delete(check.Id);
      }
      foreach (var regen in ctx.Db.outpost_regen_tick.Iter())
      {
        if (regen.TerrainFeatureId == feature.Id)
          ctx.Db.outpost_regen_tick.Id.Delete(regen.Id);
      }
      ctx.Db.terrain_feature.Id.Delete(feature.Id);
    }

    foreach (var gp in ctx.Db.game_player.GameSessionId.Filter(gameId))
    {
      foreach (var pool in ctx.Db.resource_pool.GamePlayerId.Filter(gp.Id))
        ctx.Db.resource_pool.Id.Delete(pool.Id);
      foreach (var cd in ctx.Db.ability_cooldown.GamePlayerId.Filter(gp.Id))
        ctx.Db.ability_cooldown.Id.Delete(cd.Id);
      foreach (var effect in ctx.Db.active_effect.GamePlayerId.Filter(gp.Id))
        ctx.Db.active_effect.Id.Delete(effect.Id);
      ctx.Db.game_player.Id.Delete(gp.Id);
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

    foreach (var c in ctx.Db.corpse.GameSessionId.Filter(gameId))
      ctx.Db.corpse.Id.Delete(c.Id);

    foreach (var cp in ctx.Db.capture_point.GameSessionId.Filter(gameId))
      ctx.Db.capture_point.Id.Delete(cp.Id);

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
    foreach (var gp in ctx.Db.game_player.GameSessionId.Filter(gameSessionId))
    {
      if (gp.Active) count++;
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
      ctx.Db.game_player.Id.Update(ex with { Active = true });
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
      foreach (var other_player in ctx.Db.game_player.GameSessionId.Filter(gameId))
      {
        if (other_player.Active && other_player.Position == desired)
          return false;
      }
      return true;
    };

    while (!try_spawn_location())
    {
      spawnX += offset;
    }

    var desired_spawn_location = new DbVector3(spawnX, spawnY, spawnZ);

    var gp = ctx.Db.game_player.Insert(new GamePlayer
    {
      GameSessionId = gameSession.Id,
      PlayerId = player.Id,
      Active = true,
      Health = 100,
      MaxHealth = 100,
      Armor = 0,
      Position = desired_spawn_location,
      RotationY = 0f
    });

    CreatePlayerSquad(ctx, gameSession.Id, player.Id, gp.Id, desired_spawn_location);
  }

  [SpacetimeDB.Reducer]
  public static void KickPlayerFromGame(ReducerContext ctx, ulong gameId, string playerName)
  {
    var player = ctx.Db.player.Name.Find(playerName) ?? throw new Exception("Cannot find player to kick");
    var gamePlayer = FindGamePlayer(ctx, player.Id, gameId) ?? throw new Exception("Player is not in this game");

    if (gamePlayer.Active)
    {
      ClearTargetsForGamePlayer(ctx, gamePlayer.Id, gamePlayer.GameSessionId);
      ctx.Db.game_player.Id.Update(gamePlayer with { Active = false, TargetGamePlayerId = null });
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
      ClearTargetsForGamePlayer(ctx, gp.Id, gp.GameSessionId);
      ctx.Db.game_player.Id.Update(gp with { Active = false, TargetGamePlayerId = null });
    }
    CleanupPlayerSquad(ctx, player.Id, gameId);
    CleanupPositionOverrides(ctx, player.Id);
  }

  [SpacetimeDB.Reducer]
  public static void SetTeam(ReducerContext ctx, ulong gameId, byte teamSlot)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    if (gp.GameSessionId != gameId)
      throw new Exception("Game session mismatch");

    if (teamSlot > 2)
      throw new Exception("Invalid team slot (0=Neutral, 1=Entente, 2=Central)");

    ctx.Db.game_player.Id.Update(gp with { TeamSlot = teamSlot });
    Log.Info($"Player {player.Name} joined team {teamSlot} in game {gameId}");
  }

  static DbVector3 GetTeamSpawn(byte teamSlot)
  {
    return teamSlot switch
    {
      1 => new DbVector3(0, 3, -85),
      2 => new DbVector3(0, 3, 85),
      _ => new DbVector3(0, 3, 0),
    };
  }

  [SpacetimeDB.Reducer]
  public static void Respawn(ReducerContext ctx, ulong gameId)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    if (gp.GameSessionId != gameId)
      throw new Exception("Game session mismatch");

    if (!gp.Dead)
      throw new Exception("Player is not dead");

    var session = ctx.Db.game_session.Id.Find(gameId)
      ?? throw new Exception("Game session not found");

    if (gp.DiedAt is Timestamp diedAt)
    {
      var requiredWait = TimeSpan.FromSeconds(session.RespawnTimerSeconds);
      if (ctx.Timestamp < diedAt + requiredWait)
        throw new Exception("Respawn timer has not expired yet");
    }

    var spawnPos = GetTeamSpawn(gp.TeamSlot);

    ctx.Db.game_player.Id.Update(gp with
    {
      Health = gp.MaxHealth,
      Dead = false,
      DiedAt = null,
      Position = spawnPos,
      TargetGamePlayerId = null,
    });

    ctx.Db.battle_log.Insert(new BattleLogEntry
    {
      Id = 0,
      GameSessionId = gameId,
      OccurredAt = ctx.Timestamp,
      EventType = BattleLogEventType.Revive,
      ActorGamePlayerId = gp.Id,
      AbilityId = null,
      TargetGamePlayerIds = new List<ulong> { gp.Id },
      ResolvedPower = 0,
    });

    ctx.Db.PositionOverride.Insert(new PositionOverride
    {
      Id = 0,
      PlayerId = player.Id,
      GameSessionId = gameId,
      Position = spawnPos,
    });

    foreach (var c in ctx.Db.corpse.GameSessionId.Filter(gameId))
    {
      if (c.GamePlayerId == gp.Id)
        ctx.Db.corpse.Id.Update(c with { GamePlayerId = null });
    }

    var loadout = FindLoadout(ctx, gameId, player.Id);
    if (loadout is Loadout lo)
    {
      var archetype = ctx.Db.archetype_def.Id.Find(lo.ArchetypeDefId);
      var weapon = ctx.Db.weapon_def.Id.Find(lo.WeaponDefId);
      var skill = ctx.Db.skill_def.Id.Find(lo.SkillDefId);
      if (archetype is ArchetypeDef a && weapon is WeaponDef w && skill is SkillDef s)
      {
        ClearResourcePools(ctx, gp.Id);
        SeedResourcePools(ctx, gp.Id, a, w, s);
      }
    }

    RespawnSoldiers(ctx, gp.Id, spawnPos);

    Log.Info($"Player {player.Name} respawned at team {gp.TeamSlot} spawn in game {gameId}");
  }

  [SpacetimeDB.Reducer]
  public static void SetTarget(ReducerContext ctx, ulong gameId, ulong? targetGamePlayerId)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    if (gp.Dead)
      throw new Exception("Cannot set target while dead");

    if (gp.GameSessionId != gameId)
      throw new Exception("Game session mismatch");

    if (targetGamePlayerId is ulong targetId)
    {
      var target = ctx.Db.game_player.Id.Find(targetId) ?? throw new Exception("Target not found");
      if (target.GameSessionId != gameId)
        throw new Exception("Target is not in the same game");
      if (!target.Active)
        throw new Exception("Target is not active");
      if (!HasLineOfSight(ctx, gp.Position, target.Position, gameId))
        throw new Exception("Target is not in line of sight");
    }

    ctx.Db.game_player.Id.Update(gp with { TargetGamePlayerId = targetGamePlayerId });
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
        foreach (var pool in ctx.Db.resource_pool.GamePlayerId.Filter(gp.Id))
          ctx.Db.resource_pool.Id.Delete(pool.Id);
        foreach (var cd in ctx.Db.ability_cooldown.GamePlayerId.Filter(gp.Id))
          ctx.Db.ability_cooldown.Id.Delete(cd.Id);
        foreach (var effect in ctx.Db.active_effect.GamePlayerId.Filter(gp.Id))
          ctx.Db.active_effect.Id.Delete(effect.Id);
        ctx.Db.game_player.Id.Delete(gp.Id);
      }

      CleanupAllSquadsForPlayer(ctx, player.Id);

      foreach (var gameSession in ctx.Db.game_session.OwnerIdentity.Filter(ctx.Sender))
      {
        CleanupSquadsForGame(ctx, gameSession.Id);
        foreach (var gp in ctx.Db.game_player.GameSessionId.Filter(gameSession.Id))
        {
          foreach (var pool in ctx.Db.resource_pool.GamePlayerId.Filter(gp.Id))
            ctx.Db.resource_pool.Id.Delete(pool.Id);
          foreach (var cd in ctx.Db.ability_cooldown.GamePlayerId.Filter(gp.Id))
            ctx.Db.ability_cooldown.Id.Delete(cd.Id);
          foreach (var effect in ctx.Db.active_effect.GamePlayerId.Filter(gp.Id))
            ctx.Db.active_effect.Id.Delete(effect.Id);
          ctx.Db.game_player.Id.Delete(gp.Id);
        }
        foreach (var loadout in ctx.Db.loadout.GameSessionId.Filter(gameSession.Id))
          ctx.Db.loadout.Id.Delete(loadout.Id);
        foreach (var log in ctx.Db.battle_log.GameSessionId.Filter(gameSession.Id))
          ctx.Db.battle_log.Id.Delete(log.Id);
        foreach (var feature in ctx.Db.terrain_feature.GameSessionId.Filter(gameSession.Id))
        {
          foreach (var check in ctx.Db.terrain_expiry_check.Iter())
          {
            if (check.TerrainFeatureId == feature.Id)
              ctx.Db.terrain_expiry_check.Id.Delete(check.Id);
          }
          foreach (var regen in ctx.Db.outpost_regen_tick.Iter())
          {
            if (regen.TerrainFeatureId == feature.Id)
              ctx.Db.outpost_regen_tick.Id.Delete(regen.Id);
          }
          ctx.Db.terrain_feature.Id.Delete(feature.Id);
        }
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
        foreach (var c in ctx.Db.corpse.GameSessionId.Filter(gameSession.Id))
          ctx.Db.corpse.Id.Delete(c.Id);
        foreach (var cp in ctx.Db.capture_point.GameSessionId.Filter(gameSession.Id))
          ctx.Db.capture_point.Id.Delete(cp.Id);
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
