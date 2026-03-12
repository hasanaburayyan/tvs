using SpacetimeDB;

[SpacetimeDB.Type]
public enum SpellId : byte
{
  Fireball,
  IceLance,
  Heal,
  Shield,
  Thunderbolt,
}

public static partial class Module
{
  [SpacetimeDB.Reducer]
  public static void CastSpell(ReducerContext ctx, ulong gameId, SpellId spellId, ulong? targetGamePlayerId)
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

    Log.Info($"Player {player.Name} cast {spellId} (target: {targetGamePlayerId?.ToString() ?? "none"})");
  }
}
