using SpacetimeDB;

public static partial class Module
{
  [SpacetimeDB.Reducer(ReducerKind.Init)]
  public static void Init(ReducerContext ctx)
  {
    SeedCombatData(ctx);
  }

  static void SeedCombatData(ReducerContext ctx)
  {
    // ================================================================
    // WEAPON FIRE ABILITIES
    // ================================================================

    var firePistol = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Fire Pistol",
      Description = "Fire a quick sidearm shot",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 12,
      BaseRange = 20f,
      BaseRadius = 0f,
      CooldownMs = 400,
      ResourceCosts = new List<ResourceCost>(),
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var fireSmg = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Fire SMG",
      Description = "Spray automatic fire at the target",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 15,
      BaseRange = 18f,
      BaseRadius = 0f,
      CooldownMs = 250,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Supplies, Amount = 1 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var fireRifle = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Fire Rifle",
      Description = "Fire a precise rifle round at the target",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 30,
      BaseRange = 45f,
      BaseRadius = 0f,
      CooldownMs = 1200,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Supplies, Amount = 1 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
      AllowSubSquadTargeting = true,
    });

    var fireTrenchGun = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Fire Trench Gun",
      Description = "Blast a devastating close-range shot",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 40,
      BaseRange = 8f,
      BaseRadius = 0f,
      CooldownMs = 1500,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Supplies, Amount = 2 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
      Distribution = DamageDistribution.ProximityFalloff,
    });

    // ================================================================
    // WEAPONS
    // ================================================================

    ctx.Db.weapon_def.Insert(new WeaponDef
    {
      Id = 0, Name = "Pistol",
      Description = "A reliable sidearm with infinite ammo",
      PrimaryAbilityId = firePistol.Id,
      GrantsSupplies = false,
    });

    ctx.Db.weapon_def.Insert(new WeaponDef
    {
      Id = 0, Name = "SMG",
      Description = "Rapid-fire submachine gun for close engagements",
      PrimaryAbilityId = fireSmg.Id,
      GrantsSupplies = true,
    });

    ctx.Db.weapon_def.Insert(new WeaponDef
    {
      Id = 0, Name = "Rifle",
      Description = "Standard-issue bolt-action rifle with long range",
      PrimaryAbilityId = fireRifle.Id,
      GrantsSupplies = true,
    });

    ctx.Db.weapon_def.Insert(new WeaponDef
    {
      Id = 0, Name = "Trench Gun",
      Description = "Devastating close-range shotgun built for trench warfare",
      PrimaryAbilityId = fireTrenchGun.Id,
      GrantsSupplies = true,
    });

    // ================================================================
    // ARCHETYPE INNATE ABILITIES
    // ================================================================

    // -- Officer innates --
    var rally = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Rally",
      Description = "Inspire a nearby ally, boosting their damage output",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.Ally, TargetType.SelfOnly },
      BasePower = 0, BaseRange = 20f, BaseRadius = 0f,
      CooldownMs = 15000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.DamagePercent, Value = 0.20f },
      },
      EffectDurationMs = 10000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var issueOrders = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Issue Orders",
      Description = "Placeholder: command AI soldiers (not yet implemented)",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 5000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Command, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Infantry innates --
    var fortify = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Fortify",
      Description = "Brace yourself, temporarily increasing armor",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 20000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 15 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.ArmorFlat, Value = 10 },
      },
      EffectDurationMs = 8000,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Support innates --
    var deployOutpost = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Deploy Outpost",
      Description = "Construct a forward outpost",
      Type = AbilityType.Terrain,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0, BaseRange = 15f, BaseRadius = 0f,
      CooldownMs = 30000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Supplies, Amount = 5 },
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 20 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
      SpawnedTerrainType = TerrainType.Outpost,
      TerrainSizeX = 4f, TerrainSizeY = 3f, TerrainSizeZ = 4f,
      TerrainMaxHealth = 200,
    });

    var resupply = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Resupply",
      Description = "Restore an ally's supplies",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.Ally },
      BasePower = 15,
      BaseRange = 10f, BaseRadius = 0f,
      CooldownMs = 12000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Scout innates --
    var reconnaissance = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Reconnaissance",
      Description = "Reveal nearby enemies for a short duration",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 30f, BaseRadius = 20f,
      CooldownMs = 25000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 15 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 8000,
      AffectedAbilityIds = new List<ulong>(),
    });

    // ================================================================
    // ARCHETYPES
    // ================================================================

    var officerArchetype = ctx.Db.archetype_def.Insert(new ArchetypeDef
    {
      Id = 0,
      Name = "Officer",
      Description = "Command and control specialist with buffs, debuffs, and leadership abilities",
      Kind = ArchetypeKind.Officer,
      BonusHealth = 0,
      BonusArmor = 0,
      InnateAbilityIds = new List<ulong> { rally.Id, issueOrders.Id },
    });

    var infantryArchetype = ctx.Db.archetype_def.Insert(new ArchetypeDef
    {
      Id = 0,
      Name = "Infantry",
      Description = "Frontline soldier with the highest individual combat power",
      Kind = ArchetypeKind.Infantry,
      BonusHealth = 30,
      BonusArmor = 5,
      InnateAbilityIds = new List<ulong> { fortify.Id },
    });

    var supportArchetype = ctx.Db.archetype_def.Insert(new ArchetypeDef
    {
      Id = 0,
      Name = "Support",
      Description = "Logistics and field operations: outposts, fortifications, healing, and resupply",
      Kind = ArchetypeKind.Support,
      BonusHealth = 0,
      BonusArmor = 0,
      InnateAbilityIds = new List<ulong> { deployOutpost.Id, resupply.Id },
    });

    var scoutArchetype = ctx.Db.archetype_def.Insert(new ArchetypeDef
    {
      Id = 0,
      Name = "Scout",
      Description = "Recon specialist: fast, fragile, and excels at information warfare",
      Kind = ArchetypeKind.Scout,
      BonusHealth = -10,
      BonusArmor = -2,
      InnateAbilityIds = new List<ulong> { reconnaissance.Id },
    });

    // ================================================================
    // SKILLSET ABILITIES
    // ================================================================

    // -- Officer / Commissar --
    var inspire = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Inspire",
      Description = "Boost an ally's damage output with a rousing speech",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.Ally, TargetType.SelfOnly },
      BasePower = 0, BaseRange = 20f, BaseRadius = 0f,
      CooldownMs = 18000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.DamagePercent, Value = 0.25f },
      },
      EffectDurationMs = 12000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var weaponFocus = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Weapon Focus",
      Description = "Concentrate on your weapon, increasing personal damage",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 20000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 15 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.DamageFlat, Value = 10 },
      },
      EffectDurationMs = 10000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var suppress = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Suppress",
      Description = "Reduce an enemy's damage output",
      Type = AbilityType.Debuff,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 0, BaseRange = 25f, BaseRadius = 0f,
      CooldownMs = 18000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.DamagePercent, Value = -0.25f },
      },
      EffectDurationMs = 10000,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Officer / Tactical Mage --
    var massFortify = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Mass Fortify",
      Description = "Magically harden the armor of all allies in a wide area",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0, BaseRange = 30f, BaseRadius = 15f,
      CooldownMs = 25000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 35 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.ArmorFlat, Value = 8 },
      },
      EffectDurationMs = 12000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var massHaste = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Mass Haste",
      Description = "Magically speed up all allies in a wide area",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0, BaseRange = 30f, BaseRadius = 15f,
      CooldownMs = 25000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 30 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.SpeedPercent, Value = 0.30f },
      },
      EffectDurationMs = 10000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var arcaneShield = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Arcane Shield",
      Description = "Wrap an ally in a protective arcane barrier",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.Ally, TargetType.SelfOnly },
      BasePower = 0, BaseRange = 25f, BaseRadius = 0f,
      CooldownMs = 20000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 25 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.ArmorFlat, Value = 15 },
      },
      EffectDurationMs = 8000,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Officer / Necromancer --
    var resurrect = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Resurrect",
      Description = "Bring a fallen ally back from the dead",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.Ally },
      BasePower = 50,
      BaseRange = 10f, BaseRadius = 0f,
      CooldownMs = 45000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 50 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var raiseDead = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Raise Dead",
      Description = "Placeholder: summon a zombie from a nearby corpse",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0, BaseRange = 15f, BaseRadius = 0f,
      CooldownMs = 30000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 40 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var deathGrip = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Death Grip",
      Description = "Channel necrotic energy to damage and weaken an enemy",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 25,
      BaseRange = 20f, BaseRadius = 0f,
      CooldownMs = 10000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 25 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Infantry / Vanguard --
    var reinforceArmor = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Reinforce Armor",
      Description = "Use supplies to temporarily bolster your armor",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 15000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Supplies, Amount = 3 },
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.ArmorFlat, Value = 12 },
      },
      EffectDurationMs = 10000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var ironWill = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Iron Will",
      Description = "Shake off all debuffs through sheer willpower",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 25000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 20 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var brace = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Brace",
      Description = "Reduce incoming damage by hardening your stance",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 18000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 15 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.DamagePercent, Value = -0.30f },
      },
      EffectDurationMs = 6000,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Infantry / Evoker --
    var fireball = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Fireball",
      Description = "Hurl a ball of fire that explodes on impact",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy, TargetType.Ground },
      BasePower = 50,
      BaseRange = 30f, BaseRadius = 6f,
      CooldownMs = 4000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 25 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
      Distribution = DamageDistribution.EvenSplit,
    });

    var enchantWeapon = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Enchant Weapon",
      Description = "Imbue your weapon attacks with magical energy",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 20000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 30 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.DamageFlat, Value = 15 },
      },
      EffectDurationMs = 12000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var arcaneBarrage = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Arcane Barrage",
      Description = "Bombard an area with arcane energy",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 60,
      BaseRange = 35f, BaseRadius = 10f,
      CooldownMs = 8000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 30 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
      Distribution = DamageDistribution.EvenSplit,
    });

    // -- Infantry / Commando --
    var knifeStrike = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Knife Strike",
      Description = "A quick, lethal melee strike",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 35,
      BaseRange = 3f, BaseRadius = 0f,
      CooldownMs = 1200,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var sprint = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Sprint",
      Description = "Burst of speed for rapid repositioning",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 15000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 20 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.SpeedPercent, Value = 0.50f },
      },
      EffectDurationMs = 5000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var stealth = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Stealth",
      Description = "Placeholder: become hidden from enemies",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 30000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 25 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 10000,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Support / Technician --
    var buildOutpost = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Build Outpost",
      Description = "Construct a reinforced outpost structure",
      Type = AbilityType.Terrain,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0, BaseRange = 12f, BaseRadius = 0f,
      CooldownMs = 25000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Supplies, Amount = 5 },
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 15 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
      SpawnedTerrainType = TerrainType.Outpost,
      TerrainSizeX = 4f, TerrainSizeY = 3f, TerrainSizeZ = 4f,
      TerrainMaxHealth = 200,
    });

    var plantTrap = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Plant Trap",
      Description = "Place a hidden explosive trap",
      Type = AbilityType.Terrain,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 55,
      BaseRange = 8f, BaseRadius = 4f,
      CooldownMs = 15000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Supplies, Amount = 3 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
      SpawnedTerrainType = TerrainType.Trap,
      TerrainSizeX = 1f, TerrainSizeY = 0.2f, TerrainSizeZ = 1f,
      TerrainMaxHealth = 30,
    });

    var buildFortification = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Build Fortification",
      Description = "Construct a defensive wall for cover",
      Type = AbilityType.Terrain,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0, BaseRange = 12f, BaseRadius = 0f,
      CooldownMs = 20000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Supplies, Amount = 4 },
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
      SpawnedTerrainType = TerrainType.Fortification,
      TerrainSizeX = 8f, TerrainSizeY = 2.5f, TerrainSizeZ = 1f,
      TerrainMaxHealth = 150,
    });

    var disarm = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Disarm",
      Description = "Safely disarm an enemy trap or remove a terrain object",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0, BaseRange = 8f, BaseRadius = 0f,
      CooldownMs = 10000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Support / Priest --
    var healingMist = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Healing Mist",
      Description = "Release a restorative mist that heals allies in an area",
      Type = AbilityType.Heal,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly, TargetType.Ally, TargetType.Ground },
      BasePower = 40,
      BaseRange = 25f, BaseRadius = 8f,
      CooldownMs = 8000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 30 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var priestResurrect = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Holy Resurrect",
      Description = "Call upon divine power to restore a fallen ally",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.Ally },
      BasePower = 60,
      BaseRange = 10f, BaseRadius = 0f,
      CooldownMs = 50000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 60 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var divineShield = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Divine Shield",
      Description = "Wrap an ally in holy light, greatly reducing damage taken",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.Ally, TargetType.SelfOnly },
      BasePower = 0, BaseRange = 20f, BaseRadius = 0f,
      CooldownMs = 25000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 35 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.ArmorFlat, Value = 20 },
      },
      EffectDurationMs = 8000,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Support / Fire Support --
    var deployMg = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Deploy MG",
      Description = "Set up a mounted machine gun emplacement",
      Type = AbilityType.Terrain,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0, BaseRange = 10f, BaseRadius = 0f,
      CooldownMs = 30000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Supplies, Amount = 8 },
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 20 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
      SpawnedTerrainType = TerrainType.MountedWeapon,
      TerrainSizeX = 2f, TerrainSizeY = 1.5f, TerrainSizeZ = 2f,
      TerrainMaxHealth = 100,
    });

    var deployMortar = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Deploy Mortar",
      Description = "Set up a mortar emplacement for indirect fire",
      Type = AbilityType.Terrain,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0, BaseRange = 10f, BaseRadius = 0f,
      CooldownMs = 35000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Supplies, Amount = 10 },
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 20 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
      SpawnedTerrainType = TerrainType.MountedWeapon,
      TerrainSizeX = 2f, TerrainSizeY = 1f, TerrainSizeZ = 2f,
      TerrainMaxHealth = 80,
    });

    var deconstruct = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Deconstruct",
      Description = "Dismantle one of your own constructions",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0, BaseRange = 10f, BaseRadius = 0f,
      CooldownMs = 8000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Scout / Pioneer --
    var detectTraps = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Detect Traps",
      Description = "Magically reveal hidden traps in the area",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 20f, BaseRadius = 15f,
      CooldownMs = 15000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 15 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 10000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var revealStealth = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Reveal Stealth",
      Description = "Uncover hidden enemies in a wide area",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 25f, BaseRadius = 20f,
      CooldownMs = 20000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 20 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 8000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var pathfinderAura = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Pathfinder Aura",
      Description = "Grant nearby allies immunity to traps for a short time",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0, BaseRange = 15f, BaseRadius = 10f,
      CooldownMs = 25000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 25 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 12000,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Scout / Specialist --
    var precisionShot = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Precision Shot",
      Description = "Your next weapon attack deals bonus damage",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 12000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 15 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.DamageFlat, Value = 20 },
      },
      EffectDurationMs = 8000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var steadyAim = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Steady Aim",
      Description = "Increase your weapon range temporarily",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 15000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.RangePercent, Value = 0.40f },
      },
      EffectDurationMs = 10000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var quickReload = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Quick Reload",
      Description = "Temporarily reduce weapon cooldown",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 0f, BaseRadius = 0f,
      CooldownMs = 18000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 10 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.CooldownPercent, Value = -0.40f },
      },
      EffectDurationMs = 8000,
      AffectedAbilityIds = new List<ulong>(),
    });

    // -- Scout / Seer --
    var markTarget = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Mark Target",
      Description = "Curse an enemy, increasing all damage they take",
      Type = AbilityType.Debuff,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 0, BaseRange = 30f, BaseRadius = 0f,
      CooldownMs = 18000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 20 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.ArmorFlat, Value = -10 },
      },
      EffectDurationMs = 12000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var farsight = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Farsight",
      Description = "Placeholder: magically see enemies through walls and fortifications",
      Type = AbilityType.Utility,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly },
      BasePower = 0, BaseRange = 40f, BaseRadius = 25f,
      CooldownMs = 25000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 25 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 10000,
      AffectedAbilityIds = new List<ulong>(),
    });

    var hex = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Hex",
      Description = "Curse an enemy, reducing their armor",
      Type = AbilityType.Debuff,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 0, BaseRange = 25f, BaseRadius = 0f,
      CooldownMs = 15000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 20 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.ArmorFlat, Value = -8 },
      },
      EffectDurationMs = 10000,
      AffectedAbilityIds = new List<ulong>(),
    });

    // ================================================================
    // SKILLSETS
    // ================================================================

    // Officer
    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Commissar",
      Description = "Non-magical officer with personal weapon buffs and ally inspiration",
      AbilityIds = new List<ulong> { inspire.Id, weaponFocus.Id, suppress.Id },
      ArchetypeDefId = officerArchetype.Id,
      GrantsMana = false,
    });

    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Tactical Mage",
      Description = "Magical officer with wide-area squad buffs",
      AbilityIds = new List<ulong> { massFortify.Id, massHaste.Id, arcaneShield.Id },
      ArchetypeDefId = officerArchetype.Id,
      GrantsMana = true,
    });

    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Necromancer",
      Description = "Dark magic officer who resurrects the dead and commands corpses",
      AbilityIds = new List<ulong> { resurrect.Id, raiseDead.Id, deathGrip.Id },
      ArchetypeDefId = officerArchetype.Id,
      GrantsMana = true,
    });

    // Infantry
    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Vanguard",
      Description = "Defensive specialist with armor buffs and debuff cleansing",
      AbilityIds = new List<ulong> { reinforceArmor.Id, ironWill.Id, brace.Id },
      ArchetypeDefId = infantryArchetype.Id,
      GrantsMana = false,
    });

    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Evoker",
      Description = "Offensive magic infantry with devastating AoE attacks",
      AbilityIds = new List<ulong> { fireball.Id, enchantWeapon.Id, arcaneBarrage.Id },
      ArchetypeDefId = infantryArchetype.Id,
      GrantsMana = true,
    });

    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Commando",
      Description = "Stealth assault unit with melee attacks and speed",
      AbilityIds = new List<ulong> { knifeStrike.Id, sprint.Id, stealth.Id },
      ArchetypeDefId = infantryArchetype.Id,
      GrantsMana = false,
    });

    // Support
    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Technician",
      Description = "Field engineer: outposts, traps, fortifications, and disarming",
      AbilityIds = new List<ulong> { buildOutpost.Id, plantTrap.Id, buildFortification.Id, disarm.Id },
      ArchetypeDefId = supportArchetype.Id,
      GrantsMana = false,
    });

    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Priest",
      Description = "Divine healer with resurrection and protective blessings",
      AbilityIds = new List<ulong> { healingMist.Id, priestResurrect.Id, divineShield.Id },
      ArchetypeDefId = supportArchetype.Id,
      GrantsMana = true,
    });

    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Fire Support",
      Description = "Heavy weapons specialist with stationary emplacements and extra supplies",
      AbilityIds = new List<ulong> { deployMg.Id, deployMortar.Id, deconstruct.Id },
      ArchetypeDefId = supportArchetype.Id,
      GrantsMana = false,
    });

    // Scout
    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Pioneer",
      Description = "Trap-immune scout with magical scouting and stealth detection",
      AbilityIds = new List<ulong> { detectTraps.Id, revealStealth.Id, pathfinderAura.Id },
      ArchetypeDefId = scoutArchetype.Id,
      GrantsMana = true,
    });

    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Specialist",
      Description = "Glass cannon focused on maximizing weapon damage",
      AbilityIds = new List<ulong> { precisionShot.Id, steadyAim.Id, quickReload.Id },
      ArchetypeDefId = scoutArchetype.Id,
      GrantsMana = false,
    });

    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0, Name = "Seer",
      Description = "Magical debuffer who marks enemies and sees through walls",
      AbilityIds = new List<ulong> { markTarget.Id, farsight.Id, hex.Id },
      ArchetypeDefId = scoutArchetype.Id,
      GrantsMana = true,
    });

    Log.Info("Combat data seeded: 4 weapons, 4 archetypes, 12 skillsets");
  }
}
