using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Table(Accessor = "map_def", Public = true)]
  public partial struct MapDef
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Unique]
    public string Name;
    public float SizeX;
    public float SizeZ;
  }

  [SpacetimeDB.Table(Accessor = "map_terrain_def", Public = true)]
  public partial struct MapTerrainDef
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public ulong MapDefId;

    public TerrainType TerrainType;
    public float PositionX;
    public float PositionY;
    public float PositionZ;
    public float RotationY;
    public byte TeamSlot;
    public float SizeX;
    public float SizeY;
    public float SizeZ;
    public int MaxHealth;
    public int Armor;
    public bool HasOutpostRegen;
  }

  [SpacetimeDB.Table(Accessor = "map_capture_point_def", Public = true)]
  public partial struct MapCapturePointDef
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;

    [SpacetimeDB.Index.BTree]
    public ulong MapDefId;

    public float PositionX;
    public float PositionY;
    public float PositionZ;
    public float Radius;
    public int MaxInfluence;
  }
}
