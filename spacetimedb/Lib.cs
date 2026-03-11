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

  static void DeactivateGamePlayer(ReducerContext ctx, ulong playerId)
  {
    if (FindActiveGamePlayer(ctx, playerId) is GamePlayer gp)
      ctx.Db.game_player.Id.Update(gp with { Active = false });
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
    ctx.Db.game_session.Insert(new GameSession
    {
      OwnerIdentity = ctx.Sender,
      State = SessionState.Lobby,
      MaxPlayers = maxPlayers,
      CreatedAt = ctx.Timestamp,
    });
  }

  [SpacetimeDB.Reducer]
  public static void CreateGameAndJoin(ReducerContext ctx, uint maxPlayers) {
    var session = ctx.Db.game_session.Insert(new GameSession
    {
      OwnerIdentity = ctx.Sender,
      State = SessionState.Lobby,
      MaxPlayers = maxPlayers,
      CreatedAt = ctx.Timestamp,
    });

    JoinGame(ctx, session.Id);
  }

  [SpacetimeDB.Reducer]
  public static void DeleteGame(ReducerContext ctx, ulong gameId)
  {
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

    ctx.Db.game_player.Insert(new GamePlayer
    {
      GameSessionId = gameSession.Id,
      PlayerId = player.Id,
      Active = true,
      Position = desired_spawn_location
    });
  }

  [SpacetimeDB.Reducer]
  public static void KickPlayerFromGame(ReducerContext ctx, ulong gameId, string playerName)
  {
    var player = ctx.Db.player.Name.Find(playerName) ?? throw new Exception("Cannot find player to kick");
    var gamePlayer = FindGamePlayer(ctx, player.Id, gameId) ?? throw new Exception("Player is not in this game");

    if (gamePlayer.Active)
    {
      ctx.Db.game_player.Id.Update(gamePlayer with { Active = false });
    }
  }

  [SpacetimeDB.Reducer]
  public static void LeaveGame(ReducerContext ctx, ulong gameId)
  {
    var player = GetPlayerForSender(ctx);
    var gp = FindGamePlayer(ctx, player.Id, gameId) ?? throw new Exception("cannot find a game to leave");

    if (gp.Active)
    {
      ctx.Db.game_player.Id.Update(gp with { Active = false });
    }
    CleanupPositionOverrides(ctx, player.Id);
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
        ctx.Db.game_player.Id.Delete(gp.Id);
      }

      foreach (var gameSession in ctx.Db.game_session.OwnerIdentity.Filter(ctx.Sender))
      {
        foreach (var gp in ctx.Db.game_player.GameSessionId.Filter(gameSession.Id))
        {
          ctx.Db.game_player.Id.Delete(gp.Id);
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
