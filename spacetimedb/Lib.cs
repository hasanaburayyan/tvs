using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Reducer]
  public static void CreatePlayer(ReducerContext ctx, string name) {
    var player = ctx.Db.player.Identity.Find(ctx.Sender);
    if (player is not null) {
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
  public static void DeletePlayer(ReducerContext ctx, Identity identity) {
    ctx.Db.player.Identity.Delete(identity);
  }

[SpacetimeDB.Reducer]
public static void CreateGame(ReducerContext ctx, uint maxPlayers) {
  ctx.Db.game_session.Insert(new GameSession {
    State = SessionState.Lobby,
    MaxPlayers = maxPlayers,
    CreatedAt = ctx.Timestamp,
  });
}

[SpacetimeDB.Reducer]
public static void DeleteGame(ReducerContext ctx, ulong gameId) {
  ctx.Db.game_session.Id.Delete(gameId);
}

  [SpacetimeDB.Reducer]
  public static void JoinGame(ReducerContext ctx, ulong gameId) {
    var gameSession = ctx.Db.game_session.Id.Find(gameId);
    if (gameSession is null) {
      throw new Exception("Game session not found!");
    }

    if (gameSession.Value.State != SessionState.Lobby) {
      throw new Exception("Joining a game in progress is not yet supported!");
    }

    if (gameSession.Value.MaxPlayers <= ctx.Db.game_player.GameSessionId.Filter(gameSession.Value.Id).Count()) {
      throw new Exception("Game session is full!");
    }

    ctx.Db.game_player.Insert(new GamePlayer
    {
      GameSessionId = gameSession.Value.Id,
      PlayerIdentity = ctx.Sender,
    });
  }

  [SpacetimeDB.Reducer]
  public static void LeaveGame(ReducerContext ctx, ulong gameId) {
    ctx.Db.game_player.GameSessionId.Delete(gameId);
  }
}
