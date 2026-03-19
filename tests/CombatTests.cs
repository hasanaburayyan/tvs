using System;
using System.Linq;
using SpacetimeDB.Types;
using Xunit;

public class CombatDefinitionTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public CombatDefinitionTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void AbilityDefs_SeededOnInit()
  {
    var abilities = _client.Db.AbilityDef.Iter().ToList();
    Assert.True(abilities.Count >= 25, $"Expected at least 25 abilities, got {abilities.Count}");
  }

  [Fact]
  public void AbilityDef_FireRifle_HasCorrectStats()
  {
    var fireRifle = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Fire Rifle");
    Assert.NotNull(fireRifle);
    Assert.Equal(AbilityType.Damage, fireRifle.Type);
    Assert.Equal(30, fireRifle.BasePower);
    Assert.Equal(45f, fireRifle.BaseRange);
    Assert.Equal(0f, fireRifle.BaseRadius);
    Assert.Contains(TargetType.Enemy, fireRifle.ValidTargets);
  }

  [Fact]
  public void AbilityDef_Rally_HasBuffFields()
  {
    var buff = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Rally");
    Assert.NotNull(buff);
    Assert.Equal(AbilityType.Buff, buff.Type);
    Assert.NotEmpty(buff.GrantedMods);
    Assert.True(buff.EffectDurationMs > 0);
    Assert.Contains(TargetType.SelfOnly, buff.ValidTargets);
    Assert.Contains(TargetType.Ally, buff.ValidTargets);
  }

  [Fact]
  public void AbilityDef_Suppress_IsDebuff()
  {
    var debuff = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Suppress");
    Assert.NotNull(debuff);
    Assert.Equal(AbilityType.Debuff, debuff.Type);
    Assert.NotEmpty(debuff.GrantedMods);
    Assert.Contains(TargetType.Enemy, debuff.ValidTargets);
    var damageMod = debuff.GrantedMods.First(m => m.Type == ModType.DamagePercent);
    Assert.True(damageMod.Value < 0, "Suppress should have a negative damage modifier");
  }

  [Fact]
  public void WeaponDefs_SeededOnInit()
  {
    var weapons = _client.Db.WeaponDef.Iter().ToList();
    Assert.True(weapons.Count >= 4, $"Expected at least 4 weapons, got {weapons.Count}");
  }

  [Fact]
  public void WeaponDef_Rifle_HasCorrectFields()
  {
    var rifle = _client.Db.WeaponDef.Iter().FirstOrDefault(w => w.Name == "Rifle");
    Assert.NotNull(rifle);
    Assert.True(rifle.GrantsSupplies);
    Assert.NotNull(_client.Db.AbilityDef.Id.Find(rifle.PrimaryAbilityId));
  }

  [Fact]
  public void WeaponDef_AbilityReferences_AreValid()
  {
    foreach (var weapon in _client.Db.WeaponDef.Iter())
    {
      Assert.NotNull(_client.Db.AbilityDef.Id.Find(weapon.PrimaryAbilityId));
    }
  }

  [Fact]
  public void ArchetypeDefs_SeededOnInit()
  {
    var archetypes = _client.Db.ArchetypeDef.Iter().ToList();
    Assert.True(archetypes.Count >= 4, $"Expected at least 4 archetypes, got {archetypes.Count}");
  }

  [Fact]
  public void ArchetypeDef_Infantry_HasBonuses()
  {
    var infantry = _client.Db.ArchetypeDef.Iter().FirstOrDefault(a => a.Name == "Infantry");
    Assert.NotNull(infantry);
    Assert.Equal(30, infantry.BonusHealth);
    Assert.Equal(5, infantry.BonusArmor);
    Assert.NotEmpty(infantry.InnateAbilityIds);
  }

  [Fact]
  public void SkillDefs_SeededOnInit()
  {
    var skills = _client.Db.SkillDef.Iter().ToList();
    Assert.True(skills.Count >= 12, $"Expected at least 12 skills, got {skills.Count}");
  }

  [Fact]
  public void SkillDef_Evoker_HasCorrectAbilities()
  {
    var evoker = _client.Db.SkillDef.Iter().FirstOrDefault(s => s.Name == "Evoker");
    Assert.NotNull(evoker);
    Assert.Equal(3, evoker.AbilityIds.Count);

    var abilityNames = evoker.AbilityIds
      .Select(id => _client.Db.AbilityDef.Id.Find(id)?.Name)
      .ToList();
    Assert.Contains("Fireball", abilityNames);
    Assert.Contains("Enchant Weapon", abilityNames);
    Assert.Contains("Arcane Barrage", abilityNames);
  }

  [Fact]
  public void SkillDef_ArchetypeReferences_AreValid()
  {
    foreach (var skill in _client.Db.SkillDef.Iter())
    {
      Assert.NotNull(_client.Db.ArchetypeDef.Id.Find(skill.ArchetypeDefId));
    }
  }

  [Fact]
  public void SkillDef_AbilityReferences_AreValid()
  {
    foreach (var skill in _client.Db.SkillDef.Iter())
    {
      Assert.NotEmpty(skill.AbilityIds);
      foreach (var abilityId in skill.AbilityIds)
        Assert.NotNull(_client.Db.AbilityDef.Id.Find(abilityId));
    }
  }
}

public class LoadoutTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public LoadoutTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  private (ulong gameId, ulong archetypeId, ulong weaponId, ulong skillId) SetupLobby()
  {
    _client.CreatePlayerAndGetId("LoadoutPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First();
    var weapon = _client.Db.WeaponDef.Iter().First();
    var skill = _client.Db.SkillDef.Iter().First(s => s.ArchetypeDefId == archetype.Id);
    return (gameId, archetype.Id, weapon.Id, skill.Id);
  }

  [Fact]
  public void SetLoadout_InLobby_Succeeds()
  {
    var (gameId, archetypeId, weaponId, skillId) = SetupLobby();
    _client.Call(r => r.SetLoadout(gameId, archetypeId, weaponId, skillId));

    var loadouts = _client.Db.Loadout.GameSessionId.Filter(gameId).ToList();
    Assert.Single(loadouts);
    Assert.Equal(archetypeId, loadouts[0].ArchetypeDefId);
    Assert.Equal(weaponId, loadouts[0].WeaponDefId);
    Assert.Equal(skillId, loadouts[0].SkillDefId);
  }

  [Fact]
  public void SetLoadout_UpdateExisting_Succeeds()
  {
    var (gameId, archetypeId, weaponId, skillId) = SetupLobby();
    _client.Call(r => r.SetLoadout(gameId, archetypeId, weaponId, skillId));

    var weapon2 = _client.Db.WeaponDef.Iter().Skip(1).First();
    var skill2 = _client.Db.SkillDef.Iter().Where(s => s.ArchetypeDefId == archetypeId).Skip(1).FirstOrDefault()
      ?? _client.Db.SkillDef.Iter().First(s => s.ArchetypeDefId == archetypeId);
    _client.Call(r => r.SetLoadout(gameId, archetypeId, weapon2.Id, skill2.Id));

    var loadouts = _client.Db.Loadout.GameSessionId.Filter(gameId).ToList();
    Assert.Single(loadouts);
    Assert.Equal(weapon2.Id, loadouts[0].WeaponDefId);
    Assert.Equal(skill2.Id, loadouts[0].SkillDefId);
  }

  [Fact]
  public void SetLoadout_InvalidWeapon_Fails()
  {
    var (gameId, archetypeId, _, skillId) = SetupLobby();
    _client.CallExpectFailure(r => r.SetLoadout(gameId, archetypeId, 999999, skillId));
  }

  [Fact]
  public void SetLoadout_InvalidSkill_Fails()
  {
    var (gameId, archetypeId, weaponId, _) = SetupLobby();
    _client.CallExpectFailure(r => r.SetLoadout(gameId, archetypeId, weaponId, 999999));
  }

  [Fact]
  public void SetLoadout_NotInGame_Fails()
  {
    _client.CreatePlayerAndGetId("Outsider");
    var gameId = _client.CreateGame(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First();
    var weapon = _client.Db.WeaponDef.Iter().First();
    var skill = _client.Db.SkillDef.Iter().First(s => s.ArchetypeDefId == archetype.Id);
    _client.CallExpectFailure(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));
  }

  [Fact]
  public void SetLoadout_NoActivePlayer_Fails()
  {
    using var noPlayer = SpacetimeTestClient.Create();
    noPlayer.CallExpectFailure(r => r.SetLoadout(1, 1, 1, 1));
  }

  [Fact]
  public void SetLoadout_SkillArchetypeMismatch_Fails()
  {
    _client.CreatePlayerAndGetId("MismatchPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var infantry = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var officerSkill = _client.Db.SkillDef.Iter().First(s => s.Name == "Commissar");
    var weapon = _client.Db.WeaponDef.Iter().First();
    _client.CallExpectFailure(r => r.SetLoadout(gameId, infantry.Id, weapon.Id, officerSkill.Id));
  }
}

public class UseAbilityTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public UseAbilityTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  private (ulong gameId, ulong gpId, WeaponDef weapon, SkillDef skill, ArchetypeDef archetype) SetupWithLoadout(string playerName = "CombatPlayer")
  {
    _client.CreatePlayerAndGetId(playerName);
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));
    var gp = _client.GetGamePlayer(gameId);
    return (gameId, gp.Id, weapon, skill, archetype);
  }

  [Fact]
  public void UseAbility_WeaponPrimary_Succeeds()
  {
    var (gameId, gpId, weapon, _, _) = SetupWithLoadout();
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    Assert.Equal(weapon.PrimaryAbilityId, logs[0].AbilityId);
    Assert.Equal(BattleLogEventType.Attack, logs[0].EventType);
    Assert.Equal(gpId, logs[0].ActorGamePlayerId);
  }

  [Fact]
  public void UseAbility_SkillAbility_Succeeds()
  {
    var (gameId, _, _, skill, _) = SetupWithLoadout();
    var abilityId = skill.AbilityIds.First();
    _client.Call(r => r.UseAbility(gameId, abilityId, null, null, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    Assert.Equal(abilityId, logs[0].AbilityId);
    Assert.Equal(BattleLogEventType.Attack, logs[0].EventType);
  }

  [Fact]
  public void UseAbility_ArchetypeInnate_Succeeds()
  {
    var (gameId, _, _, _, archetype) = SetupWithLoadout();
    var innateId = archetype.InnateAbilityIds.First();
    _client.Call(r => r.UseAbility(gameId, innateId, null, null, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    Assert.Equal(innateId, logs[0].AbilityId);
    Assert.Equal(BattleLogEventType.Buff, logs[0].EventType);
  }

  [Fact]
  public void UseAbility_AbilityNotInLoadout_Fails()
  {
    var (gameId, _, _, _, _) = SetupWithLoadout();
    var healingMist = _client.Db.AbilityDef.Iter().First(a => a.Name == "Healing Mist");
    _client.CallExpectFailure(r => r.UseAbility(gameId, healingMist.Id, null, null, null, null, null));
  }

  [Fact]
  public void UseAbility_NoLoadout_Fails()
  {
    _client.CreatePlayerAndGetId("NoLoadout");
    var gameId = _client.CreateGameAndJoin(4);
    var ability = _client.Db.AbilityDef.Iter().First();
    _client.CallExpectFailure(r => r.UseAbility(gameId, ability.Id, null, null, null, null, null));
  }

  [Fact]
  public void UseAbility_InvalidAbility_Fails()
  {
    var (gameId, _, _, _, _) = SetupWithLoadout();
    _client.CallExpectFailure(r => r.UseAbility(gameId, 999999, null, null, null, null, null));
  }

  [Fact]
  public void UseAbility_WrongGame_Fails()
  {
    var (_, _, weapon, _, _) = SetupWithLoadout();
    _client.CallExpectFailure(r => r.UseAbility(999999, weapon.PrimaryAbilityId, null, null, null, null, null));
  }

  [Fact]
  public void UseAbility_WithTarget_LogsTarget()
  {
    using var other = SpacetimeTestClient.Create();
    other.CreatePlayerAndGetId("TargetDummy");

    _client.CreatePlayerAndGetId("Shooter");
    var gameId = _client.CreateGameAndJoin(4);
    other.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId != _client.Db.Player.Iter().First(p => p.ControllerIdentity == _client.Identity).Id);

    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, targetGp.Id, null, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    Assert.Contains(targetGp.Id, logs[0].TargetGamePlayerIds);

    other.ClearData();
  }

  [Fact]
  public void UseAbility_InvalidTarget_Fails()
  {
    var (gameId, _, weapon, _, _) = SetupWithLoadout();
    _client.CallExpectFailure(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, 999999, null, null, null, null));
  }

  [Fact]
  public void UseAbility_MultipleUses_LogsMultiple()
  {
    var (gameId, _, weapon, skill, _) = SetupWithLoadout();

    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null, null, null));
    _client.Call(r => r.UseAbility(gameId, skill.AbilityIds[0], null, null, null, null, null));
    _client.Call(r => r.UseAbility(gameId, skill.AbilityIds[1], null, null, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Equal(3, logs.Count);
  }
}

public class CooldownTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public CooldownTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void UseAbility_CreatesCooldownRow()
  {
    _client.CreatePlayerAndGetId("CooldownPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));
    var gp = _client.GetGamePlayer(gameId);

    // Use a Buff ability (not Damage) so isDryFire=false and cooldown is set
    var enchantWeapon = _client.Db.AbilityDef.Iter().First(a => a.Name == "Enchant Weapon");
    _client.Call(r => r.UseAbility(gameId, enchantWeapon.Id, null, null, null, null, null));

    var cooldowns = _client.Db.AbilityCooldown.GamePlayerId.Filter(gp.Id).ToList();
    Assert.Single(cooldowns);
    Assert.Equal(enchantWeapon.Id, cooldowns[0].AbilityId);
  }

  [Fact]
  public void UseAbility_OnCooldown_Fails()
  {
    _client.CreatePlayerAndGetId("CooldownBlock");
    var gameId = _client.CreateGameAndJoin(4);

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    // Use a Buff ability so cooldown is actually recorded (damage with no target = dry fire)
    var enchantWeapon = _client.Db.AbilityDef.Iter().First(a => a.Name == "Enchant Weapon");
    _client.Call(r => r.UseAbility(gameId, enchantWeapon.Id, null, null, null, null, null));

    // Enchant Weapon has 20000ms cooldown - immediate reuse should fail
    _client.CallExpectFailure(r => r.UseAbility(gameId, enchantWeapon.Id, null, null, null, null, null));
  }

  [Fact]
  public void UseAbility_DifferentAbilities_IndependentCooldowns()
  {
    _client.CreatePlayerAndGetId("MultiCD");
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    // Use non-damage abilities so cooldowns are actually recorded
    var enchantWeapon = _client.Db.AbilityDef.Iter().First(a => a.Name == "Enchant Weapon");
    _client.Call(r => r.UseAbility(gameId, enchantWeapon.Id, null, null, null, null, null));

    // Fortify is Infantry innate (Buff, cooldown 20000ms)
    var fortify = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fortify");
    _client.Call(r => r.UseAbility(gameId, fortify.Id, null, null, null, null, null));

    var gp = _client.GetGamePlayer(gameId);
    var cooldowns = _client.Db.AbilityCooldown.GamePlayerId.Filter(gp.Id).ToList();
    Assert.Equal(2, cooldowns.Count);
  }
}

public class BuffDebuffTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public BuffDebuffTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void UseAbility_Buff_CreatesActiveEffect()
  {
    _client.CreatePlayerAndGetId("BuffCaster");
    var gameId = _client.CreateGameAndJoin(4);

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var evoker = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, evoker.Id));

    var enchantWeapon = _client.Db.AbilityDef.Iter().First(a => a.Name == "Enchant Weapon");
    var gp = _client.GetGamePlayer(gameId);

    _client.Call(r => r.UseAbility(gameId, enchantWeapon.Id, null, null, null, null, null));

    var effects = _client.Db.ActiveEffect.GamePlayerId.Filter(gp.Id).ToList();
    Assert.Single(effects);
    Assert.Equal(enchantWeapon.Id, effects[0].SourceAbilityId);
    Assert.Equal(gp.Id, effects[0].CasterGamePlayerId);
    Assert.NotEmpty(effects[0].Mods);
  }

  [Fact]
  public void UseAbility_Debuff_CreatesActiveEffectOnTarget()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("DebuffTarget");

    _client.CreatePlayerAndGetId("DebuffCaster");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Officer");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var commissar = _client.Db.SkillDef.Iter().First(s => s.Name == "Commissar");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, commissar.Id));

    var suppress = _client.Db.AbilityDef.Iter().First(a => a.Name == "Suppress");
    var casterGp = _client.GetGamePlayer(gameId);

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "DebuffTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    _client.Call(r => r.UseAbility(gameId, suppress.Id, targetGp.Id, null, null, null, null));

    var effects = _client.Db.ActiveEffect.GamePlayerId.Filter(targetGp.Id).ToList();
    Assert.Single(effects);
    Assert.Equal(suppress.Id, effects[0].SourceAbilityId);
    Assert.Equal(casterGp.Id, effects[0].CasterGamePlayerId);

    target.ClearData();
  }

  [Fact]
  public void UseAbility_BuffedAbility_HasIncreasedPower()
  {
    _client.CreatePlayerAndGetId("BuffedAttacker");
    var gameId = _client.CreateGameAndJoin(4);

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var evoker = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, evoker.Id));

    var fireball = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    var enchantWeapon = _client.Db.AbilityDef.Iter().First(a => a.Name == "Enchant Weapon");

    // Apply Enchant Weapon buff first (+15 flat damage, affects all abilities)
    _client.Call(r => r.UseAbility(gameId, enchantWeapon.Id, null, null, null, null, null));

    // Now use Fireball - it should have increased power from the buff
    _client.Call(r => r.UseAbility(gameId, fireball.Id, null, null, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId)
      .Where(l => l.AbilityId == fireball.Id).ToList();
    Assert.Single(logs);

    // Base power (50) + 15 flat from buff = 65
    Assert.True(logs[0].ResolvedPower > fireball.BasePower,
      $"Buffed power ({logs[0].ResolvedPower}) should be > base power ({fireball.BasePower})");
  }

  [Fact]
  public void UseAbility_NoBuff_BasePower()
  {
    _client.CreatePlayerAndGetId("NoBuff");
    var gameId = _client.CreateGameAndJoin(4);

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var evoker = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, evoker.Id));

    var fireball = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    _client.Call(r => r.UseAbility(gameId, fireball.Id, null, null, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    Assert.Equal(fireball.BasePower, logs[0].ResolvedPower);
  }
}

public class BattleLogTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public BattleLogTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void BattleLog_RecordsTimestamp()
  {
    _client.CreatePlayerAndGetId("TimePlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null, null, null));

    var log = _client.Db.BattleLog.GameSessionId.Filter(gameId).First();
    Assert.True(log.OccurredAt.MicrosecondsSinceUnixEpoch > 0);
  }

  [Fact]
  public void BattleLog_FilterByActor()
  {
    using var other = SpacetimeTestClient.Create();
    other.CreatePlayerAndGetId("OtherActor");

    _client.CreatePlayerAndGetId("MainActor");
    var gameId = _client.CreateGameAndJoin(4);
    other.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));
    other.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    var myGp = _client.GetGamePlayer(gameId);
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null, null, null));

    var otherPlayer = other.Db.Player.Iter().First(p => p.Name == "OtherActor");
    var otherGp = other.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == otherPlayer.Id);
    other.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null, null, null));

    var myLogs = _client.Db.BattleLog.ActorGamePlayerId.Filter(myGp.Id).ToList();
    Assert.Single(myLogs);
    Assert.Equal(myGp.Id, myLogs[0].ActorGamePlayerId);

    var otherLogs = other.Db.BattleLog.ActorGamePlayerId.Filter(otherGp.Id).ToList();
    Assert.Single(otherLogs);
    Assert.Equal(otherGp.Id, otherLogs[0].ActorGamePlayerId);

    other.ClearData();
  }

  [Fact]
  public void BattleLog_NoTargetHasEmptyList()
  {
    _client.CreatePlayerAndGetId("SoloPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null, null, null));

    var log = _client.Db.BattleLog.GameSessionId.Filter(gameId).First();
    Assert.Empty(log.TargetGamePlayerIds);
  }

  [Fact]
  public void BattleLog_KillEvent_EmittedOnLethalDamage()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("KillTarget");

    _client.CreatePlayerAndGetId("Killer");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "KillTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);
    var killerGp = _client.GetGamePlayer(gameId);

    // Fire Rifle (30) + Fireball (50) + Arcane Barrage (60) = 140 > 100 HP
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, targetGp.Id, null, null, null, null));
    var fireball = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    _client.Call(r => r.UseAbility(gameId, fireball.Id, targetGp.Id, null, null, null, null));
    var arcaneBarrage = _client.Db.AbilityDef.Iter().First(a => a.Name == "Arcane Barrage");
    _client.Call(r => r.UseAbility(gameId, arcaneBarrage.Id, targetGp.Id, null, null, null, null));

    var targetAfter = _client.Db.GamePlayer.Id.Find(targetGp.Id)!;
    Assert.True(targetAfter.Dead);

    var killLogs = _client.Db.BattleLog.GameSessionId.Filter(gameId)
      .Where(l => l.EventType == BattleLogEventType.Kill).ToList();
    Assert.Single(killLogs);
    Assert.Equal(killerGp.Id, killLogs[0].ActorGamePlayerId);
    Assert.Contains(targetGp.Id, killLogs[0].TargetGamePlayerIds);
    Assert.True(killLogs[0].ResolvedPower > 0);

    target.ClearData();
  }

  [Fact]
  public void BattleLog_ReviveEvent_EmittedOnRespawn()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("ReviveTarget");

    _client.CreatePlayerAndGetId("ReviveKiller");
    var gameId = _client.CreateGame(4, 0);
    _client.Call(r => r.JoinGame(gameId));
    target.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "ReviveTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    // Kill the target
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, targetGp.Id, null, null, null, null));
    var fireball = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    _client.Call(r => r.UseAbility(gameId, fireball.Id, targetGp.Id, null, null, null, null));
    var arcaneBarrage = _client.Db.AbilityDef.Iter().First(a => a.Name == "Arcane Barrage");
    _client.Call(r => r.UseAbility(gameId, arcaneBarrage.Id, targetGp.Id, null, null, null, null));

    var targetAfter = _client.Db.GamePlayer.Id.Find(targetGp.Id)!;
    Assert.True(targetAfter.Dead);

    // Respawn (timer=0 so immediate)
    target.Call(r => r.Respawn(gameId));

    var reviveLogs = target.Db.BattleLog.GameSessionId.Filter(gameId)
      .Where(l => l.EventType == BattleLogEventType.Revive).ToList();
    Assert.Single(reviveLogs);
    Assert.Contains(targetGp.Id, reviveLogs[0].TargetGamePlayerIds);
    Assert.Null(reviveLogs[0].AbilityId);
    Assert.Equal(0, reviveLogs[0].ResolvedPower);

    target.ClearData();
  }

  [Fact]
  public void BattleLog_EventType_MapsCorrectly()
  {
    _client.CreatePlayerAndGetId("EventTypePlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    // Damage ability → Attack event
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null, null, null));
    var attackLog = _client.Db.BattleLog.GameSessionId.Filter(gameId).First();
    Assert.Equal(BattleLogEventType.Attack, attackLog.EventType);

    // Buff ability → Buff event
    var enchantWeapon = _client.Db.AbilityDef.Iter().First(a => a.Name == "Enchant Weapon");
    _client.Call(r => r.UseAbility(gameId, enchantWeapon.Id, null, null, null, null, null));
    var buffLog = _client.Db.BattleLog.GameSessionId.Filter(gameId)
      .First(l => l.AbilityId == enchantWeapon.Id);
    Assert.Equal(BattleLogEventType.Buff, buffLog.EventType);
  }
}

public class HealthArmorTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public HealthArmorTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void JoinGame_SetsDefaultHealthAndArmor()
  {
    _client.CreatePlayerAndGetId("HealthPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var gp = _client.GetGamePlayer(gameId);
    Assert.Equal(100, gp.Health);
    Assert.Equal(100, gp.MaxHealth);
    Assert.Equal(0, gp.Armor);
  }

  [Fact]
  public void SetLoadout_UpdatesHealthAndArmor()
  {
    _client.CreatePlayerAndGetId("ArchetypePlayer");
    var gameId = _client.CreateGameAndJoin(4);

    var infantry = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First();
    var skill = _client.Db.SkillDef.Iter().First(s => s.ArchetypeDefId == infantry.Id);
    _client.Call(r => r.SetLoadout(gameId, infantry.Id, weapon.Id, skill.Id));

    var gp = _client.GetGamePlayer(gameId);
    Assert.Equal(130, gp.MaxHealth); // 100 base + 30 Infantry bonus
    Assert.Equal(130, gp.Health);
    Assert.Equal(5, gp.Armor); // 0 base + 5 Infantry bonus
  }

  [Fact]
  public void UseAbility_DamageReducesTargetHealth()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("DmgTarget");

    _client.CreatePlayerAndGetId("DmgDealer");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "DmgTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    int healthBefore = targetGp.Health;
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, targetGp.Id, null, null, null, null));

    var targetAfter = _client.Db.GamePlayer.Id.Find(targetGp.Id)!;
    Assert.True(targetAfter.Health < healthBefore,
      $"Expected health ({targetAfter.Health}) < before ({healthBefore})");

    target.ClearData();
  }

  [Fact]
  public void UseAbility_HealthCannotGoBelowZero()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("TankTarget_HP");

    _client.CreatePlayerAndGetId("HeavyHitter_HP");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var rifle = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var evoker = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, rifle.Id, evoker.Id));

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "TankTarget_HP");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    // Use different abilities to avoid cooldowns. Target has 100 HP, 0 armor (no loadout).
    // Fire Rifle (30) + Fireball (50) + Arcane Barrage (60) = 140 > 100
    _client.Call(r => r.UseAbility(gameId, rifle.PrimaryAbilityId, targetGp.Id, null, null, null, null));
    var fireball = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    _client.Call(r => r.UseAbility(gameId, fireball.Id, targetGp.Id, null, null, null, null));
    var arcaneBarrage = _client.Db.AbilityDef.Iter().First(a => a.Name == "Arcane Barrage");
    _client.Call(r => r.UseAbility(gameId, arcaneBarrage.Id, targetGp.Id, null, null, null, null));

    var targetAfter = _client.Db.GamePlayer.Id.Find(targetGp.Id)!;
    Assert.Equal(0, targetAfter.Health);

    target.ClearData();
  }

  [Fact]
  public void UseAbility_NoTarget_NoDamageApplied()
  {
    _client.CreatePlayerAndGetId("SoloShooter");
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    var gpBefore = _client.GetGamePlayer(gameId);
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null, null, null));

    var gpAfter = _client.Db.GamePlayer.Id.Find(gpBefore.Id)!;
    Assert.Equal(gpBefore.Health, gpAfter.Health);
  }
}

public class ResourceTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public ResourceTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void SetLoadout_SeedsResourcePools()
  {
    _client.CreatePlayerAndGetId("PoolPlayer");
    var gameId = _client.CreateGameAndJoin(4);

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    var gp = _client.GetGamePlayer(gameId);
    var pools = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).ToList();
    Assert.Equal(3, pools.Count);

    var mana = pools.First(p => p.Kind == ResourceKind.Mana);
    Assert.Equal(100, mana.Current);
    Assert.Equal(100, mana.Max);

    var stamina = pools.First(p => p.Kind == ResourceKind.Stamina);
    Assert.Equal(100, stamina.Current);
    Assert.Equal(100, stamina.Max);

    var supplies = pools.First(p => p.Kind == ResourceKind.Supplies);
    Assert.Equal(30, supplies.Current);
    Assert.Equal(30, supplies.Max);
  }

  [Fact]
  public void UseAbility_DeductsSupplies()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("SupplyTarget");

    _client.CreatePlayerAndGetId("SupplyUser");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    var gp = _client.GetGamePlayer(gameId);
    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "SupplyTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(g => g.PlayerId == targetPlayer.Id);

    var suppliesBefore = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).First(p => p.Kind == ResourceKind.Supplies).Current;

    // Fire Rifle costs 1 Supplies (not dry fire since we have a target)
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, targetGp.Id, null, null, null, null));

    var suppliesAfter = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).First(p => p.Kind == ResourceKind.Supplies).Current;
    Assert.Equal(suppliesBefore - 1, suppliesAfter);

    target.ClearData();
  }

  [Fact]
  public void UseAbility_DeductsMana()
  {
    _client.CreatePlayerAndGetId("ManaUser");
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var evoker = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, evoker.Id));

    var gp = _client.GetGamePlayer(gameId);
    var manaBefore = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).First(p => p.Kind == ResourceKind.Mana).Current;

    // Enchant Weapon is a Buff (not Damage), so it always consumes resources (no dry-fire skip)
    var enchantWeapon = _client.Db.AbilityDef.Iter().First(a => a.Name == "Enchant Weapon");
    _client.Call(r => r.UseAbility(gameId, enchantWeapon.Id, null, null, null, null, null));

    var manaAfter = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).First(p => p.Kind == ResourceKind.Mana).Current;
    Assert.Equal(manaBefore - 30, manaAfter);
  }

  [Fact]
  public void UseAbility_InsufficientMana_Fails()
  {
    _client.CreatePlayerAndGetId("BrokePlayer");
    var gameId = _client.CreateGameAndJoin(4);

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Support");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Pistol");
    var priest = _client.Db.SkillDef.Iter().First(s => s.Name == "Priest");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, priest.Id));

    // Healing Mist (30 Mana) + Divine Shield (35 Mana) = 65 Mana used, 35 left
    var healingMist = _client.Db.AbilityDef.Iter().First(a => a.Name == "Healing Mist");
    _client.Call(r => r.UseAbility(gameId, healingMist.Id, null, null, null, null, null));

    var divineShield = _client.Db.AbilityDef.Iter().First(a => a.Name == "Divine Shield");
    _client.Call(r => r.UseAbility(gameId, divineShield.Id, null, null, null, null, null));

    // Holy Resurrect costs 60 Mana but only 35 left -- should fail
    var holyResurrect = _client.Db.AbilityDef.Iter().First(a => a.Name == "Holy Resurrect");
    _client.CallExpectFailure(r => r.UseAbility(gameId, holyResurrect.Id, null, null, null, null, null));
  }

  [Fact]
  public void ResourceCosts_AbilityDef_HasCorrectCosts()
  {
    var fireRifle = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fire Rifle");
    Assert.Single(fireRifle.ResourceCosts);
    Assert.Contains(fireRifle.ResourceCosts, c => c.Kind == ResourceKind.Supplies && c.Amount == 1);

    var fireball = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    Assert.Single(fireball.ResourceCosts);
    Assert.Equal(ResourceKind.Mana, fireball.ResourceCosts[0].Kind);
    Assert.Equal(25, fireball.ResourceCosts[0].Amount);
  }
}

public class DamageTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public DamageTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void UseAbility_HealingIncreasesHealth()
  {
    using var attacker = SpacetimeTestClient.Create();
    attacker.CreatePlayerAndGetId("HealAttacker");

    _client.CreatePlayerAndGetId("HealMedic");
    var gameId = _client.CreateGameAndJoin(4);
    attacker.Call(r => r.JoinGame(gameId));

    // Set up attacker loadout
    var infantry = attacker.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var rifle = attacker.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var evoker = attacker.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    attacker.Call(r => r.SetLoadout(gameId, infantry.Id, rifle.Id, evoker.Id));

    // Set up medic loadout
    var support = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Support");
    var pistol = _client.Db.WeaponDef.Iter().First(w => w.Name == "Pistol");
    var priest = _client.Db.SkillDef.Iter().First(s => s.Name == "Priest");
    _client.Call(r => r.SetLoadout(gameId, support.Id, pistol.Id, priest.Id));

    // Attacker damages medic
    var medicGp = _client.GetGamePlayer(gameId);
    attacker.Call(r => r.UseAbility(gameId, rifle.PrimaryAbilityId, medicGp.Id, null, null, null, null));

    // Medic heals themselves
    var healingMist = _client.Db.AbilityDef.Iter().First(a => a.Name == "Healing Mist");
    _client.Call(r => r.UseAbility(gameId, healingMist.Id, null, null, null, null, null));

    var medicHealed = _client.Db.GamePlayer.Id.Find(medicGp.Id)!;
    Assert.Equal(medicGp.Health, medicHealed.Health);

    attacker.ClearData();
  }

  [Fact]
  public void UseAbility_BuffAbility_NoDamageApplied()
  {
    _client.CreatePlayerAndGetId("BuffNoDmg");
    var gameId = _client.CreateGameAndJoin(4);

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var evoker = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, evoker.Id));

    var gp = _client.GetGamePlayer(gameId);
    var enchantWeapon = _client.Db.AbilityDef.Iter().First(a => a.Name == "Enchant Weapon");

    _client.Call(r => r.UseAbility(gameId, enchantWeapon.Id, null, null, null, null, null));

    var gpAfter = _client.Db.GamePlayer.Id.Find(gp.Id)!;
    Assert.Equal(gp.Health, gpAfter.Health);
  }

  [Fact]
  public void UseAbility_DamageRespectsArmor()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("ArmorTarget");

    _client.CreatePlayerAndGetId("ArmorTester");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var rifle = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, rifle.Id, skill.Id));

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "ArmorTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    _client.Call(r => r.UseAbility(gameId, rifle.PrimaryAbilityId, targetGp.Id, null, null, null, null));

    var log = _client.Db.BattleLog.GameSessionId.Filter(gameId).First();
    var targetAfter = _client.Db.GamePlayer.Id.Find(targetGp.Id)!;

    // Target has 0 armor (no loadout), so damage = resolved power, health = 100 - resolvedPower
    Assert.Equal(100 - log.ResolvedPower, targetAfter.Health);

    target.ClearData();
  }
}

public class TerrainAbilityTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public TerrainAbilityTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  private (ulong gameId, ulong gpId) SetupTechnician(string name = "TerrainCaster")
  {
    _client.CreatePlayerAndGetId(name);
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Support");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var technician = _client.Db.SkillDef.Iter().First(s => s.Name == "Technician");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, technician.Id));
    var gp = _client.GetGamePlayer(gameId);
    return (gameId, gp.Id);
  }

  [Fact]
  public void UseAbility_TerrainAbility_CreatesTerrainFeature()
  {
    var (gameId, gpId) = SetupTechnician();
    var buildFortification = _client.Db.AbilityDef.Iter().First(a => a.Name == "Build Fortification");

    var terrainBefore = _client.Db.TerrainFeature.GameSessionId.Filter(gameId)
      .Count(t => t.CasterGamePlayerId != null);

    var targetPos = new SpacetimeDB.Types.DbVector3(5f, 0f, 5f);
    _client.Call(r => r.UseAbility(gameId, buildFortification.Id, null, null, null, targetPos, null));

    var playerTerrain = _client.Db.TerrainFeature.GameSessionId.Filter(gameId)
      .Where(t => t.CasterGamePlayerId != null).ToList();
    Assert.Equal(terrainBefore + 1, playerTerrain.Count);

    var wall = playerTerrain.Last();
    Assert.Equal(TerrainType.Fortification, wall.Type);
    Assert.Equal(gpId, wall.CasterGamePlayerId);
    Assert.Equal(8f, wall.SizeX);
    Assert.Equal(2.5f, wall.SizeY);
    Assert.Equal(1f, wall.SizeZ);
  }

  [Fact]
  public void UseAbility_TerrainAbility_RequiresPosition()
  {
    var (gameId, _) = SetupTechnician("TerrainNoPos");
    var buildFortification = _client.Db.AbilityDef.Iter().First(a => a.Name == "Build Fortification");

    _client.CallExpectFailure(r => r.UseAbility(gameId, buildFortification.Id, null, null, null, null, null));
  }

  [Fact]
  public void UseAbility_TerrainAbility_OutOfRange_Fails()
  {
    var (gameId, _) = SetupTechnician("TerrainRange");
    var buildFortification = _client.Db.AbilityDef.Iter().First(a => a.Name == "Build Fortification");

    // Build Fortification has BaseRange=12, place at distance 100 (way out of range)
    var farPos = new SpacetimeDB.Types.DbVector3(100f, 0f, 100f);
    _client.CallExpectFailure(r => r.UseAbility(gameId, buildFortification.Id, null, null, null, farPos, null));
  }
}

public class LineOfSightTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public LineOfSightTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void SetTarget_ClearLoS_Succeeds()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("LoSTarget");

    _client.CreatePlayerAndGetId("LoSShooter");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    _client.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -50f), 0f));
    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "LoSTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);
    target.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -40f), 0f));

    _client.Call(r => r.SetTarget(gameId, targetGp.Id));

    var shooter = _client.GetGamePlayer(gameId);
    Assert.Equal(targetGp.Id, shooter.TargetGamePlayerId);

    target.ClearData();
  }

  [Fact]
  public void SetTarget_BlockedByWall_Fails()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("BlockedTarget");

    _client.CreatePlayerAndGetId("BlockedShooter");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    // Position players along Z axis at Y=3
    _client.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -10f), 0f));
    target.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, 10f), 0f));

    // Set up Support/Technician loadout and place an outpost between them
    var support = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Support");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var technician = _client.Db.SkillDef.Iter().First(s => s.Name == "Technician");
    _client.Call(r => r.SetLoadout(gameId, support.Id, weapon.Id, technician.Id));

    // Deploy Outpost (innate, range 15) creates a 4x3x4 structure (top at Y=3)
    var deployOutpost = _client.Db.AbilityDef.Iter().First(a => a.Name == "Deploy Outpost");
    var wallPos = new SpacetimeDB.Types.DbVector3(0f, 0f, 0f);
    _client.Call(r => r.UseAbility(gameId, deployOutpost.Id, null, null, null, wallPos, null));

    // Verify the outpost was placed
    var walls = _client.Db.TerrainFeature.GameSessionId.Filter(gameId)
      .Where(t => t.CasterGamePlayerId != null).ToList();
    Assert.NotEmpty(walls);

    // Try to set target -- should fail because outpost blocks LoS
    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "BlockedTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    var error = _client.CallExpectFailure(r => r.SetTarget(gameId, targetGp.Id));
    Assert.Contains("line of sight", error.ToLower());

    target.ClearData();
  }

  [Fact]
  public void UseAbility_Targeted_BlockedByWall_Fails()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("AbilityBlockTarget");

    _client.CreatePlayerAndGetId("AbilityBlockShooter");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    // Position players
    _client.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -10f), 0f));
    target.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, 10f), 0f));

    // Set up Support/Technician loadout and place an outpost
    var support = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Support");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var technician = _client.Db.SkillDef.Iter().First(s => s.Name == "Technician");
    _client.Call(r => r.SetLoadout(gameId, support.Id, weapon.Id, technician.Id));

    var deployOutpost = _client.Db.AbilityDef.Iter().First(a => a.Name == "Deploy Outpost");
    _client.Call(r => r.UseAbility(gameId, deployOutpost.Id, null, null, null, new SpacetimeDB.Types.DbVector3(0f, 0f, 0f), null));

    // Try targeted ability through the outpost
    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "AbilityBlockTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    var error = _client.CallExpectFailure(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, targetGp.Id, null, null, null, null));
    Assert.Contains("line of sight", error.ToLower());

    target.ClearData();
  }

  [Fact]
  public void SetTarget_ClearTargetToNull_Succeeds()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("ClearTarget");

    _client.CreatePlayerAndGetId("ClearShooter");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    _client.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -5f), 0f));
    target.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, 5f), 0f));

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "ClearTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    _client.Call(r => r.SetTarget(gameId, targetGp.Id));
    var shooter = _client.GetGamePlayer(gameId);
    Assert.Equal(targetGp.Id, shooter.TargetGamePlayerId);

    _client.Call(r => r.SetTarget(gameId, null));
    shooter = _client.GetGamePlayer(gameId);
    Assert.Null(shooter.TargetGamePlayerId);

    target.ClearData();
  }

  [Fact]
  public void LoS_TrenchDoesNotBlock()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("TrenchTarget");

    _client.CreatePlayerAndGetId("TrenchShooter");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    // Players at Y=3 (standing height), map trenches are below player height
    _client.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -50f), 0f));
    target.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -40f), 0f));

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "TrenchTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    _client.Call(r => r.SetTarget(gameId, targetGp.Id));
    var shooter = _client.GetGamePlayer(gameId);
    Assert.Equal(targetGp.Id, shooter.TargetGamePlayerId);

    target.ClearData();
  }
}
