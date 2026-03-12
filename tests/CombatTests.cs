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
    Assert.True(abilities.Count >= 13, $"Expected at least 13 abilities, got {abilities.Count}");
  }

  [Fact]
  public void AbilityDef_ShootBullet_HasCorrectStats()
  {
    var bullet = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Shoot Bullet");
    Assert.NotNull(bullet);
    Assert.Equal(AbilityType.Damage, bullet.Type);
    Assert.Equal(20, bullet.BasePower);
    Assert.Equal(40f, bullet.BaseRange);
    Assert.Equal(0f, bullet.BaseRadius);
    Assert.Contains(TargetType.Enemy, bullet.ValidTargets);
  }

  [Fact]
  public void AbilityDef_FireEmpowerment_HasBuffFields()
  {
    var buff = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Fire Empowerment");
    Assert.NotNull(buff);
    Assert.Equal(AbilityType.Buff, buff.Type);
    Assert.NotEmpty(buff.GrantedMods);
    Assert.True(buff.EffectDurationMs > 0);
    Assert.NotEmpty(buff.AffectedAbilityIds);
    Assert.Contains(TargetType.SelfOnly, buff.ValidTargets);
    Assert.Contains(TargetType.Ally, buff.ValidTargets);
  }

  [Fact]
  public void AbilityDef_Weaken_IsDebuff()
  {
    var debuff = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Weaken");
    Assert.NotNull(debuff);
    Assert.Equal(AbilityType.Debuff, debuff.Type);
    Assert.NotEmpty(debuff.GrantedMods);
    Assert.Contains(TargetType.Enemy, debuff.ValidTargets);
    var damageMod = debuff.GrantedMods.First(m => m.Type == ModType.DamagePercent);
    Assert.True(damageMod.Value < 0, "Weaken should have a negative damage modifier");
  }

  [Fact]
  public void WeaponDefs_SeededOnInit()
  {
    var weapons = _client.Db.WeaponDef.Iter().ToList();
    Assert.True(weapons.Count >= 3, $"Expected at least 3 weapons, got {weapons.Count}");
  }

  [Fact]
  public void WeaponDef_SniperRifle_HasMods()
  {
    var sniper = _client.Db.WeaponDef.Iter().FirstOrDefault(w => w.Name == "Sniper Rifle");
    Assert.NotNull(sniper);
    Assert.NotEmpty(sniper.PrimaryMods);
    Assert.Contains(sniper.PrimaryMods, m => m.Type == ModType.DamagePercent);
    Assert.Contains(sniper.PrimaryMods, m => m.Type == ModType.RangePercent);
  }

  [Fact]
  public void WeaponDef_StandardRifle_HasBonusAbility()
  {
    var rifle = _client.Db.WeaponDef.Iter().FirstOrDefault(w => w.Name == "Standard Rifle");
    Assert.NotNull(rifle);
    Assert.NotEmpty(rifle.BonusAbilityIds);

    var bayonet = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Bayonet Stab");
    Assert.NotNull(bayonet);
    Assert.Contains(bayonet.Id, rifle.BonusAbilityIds);
  }

  [Fact]
  public void WeaponDef_AbilityReferences_AreValid()
  {
    foreach (var weapon in _client.Db.WeaponDef.Iter())
    {
      Assert.NotNull(_client.Db.AbilityDef.Id.Find(weapon.PrimaryAbilityId));
      Assert.NotNull(_client.Db.AbilityDef.Id.Find(weapon.SecondaryAbilityId));
      foreach (var bonusId in weapon.BonusAbilityIds)
        Assert.NotNull(_client.Db.AbilityDef.Id.Find(bonusId));
    }
  }

  [Fact]
  public void SkillDefs_SeededOnInit()
  {
    var skills = _client.Db.SkillDef.Iter().ToList();
    Assert.True(skills.Count >= 3, $"Expected at least 3 skills, got {skills.Count}");
  }

  [Fact]
  public void SkillDef_Grenadier_HasCorrectAbilities()
  {
    var grenadier = _client.Db.SkillDef.Iter().FirstOrDefault(s => s.Name == "Grenadier");
    Assert.NotNull(grenadier);
    Assert.Equal(3, grenadier.AbilityIds.Count);

    var abilityNames = grenadier.AbilityIds
      .Select(id => _client.Db.AbilityDef.Id.Find(id)?.Name)
      .ToList();
    Assert.Contains("Frag Grenade", abilityNames);
    Assert.Contains("Bazooka Shot", abilityNames);
    Assert.Contains("Landmine", abilityNames);
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

  private (ulong gameId, ulong weaponId, ulong skillId) SetupLobby()
  {
    _client.CreatePlayerAndGetId("LoadoutPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var weapon = _client.Db.WeaponDef.Iter().First();
    var skill = _client.Db.SkillDef.Iter().First();
    return (gameId, weapon.Id, skill.Id);
  }

  [Fact]
  public void SetLoadout_InLobby_Succeeds()
  {
    var (gameId, weaponId, skillId) = SetupLobby();
    _client.Call(r => r.SetLoadout(gameId, weaponId, skillId));

    var loadouts = _client.Db.Loadout.GameSessionId.Filter(gameId).ToList();
    Assert.Single(loadouts);
    Assert.Equal(weaponId, loadouts[0].WeaponDefId);
    Assert.Equal(skillId, loadouts[0].SkillDefId);
  }

  [Fact]
  public void SetLoadout_UpdateExisting_Succeeds()
  {
    var (gameId, weaponId, skillId) = SetupLobby();
    _client.Call(r => r.SetLoadout(gameId, weaponId, skillId));

    var weapon2 = _client.Db.WeaponDef.Iter().Skip(1).First();
    var skill2 = _client.Db.SkillDef.Iter().Skip(1).First();
    _client.Call(r => r.SetLoadout(gameId, weapon2.Id, skill2.Id));

    var loadouts = _client.Db.Loadout.GameSessionId.Filter(gameId).ToList();
    Assert.Single(loadouts);
    Assert.Equal(weapon2.Id, loadouts[0].WeaponDefId);
    Assert.Equal(skill2.Id, loadouts[0].SkillDefId);
  }

  [Fact]
  public void SetLoadout_InvalidWeapon_Fails()
  {
    var (gameId, _, skillId) = SetupLobby();
    _client.CallExpectFailure(r => r.SetLoadout(gameId, 999999, skillId));
  }

  [Fact]
  public void SetLoadout_InvalidSkill_Fails()
  {
    var (gameId, weaponId, _) = SetupLobby();
    _client.CallExpectFailure(r => r.SetLoadout(gameId, weaponId, 999999));
  }

  [Fact]
  public void SetLoadout_NotInGame_Fails()
  {
    _client.CreatePlayerAndGetId("Outsider");
    var gameId = _client.CreateGame(4);
    var weapon = _client.Db.WeaponDef.Iter().First();
    var skill = _client.Db.SkillDef.Iter().First();
    _client.CallExpectFailure(r => r.SetLoadout(gameId, weapon.Id, skill.Id));
  }

  [Fact]
  public void SetLoadout_NoActivePlayer_Fails()
  {
    using var noPlayer = SpacetimeTestClient.Create();
    noPlayer.CallExpectFailure(r => r.SetLoadout(1, 1, 1));
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

  private (ulong gameId, ulong gpId, WeaponDef weapon, SkillDef skill) SetupWithLoadout(string playerName = "CombatPlayer")
  {
    _client.CreatePlayerAndGetId(playerName);
    var gameId = _client.CreateGameAndJoin(4);
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Grenadier");
    _client.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));
    var gp = _client.GetGamePlayer(gameId);
    return (gameId, gp.Id, weapon, skill);
  }

  [Fact]
  public void UseAbility_WeaponPrimary_Succeeds()
  {
    var (gameId, gpId, weapon, _) = SetupWithLoadout();
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    Assert.Equal(weapon.PrimaryAbilityId, logs[0].AbilityId);
    Assert.True(logs[0].FromWeapon);
    Assert.Equal(gpId, logs[0].ActorGamePlayerId);
  }

  [Fact]
  public void UseAbility_WeaponSecondary_Succeeds()
  {
    var (gameId, _, weapon, _) = SetupWithLoadout();
    _client.Call(r => r.UseAbility(gameId, weapon.SecondaryAbilityId, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    Assert.Equal(weapon.SecondaryAbilityId, logs[0].AbilityId);
    Assert.True(logs[0].FromWeapon);
  }

  [Fact]
  public void UseAbility_WeaponBonus_Succeeds()
  {
    var (gameId, _, weapon, _) = SetupWithLoadout();
    var bonusId = weapon.BonusAbilityIds.First();
    _client.Call(r => r.UseAbility(gameId, bonusId, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    Assert.Equal(bonusId, logs[0].AbilityId);
    Assert.True(logs[0].FromWeapon);
  }

  [Fact]
  public void UseAbility_SkillAbility_Succeeds()
  {
    var (gameId, _, _, skill) = SetupWithLoadout();
    var abilityId = skill.AbilityIds.First();
    _client.Call(r => r.UseAbility(gameId, abilityId, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    Assert.Equal(abilityId, logs[0].AbilityId);
    Assert.False(logs[0].FromWeapon);
  }

  [Fact]
  public void UseAbility_AbilityNotInLoadout_Fails()
  {
    var (gameId, _, _, _) = SetupWithLoadout();
    var arcaneBarrage = _client.Db.AbilityDef.Iter().First(a => a.Name == "Arcane Barrage");
    _client.CallExpectFailure(r => r.UseAbility(gameId, arcaneBarrage.Id, null, null, null));
  }

  [Fact]
  public void UseAbility_NoLoadout_Fails()
  {
    _client.CreatePlayerAndGetId("NoLoadout");
    var gameId = _client.CreateGameAndJoin(4);
    var ability = _client.Db.AbilityDef.Iter().First();
    _client.CallExpectFailure(r => r.UseAbility(gameId, ability.Id, null, null, null));
  }

  [Fact]
  public void UseAbility_InvalidAbility_Fails()
  {
    var (gameId, _, _, _) = SetupWithLoadout();
    _client.CallExpectFailure(r => r.UseAbility(gameId, 999999, null, null, null));
  }

  [Fact]
  public void UseAbility_WrongGame_Fails()
  {
    var (_, _, weapon, _) = SetupWithLoadout();
    _client.CallExpectFailure(r => r.UseAbility(999999, weapon.PrimaryAbilityId, null, null, null));
  }

  [Fact]
  public void UseAbility_WithTarget_LogsTarget()
  {
    using var other = SpacetimeTestClient.Create();
    other.CreatePlayerAndGetId("TargetDummy");

    _client.CreatePlayerAndGetId("Shooter");
    var gameId = _client.CreateGameAndJoin(4);
    other.Call(r => r.JoinGame(gameId));

    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var skill = _client.Db.SkillDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));

    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId != _client.Db.Player.Iter().First(p => p.ControllerIdentity == _client.Identity).Id);

    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, targetGp.Id, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    Assert.Contains(targetGp.Id, logs[0].TargetGamePlayerIds);

    other.ClearData();
  }

  [Fact]
  public void UseAbility_InvalidTarget_Fails()
  {
    var (gameId, _, weapon, _) = SetupWithLoadout();
    _client.CallExpectFailure(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, 999999, null, null));
  }

  [Fact]
  public void UseAbility_MultipleUses_LogsMultiple()
  {
    var (gameId, _, weapon, _) = SetupWithLoadout();

    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null));
    _client.Call(r => r.UseAbility(gameId, weapon.SecondaryAbilityId, null, null, null));
    _client.Call(r => r.UseAbility(gameId, weapon.BonusAbilityIds.First(), null, null, null));

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
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var skill = _client.Db.SkillDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));
    var gp = _client.GetGamePlayer(gameId);

    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null));

    var cooldowns = _client.Db.AbilityCooldown.GamePlayerId.Filter(gp.Id).ToList();
    Assert.Single(cooldowns);
    Assert.Equal(weapon.PrimaryAbilityId, cooldowns[0].AbilityId);
  }

  [Fact]
  public void UseAbility_OnCooldown_Fails()
  {
    _client.CreatePlayerAndGetId("CooldownBlock");
    var gameId = _client.CreateGameAndJoin(4);

    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Grenadier");
    var weapon = _client.Db.WeaponDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));

    var fragGrenade = _client.Db.AbilityDef.Iter().First(a => a.Name == "Frag Grenade");
    _client.Call(r => r.UseAbility(gameId, fragGrenade.Id, null, null, null));

    // Frag Grenade has 6000ms cooldown - immediate reuse should fail
    _client.CallExpectFailure(r => r.UseAbility(gameId, fragGrenade.Id, null, null, null));
  }

  [Fact]
  public void UseAbility_DifferentAbilities_IndependentCooldowns()
  {
    _client.CreatePlayerAndGetId("MultiCD");
    var gameId = _client.CreateGameAndJoin(4);
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Grenadier");
    _client.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));

    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null));

    // Using a different ability should still work even though primary is on cooldown
    var fragGrenade = _client.Db.AbilityDef.Iter().First(a => a.Name == "Frag Grenade");
    _client.Call(r => r.UseAbility(gameId, fragGrenade.Id, null, null, null));

    var gp = _client.GetGamePlayer(gameId);
    var cooldowns = _client.Db.AbilityCooldown.GamePlayerId.Filter(gp.Id).ToList();
    Assert.Equal(2, cooldowns.Count);
  }
}

public class WeaponModTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public WeaponModTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void UseAbility_SniperRifle_AppliesDamageMods()
  {
    _client.CreatePlayerAndGetId("SniperUser");
    var gameId = _client.CreateGameAndJoin(4);
    var sniper = _client.Db.WeaponDef.Iter().First(w => w.Name == "Sniper Rifle");
    var skill = _client.Db.SkillDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, sniper.Id, skill.Id));

    _client.Call(r => r.UseAbility(gameId, sniper.PrimaryAbilityId, null, null, null));

    var shootBullet = _client.Db.AbilityDef.Id.Find(sniper.PrimaryAbilityId)!;
    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    // Sniper has +30% damage mod, so resolved power should be > base power
    Assert.True(logs[0].ResolvedPower > shootBullet.BasePower,
      $"Expected resolved power ({logs[0].ResolvedPower}) > base power ({shootBullet.BasePower})");
  }

  [Fact]
  public void UseAbility_StandardRifle_NoMods_BasePower()
  {
    _client.CreatePlayerAndGetId("RifleUser");
    var gameId = _client.CreateGameAndJoin(4);
    var rifle = _client.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var skill = _client.Db.SkillDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, rifle.Id, skill.Id));

    _client.Call(r => r.UseAbility(gameId, rifle.PrimaryAbilityId, null, null, null));

    var shootBullet = _client.Db.AbilityDef.Id.Find(rifle.PrimaryAbilityId)!;
    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    // Standard rifle has no primary mods, so resolved power == base power
    Assert.Equal(shootBullet.BasePower, logs[0].ResolvedPower);
  }

  [Fact]
  public void UseAbility_SkillAbility_NotModdedByWeapon()
  {
    _client.CreatePlayerAndGetId("SkillPure");
    var gameId = _client.CreateGameAndJoin(4);
    var sniper = _client.Db.WeaponDef.Iter().First(w => w.Name == "Sniper Rifle");
    var grenadier = _client.Db.SkillDef.Iter().First(s => s.Name == "Grenadier");
    _client.Call(r => r.SetLoadout(gameId, sniper.Id, grenadier.Id));

    var fragGrenade = _client.Db.AbilityDef.Iter().First(a => a.Name == "Frag Grenade");
    _client.Call(r => r.UseAbility(gameId, fragGrenade.Id, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId).ToList();
    Assert.Single(logs);
    // Skill abilities should not be affected by weapon mods
    Assert.Equal(fragGrenade.BasePower, logs[0].ResolvedPower);
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

    var staff = _client.Db.WeaponDef.Iter().First(w => w.Name == "Arcane Staff");
    var battleMage = _client.Db.SkillDef.Iter().First(s => s.Name == "Battle Mage");
    _client.Call(r => r.SetLoadout(gameId, staff.Id, battleMage.Id));

    var fireEmpowerment = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fire Empowerment");
    var gp = _client.GetGamePlayer(gameId);

    // Self-cast buff (no target = self)
    _client.Call(r => r.UseAbility(gameId, fireEmpowerment.Id, null, null, null));

    var effects = _client.Db.ActiveEffect.GamePlayerId.Filter(gp.Id).ToList();
    Assert.Single(effects);
    Assert.Equal(fireEmpowerment.Id, effects[0].SourceAbilityId);
    Assert.Equal(gp.Id, effects[0].CasterGamePlayerId);
    Assert.NotEmpty(effects[0].Mods);
    Assert.NotEmpty(effects[0].AffectedAbilityIds);
  }

  [Fact]
  public void UseAbility_Debuff_CreatesActiveEffectOnTarget()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("DebuffTarget");

    _client.CreatePlayerAndGetId("DebuffCaster");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    var staff = _client.Db.WeaponDef.Iter().First(w => w.Name == "Arcane Staff");
    var combatMedic = _client.Db.SkillDef.Iter().First(s => s.Name == "Combat Medic");
    _client.Call(r => r.SetLoadout(gameId, staff.Id, combatMedic.Id));

    var weaken = _client.Db.AbilityDef.Iter().First(a => a.Name == "Weaken");
    var casterGp = _client.GetGamePlayer(gameId);

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "DebuffTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    _client.Call(r => r.UseAbility(gameId, weaken.Id, targetGp.Id, null, null));

    var effects = _client.Db.ActiveEffect.GamePlayerId.Filter(targetGp.Id).ToList();
    Assert.Single(effects);
    Assert.Equal(weaken.Id, effects[0].SourceAbilityId);
    Assert.Equal(casterGp.Id, effects[0].CasterGamePlayerId);

    target.ClearData();
  }

  [Fact]
  public void UseAbility_BuffedAbility_HasIncreasedPower()
  {
    _client.CreatePlayerAndGetId("BuffedAttacker");
    var gameId = _client.CreateGameAndJoin(4);

    var staff = _client.Db.WeaponDef.Iter().First(w => w.Name == "Arcane Staff");
    var battleMage = _client.Db.SkillDef.Iter().First(s => s.Name == "Battle Mage");
    _client.Call(r => r.SetLoadout(gameId, staff.Id, battleMage.Id));

    var fireball = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    var fireEmpowerment = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fire Empowerment");

    // Apply fire empowerment buff first
    _client.Call(r => r.UseAbility(gameId, fireEmpowerment.Id, null, null, null));

    // Now use fireball - it should have increased power from the buff + weapon mods
    // Arcane Staff has +15% primary mod on Fireball, Fire Empowerment adds +15 flat damage
    _client.Call(r => r.UseAbility(gameId, fireball.Id, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId)
      .Where(l => l.AbilityId == fireball.Id).ToList();
    Assert.Single(logs);

    // Base power (50) + 15% weapon mod = 57, then +15 flat from buff = 72
    Assert.True(logs[0].ResolvedPower > fireball.BasePower,
      $"Buffed power ({logs[0].ResolvedPower}) should be > base power ({fireball.BasePower})");
  }

  [Fact]
  public void UseAbility_Buff_DoesNotAffectUnrelatedAbilities()
  {
    _client.CreatePlayerAndGetId("BuffScope");
    var gameId = _client.CreateGameAndJoin(4);

    var staff = _client.Db.WeaponDef.Iter().First(w => w.Name == "Arcane Staff");
    var battleMage = _client.Db.SkillDef.Iter().First(s => s.Name == "Battle Mage");
    _client.Call(r => r.SetLoadout(gameId, staff.Id, battleMage.Id));

    var iceLance = _client.Db.AbilityDef.Iter().First(a => a.Name == "Ice Lance");
    var fireEmpowerment = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fire Empowerment");

    // Apply fire empowerment (only affects Fireball and Arcane Barrage, not Ice Lance)
    _client.Call(r => r.UseAbility(gameId, fireEmpowerment.Id, null, null, null));

    // Use Ice Lance - the buff should NOT affect it since Ice Lance is not in AffectedAbilityIds
    // Arcane Staff has +20% range mod on secondary (Ice Lance) but no damage mods
    _client.Call(r => r.UseAbility(gameId, iceLance.Id, null, null, null));

    var logs = _client.Db.BattleLog.GameSessionId.Filter(gameId)
      .Where(l => l.AbilityId == iceLance.Id).ToList();
    Assert.Single(logs);
    // Ice Lance base power is 40, no damage mods from weapon or buff
    Assert.Equal(iceLance.BasePower, logs[0].ResolvedPower);
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
    var weapon = _client.Db.WeaponDef.Iter().First();
    var skill = _client.Db.SkillDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));

    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null));

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

    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var skill = _client.Db.SkillDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));
    other.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));

    var myGp = _client.GetGamePlayer(gameId);
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null));

    var otherPlayer = other.Db.Player.Iter().First(p => p.Name == "OtherActor");
    var otherGp = other.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == otherPlayer.Id);
    other.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null));

    // Each client reads their own logs from their own cache
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
    var weapon = _client.Db.WeaponDef.Iter().First();
    var skill = _client.Db.SkillDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));

    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null));

    var log = _client.Db.BattleLog.GameSessionId.Filter(gameId).First();
    Assert.Empty(log.TargetGamePlayerIds);
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
  public void JoinGame_SetsHealthAndArmor()
  {
    _client.CreatePlayerAndGetId("HealthPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var gp = _client.GetGamePlayer(gameId);
    Assert.Equal(100, gp.Health);
    Assert.Equal(100, gp.MaxHealth);
    Assert.Equal(0, gp.Armor);
  }

  [Fact]
  public void UseAbility_DamageReducesTargetHealth()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("DmgTarget");

    _client.CreatePlayerAndGetId("DmgDealer");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var skill = _client.Db.SkillDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "DmgTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    int healthBefore = targetGp.Health;
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, targetGp.Id, null, null));

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

    var rifle = _client.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var grenadier = _client.Db.SkillDef.Iter().First(s => s.Name == "Grenadier");
    _client.Call(r => r.SetLoadout(gameId, rifle.Id, grenadier.Id));

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "TankTarget_HP");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    // Use different abilities to avoid cooldowns. Combined damage > 100hp.
    // Shoot Bullet (20) + Pistol Shot (15) + Bayonet Stab (35) + Frag Grenade (45) = 115
    _client.Call(r => r.UseAbility(gameId, rifle.PrimaryAbilityId, targetGp.Id, null, null));
    _client.Call(r => r.UseAbility(gameId, rifle.SecondaryAbilityId, targetGp.Id, null, null));
    _client.Call(r => r.UseAbility(gameId, rifle.BonusAbilityIds.First(), targetGp.Id, null, null));
    var frag = _client.Db.AbilityDef.Iter().First(a => a.Name == "Frag Grenade");
    _client.Call(r => r.UseAbility(gameId, frag.Id, targetGp.Id, null, null));

    var targetAfter = _client.Db.GamePlayer.Id.Find(targetGp.Id)!;
    Assert.Equal(0, targetAfter.Health);

    target.ClearData();
  }

  [Fact]
  public void UseAbility_NoTarget_NoDamageApplied()
  {
    _client.CreatePlayerAndGetId("SoloShooter");
    var gameId = _client.CreateGameAndJoin(4);
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var skill = _client.Db.SkillDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));

    var gpBefore = _client.GetGamePlayer(gameId);
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null));

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
  public void JoinGame_SeedsResourcePools()
  {
    _client.CreatePlayerAndGetId("PoolPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var gp = _client.GetGamePlayer(gameId);

    var pools = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).ToList();
    Assert.Equal(3, pools.Count);

    var mana = pools.First(p => p.Kind == ResourceKind.Mana);
    Assert.Equal(100, mana.Current);
    Assert.Equal(100, mana.Max);

    var stamina = pools.First(p => p.Kind == ResourceKind.Stamina);
    Assert.Equal(100, stamina.Current);
    Assert.Equal(100, stamina.Max);

    var ammo = pools.First(p => p.Kind == ResourceKind.Ammo);
    Assert.Equal(30, ammo.Current);
    Assert.Equal(30, ammo.Max);
  }

  [Fact]
  public void UseAbility_DeductsAmmoAndStamina()
  {
    _client.CreatePlayerAndGetId("AmmoUser");
    var gameId = _client.CreateGameAndJoin(4);
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var skill = _client.Db.SkillDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, weapon.Id, skill.Id));

    var gp = _client.GetGamePlayer(gameId);
    var ammoBefore = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).First(p => p.Kind == ResourceKind.Ammo).Current;
    var staminaBefore = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).First(p => p.Kind == ResourceKind.Stamina).Current;

    // Shoot Bullet costs 1 Ammo + 5 Stamina
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, null, null, null));

    var ammoAfter = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).First(p => p.Kind == ResourceKind.Ammo).Current;
    var staminaAfter = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).First(p => p.Kind == ResourceKind.Stamina).Current;

    Assert.Equal(ammoBefore - 1, ammoAfter);
    Assert.Equal(staminaBefore - 5, staminaAfter);
  }

  [Fact]
  public void UseAbility_DeductsMana()
  {
    _client.CreatePlayerAndGetId("ManaUser");
    var gameId = _client.CreateGameAndJoin(4);
    var staff = _client.Db.WeaponDef.Iter().First(w => w.Name == "Arcane Staff");
    var battleMage = _client.Db.SkillDef.Iter().First(s => s.Name == "Battle Mage");
    _client.Call(r => r.SetLoadout(gameId, staff.Id, battleMage.Id));

    var gp = _client.GetGamePlayer(gameId);
    var manaBefore = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).First(p => p.Kind == ResourceKind.Mana).Current;

    // Fireball costs 25 Mana
    _client.Call(r => r.UseAbility(gameId, staff.PrimaryAbilityId, null, null, null));

    var manaAfter = _client.Db.ResourcePool.GamePlayerId.Filter(gp.Id).First(p => p.Kind == ResourceKind.Mana).Current;
    Assert.Equal(manaBefore - 25, manaAfter);
  }

  [Fact]
  public void UseAbility_InsufficientMana_Fails()
  {
    _client.CreatePlayerAndGetId("BrokePlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var staff = _client.Db.WeaponDef.Iter().First(w => w.Name == "Arcane Staff");
    var battleMage = _client.Db.SkillDef.Iter().First(s => s.Name == "Battle Mage");
    _client.Call(r => r.SetLoadout(gameId, staff.Id, battleMage.Id));

    // Use diverse abilities to drain mana without hitting cooldowns.
    // Fireball(25) + Ice Lance(20) + Arcane Barrage(30) = 75 mana used, 25 left
    _client.Call(r => r.UseAbility(gameId, staff.PrimaryAbilityId, null, null, null));
    _client.Call(r => r.UseAbility(gameId, staff.SecondaryAbilityId, null, null, null));
    var barrage = _client.Db.AbilityDef.Iter().First(a => a.Name == "Arcane Barrage");
    _client.Call(r => r.UseAbility(gameId, barrage.Id, null, null, null));

    // Fire Empowerment costs 30 mana but only 25 left -- should fail
    var fireEmp = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fire Empowerment");
    _client.CallExpectFailure(r => r.UseAbility(gameId, fireEmp.Id, null, null, null));
  }

  [Fact]
  public void ResourceCosts_AbilityDef_HasCorrectCosts()
  {
    var shootBullet = _client.Db.AbilityDef.Iter().First(a => a.Name == "Shoot Bullet");
    Assert.Equal(2, shootBullet.ResourceCosts.Count);
    Assert.Contains(shootBullet.ResourceCosts, c => c.Kind == ResourceKind.Ammo && c.Amount == 1);
    Assert.Contains(shootBullet.ResourceCosts, c => c.Kind == ResourceKind.Stamina && c.Amount == 5);

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

    // Set up both loadouts before combat
    var rifle = attacker.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var gSkill = attacker.Db.SkillDef.Iter().First();
    attacker.Call(r => r.SetLoadout(gameId, rifle.Id, gSkill.Id));

    var staff = _client.Db.WeaponDef.Iter().First(w => w.Name == "Arcane Staff");
    var combatMedic = _client.Db.SkillDef.Iter().First(s => s.Name == "Combat Medic");
    _client.Call(r => r.SetLoadout(gameId, staff.Id, combatMedic.Id));

    // Attacker damages medic
    var medicGp = _client.GetGamePlayer(gameId);
    attacker.Call(r => r.UseAbility(gameId, rifle.PrimaryAbilityId, medicGp.Id, null, null));

    // Medic heals themselves -- this Call also pumps the client cache to pick up the damage
    var healingMist = _client.Db.AbilityDef.Iter().First(a => a.Name == "Healing Mist");
    _client.Call(r => r.UseAbility(gameId, healingMist.Id, null, null, null));

    // After 20 damage + 40 heal, health should be 100 (capped at MaxHealth)
    // But the key assertion: health went down then back up, net result is capped at max.
    var medicHealed = _client.Db.GamePlayer.Id.Find(medicGp.Id)!;
    Assert.Equal(100, medicHealed.Health);

    attacker.ClearData();
  }

  [Fact]
  public void UseAbility_BuffAbility_NoDamageApplied()
  {
    _client.CreatePlayerAndGetId("BuffNoDmg");
    var gameId = _client.CreateGameAndJoin(4);

    var staff = _client.Db.WeaponDef.Iter().First(w => w.Name == "Arcane Staff");
    var battleMage = _client.Db.SkillDef.Iter().First(s => s.Name == "Battle Mage");
    _client.Call(r => r.SetLoadout(gameId, staff.Id, battleMage.Id));

    var gp = _client.GetGamePlayer(gameId);
    var fireEmpowerment = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fire Empowerment");

    _client.Call(r => r.UseAbility(gameId, fireEmpowerment.Id, null, null, null));

    var gpAfter = _client.Db.GamePlayer.Id.Find(gp.Id)!;
    Assert.Equal(100, gpAfter.Health);
  }

  [Fact]
  public void UseAbility_DamageRespectsArmor()
  {
    // This test verifies the armor formula works. With default armor=0,
    // damage should be applied at full. We verify damage equals resolved power.
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("ArmorTarget");

    _client.CreatePlayerAndGetId("ArmorTester");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    var rifle = _client.Db.WeaponDef.Iter().First(w => w.Name == "Standard Rifle");
    var skill = _client.Db.SkillDef.Iter().First();
    _client.Call(r => r.SetLoadout(gameId, rifle.Id, skill.Id));

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "ArmorTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    _client.Call(r => r.UseAbility(gameId, rifle.PrimaryAbilityId, targetGp.Id, null, null));

    var log = _client.Db.BattleLog.GameSessionId.Filter(gameId).First();
    var targetAfter = _client.Db.GamePlayer.Id.Find(targetGp.Id)!;

    // With 0 armor, damage = resolved power, so health = 100 - resolvedPower
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

  private (ulong gameId, ulong gpId) SetupBattleMage(string name = "TerrainCaster")
  {
    _client.CreatePlayerAndGetId(name);
    var gameId = _client.CreateGameAndJoin(4);
    var staff = _client.Db.WeaponDef.Iter().First(w => w.Name == "Arcane Staff");
    var battleMage = _client.Db.SkillDef.Iter().First(s => s.Name == "Battle Mage");
    _client.Call(r => r.SetLoadout(gameId, staff.Id, battleMage.Id));
    var gp = _client.GetGamePlayer(gameId);
    return (gameId, gp.Id);
  }

  [Fact]
  public void UseAbility_TerrainAbility_CreatesTerrainFeature()
  {
    var (gameId, gpId) = SetupBattleMage();
    var wardOfEarth = _client.Db.AbilityDef.Iter().First(a => a.Name == "Ward of Earth");

    var terrainBefore = _client.Db.TerrainFeature.GameSessionId.Filter(gameId)
      .Count(t => t.CasterGamePlayerId != null);

    var targetPos = new SpacetimeDB.Types.DbVector3(5f, 0f, 5f);
    _client.Call(r => r.UseAbility(gameId, wardOfEarth.Id, null, targetPos, null));

    var playerTerrain = _client.Db.TerrainFeature.GameSessionId.Filter(gameId)
      .Where(t => t.CasterGamePlayerId != null).ToList();
    Assert.Equal(terrainBefore + 1, playerTerrain.Count);

    var wall = playerTerrain.Last();
    Assert.Equal(TerrainType.Wall, wall.Type);
    Assert.Equal(gpId, wall.CasterGamePlayerId);
    Assert.Equal(10f, wall.SizeX);
    Assert.Equal(3f, wall.SizeY);
    Assert.Equal(1f, wall.SizeZ);
  }

  [Fact]
  public void UseAbility_TerrainAbility_RequiresPosition()
  {
    var (gameId, _) = SetupBattleMage("TerrainNoPos");
    var wardOfEarth = _client.Db.AbilityDef.Iter().First(a => a.Name == "Ward of Earth");

    _client.CallExpectFailure(r => r.UseAbility(gameId, wardOfEarth.Id, null, null, null));
  }

  [Fact]
  public void UseAbility_TerrainAbility_OutOfRange_Fails()
  {
    var (gameId, _) = SetupBattleMage("TerrainRange");
    var wardOfEarth = _client.Db.AbilityDef.Iter().First(a => a.Name == "Ward of Earth");

    // Ward of Earth has BaseRange=20, place at distance 100 (way out of range)
    var farPos = new SpacetimeDB.Types.DbVector3(100f, 0f, 100f);
    _client.CallExpectFailure(r => r.UseAbility(gameId, wardOfEarth.Id, null, farPos, null));
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

    // Move both players to known positions with clear line of sight
    _client.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -50f), 0f));
    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "LoSTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);
    target.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -40f), 0f));

    // Setting target should succeed (clear LoS)
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

    // Position players far apart along Z axis at Y=3
    _client.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -20f), 0f));
    target.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, 20f), 0f));

    // Place a tall wall directly between them using Ward of Earth
    var staff = _client.Db.WeaponDef.Iter().First(w => w.Name == "Arcane Staff");
    var battleMage = _client.Db.SkillDef.Iter().First(s => s.Name == "Battle Mage");
    _client.Call(r => r.SetLoadout(gameId, staff.Id, battleMage.Id));
    var wardOfEarth = _client.Db.AbilityDef.Iter().First(a => a.Name == "Ward of Earth");
    var wallPos = new SpacetimeDB.Types.DbVector3(0f, 0f, 0f);
    _client.Call(r => r.UseAbility(gameId, wardOfEarth.Id, null, wallPos, null));

    // Verify the wall was placed
    var walls = _client.Db.TerrainFeature.GameSessionId.Filter(gameId)
      .Where(t => t.CasterGamePlayerId != null).ToList();
    Assert.NotEmpty(walls);

    // Now try to set target -- should fail because wall blocks LoS
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
    _client.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -20f), 0f));
    target.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, 20f), 0f));

    // Set up Battle Mage loadout and place wall
    var staff = _client.Db.WeaponDef.Iter().First(w => w.Name == "Arcane Staff");
    var battleMage = _client.Db.SkillDef.Iter().First(s => s.Name == "Battle Mage");
    _client.Call(r => r.SetLoadout(gameId, staff.Id, battleMage.Id));
    var wardOfEarth = _client.Db.AbilityDef.Iter().First(a => a.Name == "Ward of Earth");
    _client.Call(r => r.UseAbility(gameId, wardOfEarth.Id, null, new SpacetimeDB.Types.DbVector3(0f, 0f, 0f), null));

    // Try targeted ability through wall
    var iceLance = _client.Db.AbilityDef.Iter().First(a => a.Name == "Ice Lance");
    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "AbilityBlockTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    var error = _client.CallExpectFailure(r => r.UseAbility(gameId, iceLance.Id, targetGp.Id, null, null));
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

    // Move players close together with clear LoS
    _client.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, -5f), 0f));
    target.Call(r => r.MovePlayer(gameId, new SpacetimeDB.Types.DbVector3(0f, 3f, 5f), 0f));

    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "ClearTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    _client.Call(r => r.SetTarget(gameId, targetGp.Id));
    var shooter = _client.GetGamePlayer(gameId);
    Assert.Equal(targetGp.Id, shooter.TargetGamePlayerId);

    // Clear target (null) should always succeed
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

    // Players at Y=3 (standing height), trenches are at PosY=-1 with SizeY=2 (top at Y=1)
    // So trenches should NOT block LoS at player height
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
