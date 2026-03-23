using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Table(Accessor = "base_resource_store", Public = true)]
  public partial struct BaseResourceStore
  {
    [SpacetimeDB.PrimaryKey]
    public ulong EntityId;
    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;
    public byte TeamSlot;
    public int Supplies;
    public int SuppliesMax;
    public float GenerationPerSecond;
    public byte Level;
  }

  [SpacetimeDB.Table(Accessor = "road_segment", Public = true)]
  public partial struct RoadSegment
  {
    [SpacetimeDB.PrimaryKey]
    public ulong EntityId;
    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;
    public byte TeamSlot;
    public ulong ConnectedToEntityId;
    public byte Level;
  }

  [SpacetimeDB.Table(Accessor = "resupply_session", Public = true)]
  public partial struct ResupplySession
  {
    [SpacetimeDB.PrimaryKey]
    public ulong EntityId;
    public ulong BaseEntityId;
  }

  [SpacetimeDB.Table(Accessor = "logistics_tick", Scheduled = nameof(LogisticsTick))]
  public partial struct LogisticsTickSchedule
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public ulong GameSessionId;
    public ScheduleAt ScheduledAt;
  }
}
