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
    // --- Base abilities ---

    var shootBullet = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Shoot Bullet",
      Description = "Fire a standard round at the target",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 20,
      BaseRange = 40f,
      BaseRadius = 0f,
      CooldownMs = 500,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Ammo, Amount = 1 },
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 5 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var bayonetStab = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Bayonet Stab",
      Description = "Melee thrust with an attached bayonet",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 35,
      BaseRange = 3f,
      BaseRadius = 0f,
      CooldownMs = 1200,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 15 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var pistolShot = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Pistol Shot",
      Description = "Quick sidearm shot at close range",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 15,
      BaseRange = 20f,
      BaseRadius = 0f,
      CooldownMs = 400,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Ammo, Amount = 1 },
        new ResourceCost { Kind = ResourceKind.Stamina, Amount = 3 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var fragGrenade = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Frag Grenade",
      Description = "Lob an explosive that damages all enemies in a radius",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 45,
      BaseRange = 25f,
      BaseRadius = 8f,
      CooldownMs = 6000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Ammo, Amount = 1 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var bazookaShot = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Bazooka Shot",
      Description = "Fire a rocket that deals heavy area damage",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Ground, TargetType.Enemy },
      BasePower = 70,
      BaseRange = 35f,
      BaseRadius = 6f,
      CooldownMs = 10000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Ammo, Amount = 2 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var landmine = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Landmine",
      Description = "Place an explosive that detonates when an enemy steps on it",
      Type = AbilityType.Terrain,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 60,
      BaseRange = 10f,
      BaseRadius = 5f,
      CooldownMs = 15000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Ammo, Amount = 1 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var fireball = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Fireball",
      Description = "Hurl a ball of fire that explodes on impact",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy, TargetType.Ground },
      BasePower = 50,
      BaseRange = 30f,
      BaseRadius = 6f,
      CooldownMs = 4000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 25 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var iceLance = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Ice Lance",
      Description = "Pierce the target with a shard of ice",
      Type = AbilityType.Damage,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 40,
      BaseRange = 35f,
      BaseRadius = 0f,
      CooldownMs = 3000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 20 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var healingMist = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Healing Mist",
      Description = "Release a restorative mist that heals allies in an area",
      Type = AbilityType.Heal,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly, TargetType.Ally, TargetType.Ground },
      BasePower = 40,
      BaseRange = 25f,
      BaseRadius = 8f,
      CooldownMs = 8000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 30 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
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
      BaseRange = 35f,
      BaseRadius = 10f,
      CooldownMs = 8000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 30 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var wardOfEarth = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Ward of Earth",
      Description = "Raise a wall of stone that provides cover",
      Type = AbilityType.Terrain,
      ValidTargets = new List<TargetType> { TargetType.Ground },
      BasePower = 0,
      BaseRange = 20f,
      BaseRadius = 5f,
      CooldownMs = 15000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 25 },
      },
      GrantedMods = new List<AbilityMod>(),
      EffectDurationMs = 0,
      AffectedAbilityIds = new List<ulong>(),
    });

    var fireEmpowerment = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Fire Empowerment",
      Description = "Imbue your attacks with fire for a short time",
      Type = AbilityType.Buff,
      ValidTargets = new List<TargetType> { TargetType.SelfOnly, TargetType.Ally },
      BasePower = 0,
      BaseRange = 15f,
      BaseRadius = 0f,
      CooldownMs = 20000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 30 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.DamageFlat, Value = 15 },
      },
      EffectDurationMs = 10000,
      AffectedAbilityIds = new List<ulong> { fireball.Id, arcaneBarrage.Id },
    });

    var weaken = ctx.Db.ability_def.Insert(new AbilityDef
    {
      Id = 0,
      Name = "Weaken",
      Description = "Curse an enemy, reducing their damage output",
      Type = AbilityType.Debuff,
      ValidTargets = new List<TargetType> { TargetType.Enemy },
      BasePower = 0,
      BaseRange = 25f,
      BaseRadius = 0f,
      CooldownMs = 18000,
      ResourceCosts = new List<ResourceCost>
      {
        new ResourceCost { Kind = ResourceKind.Mana, Amount = 20 },
      },
      GrantedMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.DamagePercent, Value = -0.25f },
      },
      EffectDurationMs = 12000,
      AffectedAbilityIds = new List<ulong>(),
    });

    // --- Weapons ---

    ctx.Db.weapon_def.Insert(new WeaponDef
    {
      Id = 0,
      Name = "Standard Rifle",
      Description = "A reliable bolt-action rifle with a bayonet attachment",
      PrimaryAbilityId = shootBullet.Id,
      SecondaryAbilityId = pistolShot.Id,
      BonusAbilityIds = new List<ulong> { bayonetStab.Id },
      PrimaryMods = new List<AbilityMod>(),
      SecondaryMods = new List<AbilityMod>(),
    });

    ctx.Db.weapon_def.Insert(new WeaponDef
    {
      Id = 0,
      Name = "Sniper Rifle",
      Description = "Long-range precision rifle with enhanced stopping power",
      PrimaryAbilityId = shootBullet.Id,
      SecondaryAbilityId = pistolShot.Id,
      BonusAbilityIds = new List<ulong>(),
      PrimaryMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.DamagePercent, Value = 0.30f },
        new AbilityMod { Type = ModType.RangePercent, Value = 0.50f },
        new AbilityMod { Type = ModType.CooldownPercent, Value = 0.40f },
      },
      SecondaryMods = new List<AbilityMod>(),
    });

    ctx.Db.weapon_def.Insert(new WeaponDef
    {
      Id = 0,
      Name = "Arcane Staff",
      Description = "A staff that channels magical energy for devastating spells",
      PrimaryAbilityId = fireball.Id,
      SecondaryAbilityId = iceLance.Id,
      BonusAbilityIds = new List<ulong>(),
      PrimaryMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.DamagePercent, Value = 0.15f },
      },
      SecondaryMods = new List<AbilityMod>
      {
        new AbilityMod { Type = ModType.RangePercent, Value = 0.20f },
      },
    });

    // --- Skills ---

    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0,
      Name = "Grenadier",
      Description = "Explosive ordnance specialist",
      AbilityIds = new List<ulong> { fragGrenade.Id, bazookaShot.Id, landmine.Id },
    });

    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0,
      Name = "Battle Mage",
      Description = "Offensive magic focused on destruction and control",
      AbilityIds = new List<ulong> { arcaneBarrage.Id, fireEmpowerment.Id, wardOfEarth.Id },
    });

    ctx.Db.skill_def.Insert(new SkillDef
    {
      Id = 0,
      Name = "Combat Medic",
      Description = "Support specialist with healing and debuff abilities",
      AbilityIds = new List<ulong> { healingMist.Id, weaken.Id },
    });

    Log.Info("Combat data seeded");
  }
}
