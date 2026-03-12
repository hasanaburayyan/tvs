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
  public static void CreateGame(ReducerContext ctx, uint maxPlayers)
  {
    var seed = (uint)(ctx.Timestamp.MicrosecondsSinceUnixEpoch & 0xFFFFFFFF);
    var session = ctx.Db.game_session.Insert(new GameSession
    {
      OwnerIdentity = ctx.Sender,
      State = SessionState.Lobby,
      MaxPlayers = maxPlayers,
      CreatedAt = ctx.Timestamp,
      MapSeed = seed,
    });
    GenerateMap(ctx, session.Id, seed);
  }

  [SpacetimeDB.Reducer]
  public static void CreateGameAndJoin(ReducerContext ctx, uint maxPlayers)
  {
    var seed = (uint)(ctx.Timestamp.MicrosecondsSinceUnixEpoch & 0xFFFFFFFF);
    var session = ctx.Db.game_session.Insert(new GameSession
    {
      OwnerIdentity = ctx.Sender,
      State = SessionState.Lobby,
      MaxPlayers = maxPlayers,
      CreatedAt = ctx.Timestamp,
      MapSeed = seed,
    });
    GenerateMap(ctx, session.Id, seed);
    JoinGame(ctx, session.Id);
  }

  [SpacetimeDB.Reducer]
  public static void DeleteGame(ReducerContext ctx, ulong gameId)
  {
    foreach (var feature in ctx.Db.terrain_feature.GameSessionId.Filter(gameId))
    {
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

    var desired_spawn_location = new DbVector3(0, floor_height + 1, 0);

    var try_spawn_location = bool () =>
    {
      foreach (var other_player in ctx.Db.game_player.GameSessionId.Filter(gameId))
      {
        if (other_player.Active && other_player.Position == desired_spawn_location)
          return false;
      }
      return true;
    };

    while (!try_spawn_location())
    {
      desired_spawn_location = desired_spawn_location * 2;
    }

    var newGp = ctx.Db.game_player.Insert(new GamePlayer
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

    SeedResourcePools(ctx, newGp.Id);
  }

  static void SeedResourcePools(ReducerContext ctx, ulong gamePlayerId)
  {
    ctx.Db.resource_pool.Insert(new ResourcePool { Id = 0, GamePlayerId = gamePlayerId, Kind = ResourceKind.Mana, Current = 100, Max = 100 });
    ctx.Db.resource_pool.Insert(new ResourcePool { Id = 0, GamePlayerId = gamePlayerId, Kind = ResourceKind.Stamina, Current = 100, Max = 100 });
    ctx.Db.resource_pool.Insert(new ResourcePool { Id = 0, GamePlayerId = gamePlayerId, Kind = ResourceKind.Ammo, Current = 30, Max = 30 });
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
    CleanupPositionOverrides(ctx, player.Id);
  }

  [SpacetimeDB.Reducer]
  public static void SetTarget(ReducerContext ctx, ulong gameId, ulong? targetGamePlayerId)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("No active game player");

    if (gp.GameSessionId != gameId)
      throw new Exception("Game session mismatch");

    if (targetGamePlayerId is ulong targetId)
    {
      var target = ctx.Db.game_player.Id.Find(targetId) ?? throw new Exception("Target not found");
      if (target.GameSessionId != gameId)
        throw new Exception("Target is not in the same game");
      if (!target.Active)
        throw new Exception("Target is not active");
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

      foreach (var gameSession in ctx.Db.game_session.OwnerIdentity.Filter(ctx.Sender))
      {
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
          ctx.Db.terrain_feature.Id.Delete(feature.Id);
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
