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

    if (outpost.Expired || outpost.Health <= 0)
      return;
    if (outpost.Type != TerrainType.Outpost && outpost.Type != TerrainType.CommandCenter)
      return;

    float ox = outpost.PosX;
    float oz = outpost.PosZ;
    float radiusSq = OutpostRegenRadius * OutpostRegenRadius;

    foreach (var gp in ctx.Db.game_player.GameSessionId.Filter(outpost.GameSessionId))
    {
      if (!gp.Active || gp.Dead) continue;
      if (outpost.TeamIndex != 0 && gp.TeamSlot != outpost.TeamIndex) continue;

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

    foreach (var gp in ctx.Db.game_player.GameSessionId.Filter(tick.GameSessionId))
    {
      if (!gp.Active) continue;

      foreach (var effect in ctx.Db.active_effect.GamePlayerId.Filter(gp.Id))
      {
        if (effect.ExpiresAt.MicrosecondsSinceUnixEpoch < ctx.Timestamp.MicrosecondsSinceUnixEpoch)
          ctx.Db.active_effect.Id.Delete(effect.Id);
      }

      if (gp.Dead) continue;

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

  // --- Capture Points ---

  const int CaptureInfluencePerPlayer = 1;
  const int CaptureMaxInfluence = 100;
  const float CaptureRadius = 10f;
  const ulong CaptureTickIntervalMs = 1000;

  [SpacetimeDB.Table(Accessor = "capture_point", Public = true)]
  public partial struct CapturePoint
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;

    public float PosX;
    public float PosZ;
    public float Radius;

    public byte OwningTeam;
    public int InfluenceTeam1;
    public int InfluenceTeam2;
    public int MaxInfluence;
  }

  [SpacetimeDB.Table(Accessor = "capture_tick", Scheduled = nameof(CapturePointTick))]
  public partial struct CaptureTickSchedule
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public ulong GameSessionId;
    public ScheduleAt ScheduledAt;
  }

  [SpacetimeDB.Reducer]
  public static void CapturePointTick(ReducerContext ctx, CaptureTickSchedule tick)
  {
    if (ctx.Db.game_session.Id.Find(tick.GameSessionId) is not GameSession session)
      return;

    if (session.State == SessionState.Ended)
      return;

    foreach (var cp in ctx.Db.capture_point.GameSessionId.Filter(tick.GameSessionId))
    {
      float radiusSq = cp.Radius * cp.Radius;
      int team1Count = 0;
      int team2Count = 0;

      foreach (var gp in ctx.Db.game_player.GameSessionId.Filter(tick.GameSessionId))
      {
        if (!gp.Active || gp.Dead || gp.TeamSlot == 0) continue;

        float dx = gp.Position.x - cp.PosX;
        float dz = gp.Position.z - cp.PosZ;
        if (dx * dx + dz * dz > radiusSq) continue;

        if (gp.TeamSlot == 1) team1Count++;
        else if (gp.TeamSlot == 2) team2Count++;
      }

      int net = (team1Count - team2Count) * CaptureInfluencePerPlayer;
      if (net == 0) continue;

      int inf1 = cp.InfluenceTeam1;
      int inf2 = cp.InfluenceTeam2;

      if (net > 0)
      {
        if (inf2 > 0)
        {
          inf2 -= net;
          if (inf2 < 0) { inf1 += -inf2; inf2 = 0; }
        }
        else
        {
          inf1 += net;
        }
      }
      else
      {
        int absNet = -net;
        if (inf1 > 0)
        {
          inf1 -= absNet;
          if (inf1 < 0) { inf2 += -inf1; inf1 = 0; }
        }
        else
        {
          inf2 += absNet;
        }
      }

      inf1 = Math.Clamp(inf1, 0, cp.MaxInfluence);
      inf2 = Math.Clamp(inf2, 0, cp.MaxInfluence);

      byte owner = cp.OwningTeam;
      if (inf1 >= cp.MaxInfluence) owner = 1;
      else if (inf2 >= cp.MaxInfluence) owner = 2;
      else if (inf1 == 0 && inf2 == 0) owner = 0;

      ctx.Db.capture_point.Id.Update(cp with
      {
        InfluenceTeam1 = inf1,
        InfluenceTeam2 = inf2,
        OwningTeam = owner,
      });
    }

    ctx.Db.capture_tick.Insert(new CaptureTickSchedule
    {
      Id = 0,
      GameSessionId = tick.GameSessionId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(CaptureTickIntervalMs)),
    });
  }

  static void ScheduleCaptureTick(ReducerContext ctx, ulong gameSessionId)
  {
    ctx.Db.capture_tick.Insert(new CaptureTickSchedule
    {
      Id = 0,
      GameSessionId = gameSessionId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(CaptureTickIntervalMs)),
    });
  }
}
