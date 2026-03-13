using SpacetimeDB;

public static partial class Module {
  const float OutpostRegenRadius = 10f;
  const int OutpostHealthPerTick = 2;
  const int OutpostSuppliesPerTick = 1;
  const ulong OutpostRegenIntervalMs = 3000;

  const int PassiveStaminaPerTick = 1;
  const int PassiveManaPerTick = 1;
  const ulong PassiveRegenIntervalMs = 5000;

  [SpacetimeDB.Table(Accessor = "terrain_feature", Public = true)]
  public partial struct TerrainFeature
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;
    public TerrainType Type;
    public float PosX;
    public float PosY;
    public float PosZ;
    public float SizeX;
    public float SizeY;
    public float SizeZ;
    public float RotationY;
    public byte TeamIndex;
    public ulong? CasterGamePlayerId;
    public Timestamp? ExpiresAt;
    [SpacetimeDB.Default(false)]
    public bool Expired;
    public int Health;
    public int MaxHealth;
  }

  [SpacetimeDB.Table(Accessor = "terrain_expiry_check", Scheduled = nameof(TerrainFeatureExpiry))]
  public partial struct TerrainExpiryCheck
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public ulong TerrainFeatureId;
    public ScheduleAt ScheduledAt;
  }

  [SpacetimeDB.Table(Accessor = "outpost_regen_tick", Scheduled = nameof(OutpostRegenTick))]
  public partial struct OutpostRegenTickSchedule
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public ulong TerrainFeatureId;
    public ScheduleAt ScheduledAt;
  }

  [SpacetimeDB.Reducer]
  public static void TerrainFeatureExpiry(ReducerContext ctx, TerrainExpiryCheck check) {
    if (ctx.Db.terrain_feature.Id.Find(check.TerrainFeatureId) is TerrainFeature terrain)
    {
      terrain.Expired = true;
      ctx.Db.terrain_feature.Id.Update(terrain);
    }
  }

  [SpacetimeDB.Reducer]
  public static void OutpostRegenTick(ReducerContext ctx, OutpostRegenTickSchedule tick)
  {
    if (ctx.Db.terrain_feature.Id.Find(tick.TerrainFeatureId) is not TerrainFeature outpost)
      return;

    if (outpost.Expired || outpost.Health <= 0 || outpost.Type != TerrainType.Outpost)
      return;

    float ox = outpost.PosX;
    float oz = outpost.PosZ;
    float radiusSq = OutpostRegenRadius * OutpostRegenRadius;

    foreach (var gp in ctx.Db.game_player.GameSessionId.Filter(outpost.GameSessionId))
    {
      if (!gp.Active) continue;

      float dx = gp.Position.x - ox;
      float dz = gp.Position.z - oz;
      if (dx * dx + dz * dz > radiusSq) continue;

      if (gp.Health < gp.MaxHealth)
      {
        int newHealth = Math.Min(gp.MaxHealth, gp.Health + OutpostHealthPerTick);
        ctx.Db.game_player.Id.Update(gp with { Health = newHealth });
      }

      if (FindResourcePool(ctx, gp.Id, ResourceKind.Supplies) is ResourcePool pool && pool.Current < pool.Max)
      {
        int newSupplies = Math.Min(pool.Max, pool.Current + OutpostSuppliesPerTick);
        ctx.Db.resource_pool.Id.Update(pool with { Current = newSupplies });
      }
    }

    ctx.Db.outpost_regen_tick.Insert(new OutpostRegenTickSchedule
    {
      Id = 0,
      TerrainFeatureId = tick.TerrainFeatureId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(OutpostRegenIntervalMs)),
    });
  }

  static void ScheduleOutpostRegen(ReducerContext ctx, ulong terrainFeatureId)
  {
    ctx.Db.outpost_regen_tick.Insert(new OutpostRegenTickSchedule
    {
      Id = 0,
      TerrainFeatureId = terrainFeatureId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(OutpostRegenIntervalMs)),
    });
  }

  [SpacetimeDB.Table(Accessor = "passive_regen_tick", Scheduled = nameof(PassiveRegenTick))]
  public partial struct PassiveRegenTickSchedule
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public ulong GameSessionId;
    public ScheduleAt ScheduledAt;
  }

  [SpacetimeDB.Reducer]
  public static void PassiveRegenTick(ReducerContext ctx, PassiveRegenTickSchedule tick)
  {
    if (ctx.Db.game_session.Id.Find(tick.GameSessionId) is not GameSession session)
      return;

    if (session.State == SessionState.Ended)
      return;

    bool hasActivePlayers = false;

    foreach (var gp in ctx.Db.game_player.GameSessionId.Filter(tick.GameSessionId))
    {
      if (!gp.Active) continue;
      hasActivePlayers = true;

      if (FindResourcePool(ctx, gp.Id, ResourceKind.Stamina) is ResourcePool stamina && stamina.Current < stamina.Max)
      {
        int newVal = Math.Min(stamina.Max, stamina.Current + PassiveStaminaPerTick);
        ctx.Db.resource_pool.Id.Update(stamina with { Current = newVal });
      }

      if (FindResourcePool(ctx, gp.Id, ResourceKind.Mana) is ResourcePool mana && mana.Current < mana.Max)
      {
        int newVal = Math.Min(mana.Max, mana.Current + PassiveManaPerTick);
        ctx.Db.resource_pool.Id.Update(mana with { Current = newVal });
      }
    }

    if (!hasActivePlayers)
      return;

    ctx.Db.passive_regen_tick.Insert(new PassiveRegenTickSchedule
    {
      Id = 0,
      GameSessionId = tick.GameSessionId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(PassiveRegenIntervalMs)),
    });
  }

  static void SchedulePassiveRegen(ReducerContext ctx, ulong gameSessionId)
  {
    ctx.Db.passive_regen_tick.Insert(new PassiveRegenTickSchedule
    {
      Id = 0,
      GameSessionId = gameSessionId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(PassiveRegenIntervalMs)),
    });
  }
}
