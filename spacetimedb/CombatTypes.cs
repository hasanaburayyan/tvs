using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Type]
  public enum AbilityType : byte
  {
    Damage,
    Heal,
    Buff,
    Debuff,
    Terrain,
    Utility,
  }

  [SpacetimeDB.Type]
  public enum TargetType : byte
  {
    SelfOnly,
    Ally,
    Enemy,
    Ground,
  }

  [SpacetimeDB.Type]
  public enum ModType : byte
  {
    DamageFlat,
    DamagePercent,
    RangeFlat,
    RangePercent,
    CooldownPercent,
    RadiusPercent,
    ArmorFlat,
    SpeedPercent,
  }

  [SpacetimeDB.Type]
  public partial struct AbilityMod
  {
    public ModType Type;
    public float Value;
  }

  [SpacetimeDB.Type]
  public enum ResourceKind : byte
  {
    Health,
    Stamina,
    Supplies,
    Mana,
    Command,
  }

  [SpacetimeDB.Type]
  public partial struct ResourceCost
  {
    public ResourceKind Kind;
    public int Amount;
  }

  [SpacetimeDB.Type]
  public enum ArchetypeKind : byte
  {
    Officer,
    Infantry,
    Support,
    Scout,
  }
}
