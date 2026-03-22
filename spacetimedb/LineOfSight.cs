using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Table(Accessor = "los_check_schedule", Scheduled = nameof(CheckLineOfSight))]
  public partial struct LosCheckSchedule
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public ulong GameSessionId;
    public ScheduleAt ScheduledAt;
  }

  public static bool HasLineOfSight(ReducerContext ctx, DbVector3 posA, DbVector3 posB, ulong gameSessionId)
  {
    float dx = posB.x - posA.x;
    float dy = posB.y - posA.y;
    float dz = posB.z - posA.z;
    float rayLength = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);

    if (rayLength < 0.001f) return true;

    float invLen = 1f / rayLength;
    var rayDir = new DbVector3(dx * invLen, dy * invLen, dz * invLen);

    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(gameSessionId))
    {
      if (ent.Type != EntityType.Terrain) continue;
      if (ctx.Db.terrain_feature.EntityId.Find(ent.EntityId) is not TerrainFeature terrain) continue;

      var boxCenter = new DbVector3(ent.Position.x, ent.Position.y + terrain.SizeY * 0.5f, ent.Position.z);
      var boxHalfExtents = new DbVector3(terrain.SizeX * 0.5f, terrain.SizeY * 0.5f, terrain.SizeZ * 0.5f);

      if (RayIntersectsOBB(posA, rayDir, rayLength, boxCenter, boxHalfExtents, ent.RotationY))
        return false;
    }

    return true;
  }

  public static bool RayIntersectsOBB(
    DbVector3 rayOrigin, DbVector3 rayDir, float rayLength,
    DbVector3 boxCenter, DbVector3 boxHalfExtents, float rotationYDeg)
  {
    float rad = -rotationYDeg * (float)Math.PI / 180f;
    float cos = (float)Math.Cos(rad);
    float sin = (float)Math.Sin(rad);

    float ox = rayOrigin.x - boxCenter.x;
    float oy = rayOrigin.y - boxCenter.y;
    float oz = rayOrigin.z - boxCenter.z;

    float localOx = ox * cos - oz * sin;
    float localOy = oy;
    float localOz = ox * sin + oz * cos;

    float localDx = rayDir.x * cos - rayDir.z * sin;
    float localDy = rayDir.y;
    float localDz = rayDir.x * sin + rayDir.z * cos;

    float tMin = 0f;
    float tMax = rayLength;

    if (Math.Abs(localDx) < 1e-8f)
    {
      if (localOx < -boxHalfExtents.x || localOx > boxHalfExtents.x) return false;
    }
    else
    {
      float invD = 1f / localDx;
      float t1 = (-boxHalfExtents.x - localOx) * invD;
      float t2 = (boxHalfExtents.x - localOx) * invD;
      if (t1 > t2) (t1, t2) = (t2, t1);
      tMin = Math.Max(tMin, t1);
      tMax = Math.Min(tMax, t2);
      if (tMin > tMax) return false;
    }

    if (Math.Abs(localDy) < 1e-8f)
    {
      if (localOy < -boxHalfExtents.y || localOy > boxHalfExtents.y) return false;
    }
    else
    {
      float invD = 1f / localDy;
      float t1 = (-boxHalfExtents.y - localOy) * invD;
      float t2 = (boxHalfExtents.y - localOy) * invD;
      if (t1 > t2) (t1, t2) = (t2, t1);
      tMin = Math.Max(tMin, t1);
      tMax = Math.Min(tMax, t2);
      if (tMin > tMax) return false;
    }

    if (Math.Abs(localDz) < 1e-8f)
    {
      if (localOz < -boxHalfExtents.z || localOz > boxHalfExtents.z) return false;
    }
    else
    {
      float invD = 1f / localDz;
      float t1 = (-boxHalfExtents.z - localOz) * invD;
      float t2 = (boxHalfExtents.z - localOz) * invD;
      if (t1 > t2) (t1, t2) = (t2, t1);
      tMin = Math.Max(tMin, t1);
      tMax = Math.Min(tMax, t2);
      if (tMin > tMax) return false;
    }

    return true;
  }

  [SpacetimeDB.Reducer]
  public static void CheckLineOfSight(ReducerContext ctx, LosCheckSchedule schedule)
  {
    var session = ctx.Db.game_session.Id.Find(schedule.GameSessionId);
    if (session is not GameSession gs)
      return;

    if (gs.State == SessionState.Ended)
      return;

    foreach (var ent in ctx.Db.entity.GameSessionId.Filter(schedule.GameSessionId))
    {
      if (ent.Type != EntityType.GamePlayer) continue;
      if (ctx.Db.game_player.EntityId.Find(ent.EntityId) is not GamePlayer gp) continue;
      if (!gp.Active || gp.TargetEntityId is not ulong targetId) continue;
      if (ctx.Db.targetable.EntityId.Find(ent.EntityId) is Targetable selfT && selfT.Dead) continue;

      var targetEnt = ctx.Db.entity.EntityId.Find(targetId);
      if (targetEnt is not Entity te) continue;

      bool clearTarget = false;
      if (ctx.Db.targetable.EntityId.Find(targetId) is Targetable tt && tt.Dead)
        clearTarget = true;
      else if (!HasLineOfSight(ctx, ent.Position, te.Position, schedule.GameSessionId))
        clearTarget = true;

      if (clearTarget)
        ctx.Db.game_player.EntityId.Update(gp with { TargetEntityId = null });
    }

    ctx.Db.los_check_schedule.Insert(new LosCheckSchedule
    {
      Id = 0,
      GameSessionId = schedule.GameSessionId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(500)),
    });
  }

  static void ScheduleLosCheck(ReducerContext ctx, ulong gameSessionId)
  {
    ctx.Db.los_check_schedule.Insert(new LosCheckSchedule
    {
      Id = 0,
      GameSessionId = gameSessionId,
      ScheduledAt = new ScheduleAt.Time(ctx.Timestamp + TimeSpan.FromMilliseconds(500)),
    });
  }
}
