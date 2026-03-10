using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Reducer]
  public static void CreatePlayer(ReducerContext ctx, string name)
  {
    var player = ctx.Db.player.Identity.Find(ctx.Sender);
    if (player is not null)
    {
      throw new Exception("Player already exists");
    }

    ctx.Db.player.Insert(new Player
    {
      Identity = ctx.Sender,
      Name = name,
      Online = true,
    });
  }

  [SpacetimeDB.Reducer]
  public static void DeletePlayer(ReducerContext ctx, Identity identity)
  {
    ctx.Db.player.Identity.Delete(identity);
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
  public static void DeleteGame(ReducerContext ctx, ulong gameId)
  {
    ctx.Db.game_session.Id.Delete(gameId);
  }


  const int floor_height = 2;

  [SpacetimeDB.Reducer]
  public static void JoinGame(ReducerContext ctx, ulong gameId)
  {
    var gameSession = ctx.Db.game_session.Id.Find(gameId);
    if (gameSession is null)
    {
      throw new Exception("Game session not found!");
    }

    if (gameSession.Value.State != SessionState.Lobby)
    {
      throw new Exception("Joining a game in progress is not yet supported!");
    }

    if (gameSession.Value.MaxPlayers <= ctx.Db.game_player.GameSessionId.Filter(gameSession.Value.Id).Count())
    {
      throw new Exception("Game session is full!");
    }


    var desired_spawn_location = new DbVector3(0, floor_height + 1, 0);

    var try_spawn_location = bool () =>
    {
      var other_players = ctx.Db.game_player.GameSessionId.Filter(gameId);
      foreach (var other_player in other_players)
      {
        if (other_player.Position == desired_spawn_location)
        {
          return false;
        }
      }
      return true;
    };


    while (!try_spawn_location())
    {
      desired_spawn_location = desired_spawn_location * 2;
    }

    ctx.Db.game_player.Insert(new GamePlayer
    {
      GameSessionId = gameSession.Value.Id,
      PlayerIdentity = ctx.Sender,
      Position = desired_spawn_location
    });
  }

  [SpacetimeDB.Reducer]
  public static void KickPlayerFromGame(ReducerContext ctx, ulong gameId, String playerName)
  {
    var player = ctx.Db.player.Name.Find(playerName) ?? throw new Exception("Cannot find player to kick");
    var gamePlayer = ctx.Db.game_player.PlayerIdentity.Find(player.Identity) ?? throw new Exception("Player is not in any games to kick");

    if (gamePlayer.GameSessionId == gameId)
    {
      ctx.Db.game_player.Id.Delete(gamePlayer.Id);
    }
  }

  [SpacetimeDB.Reducer]
  public static void LeaveGame(ReducerContext ctx, ulong gameId)
  {
    var gp = ctx.Db.game_player.PlayerIdentity.Find(ctx.Sender) ?? throw new Exception("cannot find a game to leave");
    if (gp.GameSessionId == gameId)
    {
      ctx.Db.game_player.Id.Delete(gp.Id);
    }
    CleanupPositionOverrides(ctx, ctx.Sender);
  }

  [SpacetimeDB.Reducer]
  public static void ClearData(ReducerContext ctx)
  {
    var player = ctx.Db.player.Identity.Find(ctx.Sender);
    if (player != null)
    {
      if (ctx.Db.game_player.PlayerIdentity.Find(ctx.Sender) is GamePlayer gamePlayer)
      {
        ctx.Db.game_player.Id.Delete(gamePlayer.Id);

        // delete game sessions owned by player, if any
        // but remove all players in that game session first
        foreach (var gameSession in ctx.Db.game_session.OwnerIdentity.Filter(ctx.Sender))
        {
          foreach (var gp in ctx.Db.game_player.GameSessionId.Filter(gameSession.Id))
          {
            ctx.Db.game_player.Id.Delete(gp.Id);
          }
          ctx.Db.game_session.Id.Delete(gameSession.Id);
        }

      }

      // now delete the player
      ctx.Db.player.Identity.Delete(ctx.Sender);
    }

    CleanupPositionOverrides(ctx, ctx.Sender);

    // Find chat sessions player owns
    var chatSessions = ctx.Db.ChatSession.Owner.Filter(ctx.Sender);

    // Remove all memberships
    foreach (var membership in ctx.Db.ChatSessionPlayer.PlayerIdentity.Filter(ctx.Sender))
    {
      ctx.Db.ChatSessionPlayer.Id.Delete(membership.Id);
    }

    foreach (var message in ctx.Db.Message.Sender.Filter(ctx.Sender))
    {
      ctx.Db.Message.Id.Delete(message.Id);
    }

    foreach (var chatSession in chatSessions)
    {
      ctx.Db.ChatSession.Id.Delete(chatSession.Id);
    }
  }
}
