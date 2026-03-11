using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Table(Accessor = "PositionOverride", Public = true)]
  public partial struct PositionOverride
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public ulong PlayerId;

    public ulong GameSessionId;
    public DbVector3 Position;
  }

  [SpacetimeDB.Reducer]
  public static void MovePlayer(ReducerContext ctx, ulong gameId, DbVector3 newPosition)
  {
    var player = GetPlayerForSender(ctx);
    var gamePlayer = ctx.Db.game_player.PlayerId.Find(player.Id) ?? throw new Exception("Game player not found!");

    ctx.Db.game_player.Id.Update(gamePlayer with { Position = newPosition });
  }

  [SpacetimeDB.Reducer]
  public static void TeleportPlayer(ReducerContext ctx, ulong gameSessionId, string playerName, DbVector3 position)
  {
    if (ctx.Db.player.Name.Find(playerName) is not Player player)
    {
      throw new Exception($"Player '{playerName}' not found!");
    }

    var gp = ctx.Db.game_player.PlayerId.Find(player.Id) ?? throw new Exception("Game player not found!");

    if (gp.GameSessionId != gameSessionId)
    {
      throw new Exception($"Player '{playerName}' is not in game session {gameSessionId}!");
    }

    ctx.Db.game_player.Id.Update(gp with { Position = position });

    ctx.Db.PositionOverride.Insert(new PositionOverride
    {
      Id = 0,
      PlayerId = player.Id,
      GameSessionId = gameSessionId,
      Position = position,
    });
  }

  [SpacetimeDB.Reducer]
  public static void AckPositionOverride(ReducerContext ctx, ulong overrideId)
  {
    if (ctx.Db.PositionOverride.Id.Find(overrideId) is not PositionOverride po)
    {
      return;
    }

    var activePlayer = GetPlayerForSender(ctx);
    if (po.PlayerId != activePlayer.Id)
    {
      throw new Exception("Not authorized to acknowledge this override!");
    }

    ctx.Db.PositionOverride.Id.Delete(overrideId);
  }

  public static void CleanupPositionOverrides(ReducerContext ctx, ulong playerId)
  {
    foreach (var po in ctx.Db.PositionOverride.PlayerId.Filter(playerId))
    {
      ctx.Db.PositionOverride.Id.Delete(po.Id);
    }
  }
}
