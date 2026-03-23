using SpacetimeDB;

public static partial class Module {
  const float OutpostRegenRadius = 10f;
  const int OutpostHealthPerTick = 2;
  const ulong OutpostRegenIntervalMs = 3000;

  const int PassiveStaminaPerTick = 1;
  const int PassiveManaPerTick = 1;
  const ulong PassiveRegenIntervalMs = 5000;

  [SpacetimeDB.Table(Accessor = "terrain_feature", Public = true)]
  public partial struct TerrainFeature
  {
    [SpacetimeDB.PrimaryKey]
    public ulong EntityId;
    public TerrainType Type;
    public float SizeX;
    public float SizeY;
    public float SizeZ;
    public ulong? CasterEntityId;
    public Timestamp? ExpiresAt;
    [SpacetimeDB.Default(false)]
    public bool Expired;
  }

  [SpacetimeDB.Table(Accessor = "terrain_expiry_check", Scheduled = nameof(TerrainFeatureExpiry))]
  public partial struct TerrainExpiryCheck
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public ulong EntityId;
    public ScheduleAt ScheduledAt;
  }

  [SpacetimeDB.Table(Accessor = "outpost_regen_tick", Scheduled = nameof(OutpostRegenTick))]
  public partial struct OutpostRegenTickSchedule
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public ulong EntityId;
    public ScheduleAt ScheduledAt;
  }

  [SpacetimeDB.Reducer]
  public static void TerrainFeatureExpiry(ReducerContext ctx, TerrainExpiryCheck check) {
    if (ctx.Db.terrain_feature.EntityId.Find(check.EntityId) is TerrainFeature terrain)
    {
      ctx.Db.terrain_feature.EntityId.Update(terrain with { Expired = true });
    }
  }

  [SpacetimeDB.Reducer]
  public static void OutpostRegenTick(ReducerContext ctx, OutpostRegenTickSchedule tick)
  {
    if (ctx.Db.terrain_feature.EntityId.Find(tick.EntityId) is not TerrainFeature outpost)
      return;

    var outpostEntity = ctx.Db.entity.EntityId.Find(tick.EntityId);
    if (outpostEntity is not Entity oe) return;

    var outpostTarget = ctx.Db.targetable.EntityId.Find(tick.EntityId);
    if (outpostTarget is not Targetable ot) return;

    if (outpost.Expired || ot.Health <= 0)
      return;
    if (outpost.Type != TerrainType.Outpost && outpost.Type != TerrainType.CommandCenter)
      return;

    float ox = oe.Position.x;
    float oz = oe.Position.z;
    float radiusSq = OutpostRegenRadius * OutpostRegenRadius;

    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(oe.GameSessionId))
    {
      if (ent.Type != EntityType.GamePlayer) continue;
      if (ctx.Db.game_player.EntityId.Find(ent.EntityId) is not GamePlayer gp) continue;
      if (!gp.Active) continue;
      if (ctx.Db.targetable.EntityId.Find(ent.EntityId) is not Targetable gpTarget) continue;
      if (gpTarget.Dead) continue;
      if (oe.TeamSlot != 0 && ent.TeamSlot != oe.TeamSlot) continue;

      float dx = ent.Position.x - ox;
      float dz = ent.Position.z - oz;
      if (dx * dx + dz * dz > radiusSq) continue;

      if (gpTarget.Health < gpTarget.MaxHealth)
      {
        int newHealth = Math.Min(gpTarget.MaxHealth, gpTarget.Health + OutpostHealthPerTick);
        ctx.Db.targetable.EntityId.Update(gpTarget with { Health = newHealth });
      }
    }

    ctx.Db.outpost_regen_tick.Insert(new OutpostRegenTickSchedule
    {
      Id = 0,
      EntityId = tick.EntityId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(OutpostRegenIntervalMs)),
    });
  }

  static void ScheduleOutpostRegen(ReducerContext ctx, ulong entityId)
  {
    ctx.Db.outpost_regen_tick.Insert(new OutpostRegenTickSchedule
    {
      Id = 0,
      EntityId = entityId,
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

    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(tick.GameSessionId))
    {
      if (ent.Type != EntityType.GamePlayer) continue;
      if (ctx.Db.game_player.EntityId.Find(ent.EntityId) is not GamePlayer gp) continue;
      if (!gp.Active) continue;

      foreach (var effect in ctx.Db.active_effect.EntityId.Filter(ent.EntityId))
      {
        if (effect.ExpiresAt.MicrosecondsSinceUnixEpoch < ctx.Timestamp.MicrosecondsSinceUnixEpoch)
          ctx.Db.active_effect.Id.Delete(effect.Id);
      }

      if (ctx.Db.targetable.EntityId.Find(ent.EntityId) is Targetable t && t.Dead) continue;

      if (FindResourcePool(ctx, ent.EntityId, ResourceKind.Stamina) is ResourcePool stamina && stamina.Current < stamina.Max)
      {
        float newVal = MathF.Min(stamina.Max, stamina.Current + PassiveStaminaPerTick);
        ctx.Db.resource_pool.Id.Update(stamina with { Current = newVal });
      }

      if (FindResourcePool(ctx, ent.EntityId, ResourceKind.Mana) is ResourcePool mana && mana.Current < mana.Max)
      {
        float newVal = MathF.Min(mana.Max, mana.Current + PassiveManaPerTick);
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
    public ulong EntityId;

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

    foreach (var cp in ctx.Db.capture_point.Iter())
    {
      var cpEnt = ctx.Db.entity.EntityId.Find(cp.EntityId);
      if (cpEnt is not Entity cpEntity) continue;
      if (cpEntity.GameSessionId != tick.GameSessionId) continue;

      float radiusSq = cp.Radius * cp.Radius;
      int team1Count = 0;
      int team2Count = 0;

      foreach (var ent in ctx.Db.entity.GameSessionId.Filter(tick.GameSessionId))
      {
        if (ent.Type != EntityType.GamePlayer) continue;
        if (ent.TeamSlot == 0) continue;
        if (ctx.Db.game_player.EntityId.Find(ent.EntityId) is not GamePlayer gp) continue;
        if (!gp.Active) continue;
        if (ctx.Db.targetable.EntityId.Find(ent.EntityId) is Targetable t && t.Dead) continue;

        float dx = ent.Position.x - cpEntity.Position.x;
        float dz = ent.Position.z - cpEntity.Position.z;
        if (dx * dx + dz * dz > radiusSq) continue;

        if (ent.TeamSlot == 1) team1Count++;
        else if (ent.TeamSlot == 2) team2Count++;
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

      ctx.Db.capture_point.EntityId.Update(cp with
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
