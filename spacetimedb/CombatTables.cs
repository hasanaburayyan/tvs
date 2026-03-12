using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Table(Accessor = "ability_def", Public = true)]
  public partial struct AbilityDef
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public string Name;
    public string Description;
    public AbilityType Type;
    public List<TargetType> ValidTargets;
    public int BasePower;
    public float BaseRange;
    public float BaseRadius;
    public ulong CooldownMs;
    public List<ResourceCost> ResourceCosts;

    public List<AbilityMod> GrantedMods;
    public ulong EffectDurationMs;
    public List<ulong> AffectedAbilityIds;

    public TerrainType? SpawnedTerrainType;
    public float TerrainSizeX;
    public float TerrainSizeY;
    public float TerrainSizeZ;
  }

  [SpacetimeDB.Table(Accessor = "weapon_def", Public = true)]
  public partial struct WeaponDef
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public string Name;
    public string Description;
    public ulong PrimaryAbilityId;
    public ulong SecondaryAbilityId;
    public List<ulong> BonusAbilityIds;
    public List<AbilityMod> PrimaryMods;
    public List<AbilityMod> SecondaryMods;
  }

  [SpacetimeDB.Table(Accessor = "skill_def", Public = true)]
  public partial struct SkillDef
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    public string Name;
    public string Description;
    public List<ulong> AbilityIds;
  }

  [SpacetimeDB.Table(Accessor = "loadout", Public = true)]
  [SpacetimeDB.Index.BTree(Accessor = "by_session_player", Columns = new[] { "GameSessionId", "PlayerId" })]
  public partial struct Loadout
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;
    [SpacetimeDB.Index.BTree]
    public ulong PlayerId;
    public ulong WeaponDefId;
    public ulong SkillDefId;
  }

  [SpacetimeDB.Table(Accessor = "ability_cooldown", Public = true)]
  [SpacetimeDB.Index.BTree(Accessor = "by_gp_ability", Columns = new[] { "GamePlayerId", "AbilityId" })]
  public partial struct AbilityCooldown
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    [SpacetimeDB.Index.BTree]
    public ulong GamePlayerId;
    public ulong AbilityId;
    public Timestamp ReadyAt;
  }

  [SpacetimeDB.Table(Accessor = "active_effect", Public = true)]
  public partial struct ActiveEffect
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    [SpacetimeDB.Index.BTree]
    public ulong GamePlayerId;
    public ulong CasterGamePlayerId;
    public ulong SourceAbilityId;
    public List<AbilityMod> Mods;
    public List<ulong> AffectedAbilityIds;
    public Timestamp ExpiresAt;
  }

  [SpacetimeDB.Table(Accessor = "resource_pool", Public = true)]
  public partial struct ResourcePool
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    [SpacetimeDB.Index.BTree]
    public ulong GamePlayerId;
    public ResourceKind Kind;
    public int Current;
    public int Max;
  }

  [SpacetimeDB.Table(Accessor = "battle_log", Public = true)]
  public partial struct BattleLogEntry
  {
    [SpacetimeDB.PrimaryKey]
    [SpacetimeDB.AutoInc]
    public ulong Id;
    [SpacetimeDB.Index.BTree]
    public ulong GameSessionId;
    public Timestamp OccurredAt;
    [SpacetimeDB.Index.BTree]
    public ulong ActorGamePlayerId;
    public ulong AbilityId;
    public bool FromWeapon;
    public List<ulong> TargetGamePlayerIds;
    public int ResolvedPower;
  }
}
