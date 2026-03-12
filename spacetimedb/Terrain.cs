using SpacetimeDB;

public static partial class Module {
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

  [SpacetimeDB.Reducer]
  public static void TerrainFeatureExpiry(ReducerContext ctx, TerrainExpiryCheck check) {
    if (ctx.Db.terrain_feature.Id.Find(check.TerrainFeatureId) is TerrainFeature terrain)
    {
      terrain.Expired = true;
      ctx.Db.terrain_feature.Id.Update(terrain);
    }
  }
}
