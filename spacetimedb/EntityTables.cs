using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Type]
  public enum EntityType : byte
  {
    GamePlayer,
    Soldier,
    Terrain,
    CapturePoint,
    Corpse,
  }

  [SpacetimeDB.Table(Accessor = "entity", Public = true)]
  public partial struct Entity
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong EntityId;

    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;

    public EntityType Type;
    public DbVector3 Position;
    public float RotationY;
    public byte TeamSlot;
  }

  [SpacetimeDB.Table(Accessor = "targetable", Public = true)]
  public partial struct Targetable
  {
    [SpacetimeDB.PrimaryKey]
    public ulong EntityId;

    public int Health;
    public int MaxHealth;
    public int Armor;

    [SpacetimeDB.Default(false)]
    public bool Dead;
    public Timestamp? DiedAt;
  }

  static Entity CreateEntity(ReducerContext ctx, ulong gameSessionId, EntityType type, DbVector3 position, float rotationY, byte teamSlot)
  {
    return ctx.Db.entity.Insert(new Entity
    {
      EntityId = 0,
      GameSessionId = gameSessionId,
      Type = type,
      Position = position,
      RotationY = rotationY,
      TeamSlot = teamSlot,
    });
  }

  static Targetable CreateTargetable(ReducerContext ctx, ulong entityId, int health, int maxHealth, int armor)
  {
    return ctx.Db.targetable.Insert(new Targetable
    {
      EntityId = entityId,
      Health = health,
      MaxHealth = maxHealth,
      Armor = armor,
    });
  }

  static void DestroyEntity(ReducerContext ctx, ulong entityId)
  {
    ctx.Db.targetable.EntityId.Delete(entityId);
    ctx.Db.entity.EntityId.Delete(entityId);
  }

  static DbVector3 GetEntityPosition(ReducerContext ctx, ulong entityId)
  {
    if (ctx.Db.entity.EntityId.Find(entityId) is Entity e)
      return e.Position;
    return new DbVector3(0, 0, 0);
  }
}
