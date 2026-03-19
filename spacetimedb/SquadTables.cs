using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Table(Accessor = "squad", Public = true)]
  public partial struct Squad
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;

    [SpacetimeDB.Index.BTree]
    public ulong ParentSquadId;

    public ulong? OwnerPlayerId;

    public float CohesionRadius;

    public DbVector3 CenterPosition;

    [SpacetimeDB.Index.BTree]
    public ulong GamePlayerId;

    [SpacetimeDB.Index.BTree]
    public ulong SoldierId;
  }

  [SpacetimeDB.Table(Accessor = "ai_squad_tick", Scheduled = nameof(AiSquadTick))]
  public partial struct AiSquadTickSchedule
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    public ulong GameSessionId;
    public ScheduleAt ScheduledAt;
  }

  [SpacetimeDB.Table(Accessor = "soldier", Public = true)]
  public partial struct Soldier
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;

    public ulong? OwnerPlayerId;

    public int Health;
    public int MaxHealth;
    public int Armor;

    public DbVector3 Position;
    public float RotationY;

    [SpacetimeDB.Default(false)]
    public bool Dead;
    public Timestamp? DiedAt;

    public byte FormationIndex;
  }
}
