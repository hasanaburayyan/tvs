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

  const float BASE_SPEED = 5.0f;
  const float SPRINT_MULTIPLIER = 10.0f;
  const float MAX_MOVE_INTERVAL = 0.5f;

  [SpacetimeDB.Reducer]
  public static void MovePlayer(ReducerContext ctx, ulong gameId, DbVector3 newPosition, float rotationY, bool isSprinting)
  {
    var player = GetPlayerForSender(ctx);
    var gamePlayer = FindActiveGamePlayer(ctx, player.Id) ?? throw new Exception("Game player not found!");

    var ent = ctx.Db.entity.EntityId.Find(gamePlayer.EntityId)!.Value;
    var target = ctx.Db.targetable.EntityId.Find(gamePlayer.EntityId);
    if (target is Targetable t && t.Dead)
      throw new Exception("Cannot move while dead");

    float maxSpeed = isSprinting ? BASE_SPEED * SPRINT_MULTIPLIER : BASE_SPEED;
    float maxDist = maxSpeed * MAX_MOVE_INTERVAL;
    float dx = newPosition.x - ent.Position.x;
    float dz = newPosition.z - ent.Position.z;
    float distSq = dx * dx + dz * dz;
    if (distSq > maxDist * maxDist)
      throw new Exception("Move distance exceeds allowed speed");

    ctx.Db.entity.EntityId.Update(ent with { Position = newPosition, RotationY = rotationY });

    MoveSoldiersWithPlayer(ctx, gamePlayer.EntityId, newPosition, rotationY);

    var playerLeaf = FindLeafSquadForEntity(ctx, gamePlayer.EntityId);
    if (playerLeaf is Squad leaf)
    {
      CheckAndMergeCohesion(ctx, leaf.Id, gameId);
      CheckAndSplitCohesion(ctx, leaf.Id);
    }
  }

  [SpacetimeDB.Reducer]
  public static void TeleportPlayer(ReducerContext ctx, ulong gameSessionId, string playerName, DbVector3 position)
  {
    if (ctx.Db.player.Name.Find(playerName) is not Player player)
    {
      throw new Exception($"Player '{playerName}' not found!");
    }

    var gp = FindGamePlayer(ctx, player.Id, gameSessionId) ?? throw new Exception($"Player '{playerName}' is not in game session {gameSessionId}!");

    var ent = ctx.Db.entity.EntityId.Find(gp.EntityId)!.Value;
    ctx.Db.entity.EntityId.Update(ent with { Position = position });

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
