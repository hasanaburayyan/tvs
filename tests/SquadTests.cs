using System;
using System.Linq;
using Xunit;
using SpacetimeDB.Types;

public class SquadCreationTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public SquadCreationTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void JoinGame_CreatesSquadWithSoldiers()
  {
    _client.CreatePlayerAndGetId("SquadPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var gp = _client.GetGamePlayer(gameId);

    var squads = _client.Db.Squad.GameSessionId.Filter(gameId).ToList();
    var soldiers = _client.Db.Soldier.GameSessionId.Filter(gameId).ToList();

    Assert.Equal(2, soldiers.Count);
    Assert.Equal(4, squads.Count);

    var playerLeaf = squads.FirstOrDefault(s => s.GamePlayerId == gp.Id);
    Assert.NotNull(playerLeaf);
    Assert.Equal(0UL, playerLeaf.SoldierId);

    var soldierLeaves = squads.Where(s => s.SoldierId != 0).ToList();
    Assert.Equal(2, soldierLeaves.Count);

    var composites = squads.Where(s => s.GamePlayerId == 0 && s.SoldierId == 0).ToList();
    Assert.Single(composites);

    var composite = composites[0];
    Assert.Equal(0UL, composite.ParentSquadId);
    Assert.Equal(composite.Id, playerLeaf.ParentSquadId);
    foreach (var sl in soldierLeaves)
      Assert.Equal(composite.Id, sl.ParentSquadId);
  }

  [Fact]
  public void JoinGame_SoldiersHaveCorrectStats()
  {
    _client.CreatePlayerAndGetId("SoldierStats");
    var gameId = _client.CreateGameAndJoin(4);

    var soldiers = _client.Db.Soldier.GameSessionId.Filter(gameId).ToList();
    Assert.Equal(2, soldiers.Count);

    foreach (var soldier in soldiers)
    {
      Assert.Equal(15, soldier.Health);
      Assert.Equal(15, soldier.MaxHealth);
      Assert.Equal(0, soldier.Armor);
      Assert.False(soldier.Dead);
      Assert.Null(soldier.DiedAt);
    }
  }

  [Fact]
  public void JoinGame_SoldiersHaveDistinctFormationIndices()
  {
    _client.CreatePlayerAndGetId("FormationPlayer");
    var gameId = _client.CreateGameAndJoin(4);

    var soldiers = _client.Db.Soldier.GameSessionId.Filter(gameId).ToList();
    var indices = soldiers.Select(s => s.FormationIndex).Distinct().ToList();
    Assert.Equal(2, indices.Count);
  }

  [Fact]
  public void JoinGame_SquadsHaveCorrectOwner()
  {
    var playerId = _client.CreatePlayerAndGetId("OwnerCheck");
    var gameId = _client.CreateGameAndJoin(4);

    var squads = _client.Db.Squad.GameSessionId.Filter(gameId).ToList();
    foreach (var squad in squads)
      Assert.Equal(playerId, squad.OwnerPlayerId);

    var soldiers = _client.Db.Soldier.GameSessionId.Filter(gameId).ToList();
    foreach (var soldier in soldiers)
      Assert.Equal(playerId, soldier.OwnerPlayerId);
  }

  [Fact]
  public void TwoPlayers_CreateIndependentSquads()
  {
    _client.CreatePlayerAndGetId("Player1");
    var gameId = _client.CreateGameAndJoin(4);

    using var client2 = SpacetimeTestClient.Create();
    client2.CreatePlayerAndGetId("Player2");
    client2.Call(r => r.JoinGame(gameId));

    _client.Sync();

    var squads = _client.Db.Squad.GameSessionId.Filter(gameId).ToList();
    var soldiers = _client.Db.Soldier.GameSessionId.Filter(gameId).ToList();

    Assert.Equal(4, soldiers.Count);
    Assert.Equal(8, squads.Count);

    var rootSquads = squads.Where(s => s.ParentSquadId == 0).ToList();
    Assert.Equal(2, rootSquads.Count);

    client2.ClearData();
  }
}

public class SquadMovementTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public SquadMovementTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void MovePlayer_UpdatesSoldierPositions()
  {
    _client.CreatePlayerAndGetId("Mover");
    var gameId = _client.CreateGameAndJoin(4);
    var gp = _client.GetGamePlayer(gameId);

    var newPos = new DbVector3(10f, gp.Position.Y, 10f);
    _client.Call(r => r.MovePlayer(gameId, newPos, 0.5f));

    var soldiersAfter = _client.Db.Soldier.GameSessionId.Filter(gameId).ToList();
    foreach (var soldier in soldiersAfter)
    {
      var dx = soldier.Position.X - 10f;
      var dz = soldier.Position.Z - 10f;
      var dist = Math.Sqrt(dx * dx + dz * dz);
      Assert.True(dist < 5.0, $"Soldier should be near player after move, distance={dist}");
    }
  }

  [Fact]
  public void MovePlayer_UpdatesSquadCenter()
  {
    _client.CreatePlayerAndGetId("CenterCheck");
    var gameId = _client.CreateGameAndJoin(4);
    var gp = _client.GetGamePlayer(gameId);

    var newPos = new DbVector3(20f, gp.Position.Y, 20f);
    _client.Call(r => r.MovePlayer(gameId, newPos, 0f));

    var composites = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.GamePlayerId == 0 && s.SoldierId == 0)
      .ToList();

    Assert.Single(composites);
    var center = composites[0].CenterPosition;
    Assert.True(Math.Abs(center.X - 20f) < 3f, $"Center X should be near 20, got {center.X}");
    Assert.True(Math.Abs(center.Z - 20f) < 3f, $"Center Z should be near 20, got {center.Z}");
  }
}

public class SquadCohesionTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public SquadCohesionTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void TwoPlayersCloseByMerge()
  {
    _client.CreatePlayerAndGetId("MergeA");
    var gameId = _client.CreateGameAndJoin(4);

    using var client2 = SpacetimeTestClient.Create();
    client2.CreatePlayerAndGetId("MergeB");
    client2.Call(r => r.JoinGame(gameId));

    var gpA = _client.GetGamePlayer(gameId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpA.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpB.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsBefore = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(2, rootsBefore);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(5f, gpA.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(7f, gpB.Position.Y, 0f), 0f));

    _client.Sync();
    var rootSquads = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).ToList();

    Assert.Single(rootSquads);

    client2.ClearData();
  }

  [Fact]
  public void TwoPlayersFarApartStaySeparate()
  {
    _client.CreatePlayerAndGetId("FarA");
    var gameId = _client.CreateGameAndJoin(4);

    using var client2 = SpacetimeTestClient.Create();
    client2.CreatePlayerAndGetId("FarB");
    client2.Call(r => r.JoinGame(gameId));

    var gpA = _client.GetGamePlayer(gameId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpA.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpB.Position.Y, 0f), 0f));

    _client.Sync();
    var rootSquads = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).ToList();

    Assert.Equal(2, rootSquads.Count);

    client2.ClearData();
  }

  [Fact]
  public void MergedSquadsSplitWhenMovingApart()
  {
    _client.CreatePlayerAndGetId("SplitA");
    var gameId = _client.CreateGameAndJoin(4);

    using var client2 = SpacetimeTestClient.Create();
    client2.CreatePlayerAndGetId("SplitB");
    client2.Call(r => r.JoinGame(gameId));

    var gpA = _client.GetGamePlayer(gameId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpA.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpB.Position.Y, 0f), 0f));

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(5f, gpA.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(7f, gpB.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsBefore = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(1, rootsBefore);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpA.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsAfter = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(2, rootsAfter);

    client2.ClearData();
  }

  [Fact]
  public void ThreeWayMergeCreatesCorrectTree()
  {
    _client.CreatePlayerAndGetId("ThreeA");
    var gameId = _client.CreateGameAndJoin(4);

    using var client2 = SpacetimeTestClient.Create();
    client2.CreatePlayerAndGetId("ThreeB");
    client2.Call(r => r.JoinGame(gameId));

    using var client3 = SpacetimeTestClient.Create();
    client3.CreatePlayerAndGetId("ThreeC");
    client3.Call(r => r.JoinGame(gameId));

    var gpA = _client.GetGamePlayer(gameId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);
    var gpC = client3.GetGamePlayer(gameId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpA.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpB.Position.Y, 0f), 0f));
    client3.Call(r => r.MovePlayer(gameId, new DbVector3(80f, gpC.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsInitial = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(3, rootsInitial);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(0f, gpA.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(2f, gpB.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsAfterAB = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(2, rootsAfterAB);

    client3.Call(r => r.MovePlayer(gameId, new DbVector3(1f, gpC.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsAfterABC = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(1, rootsAfterABC);

    client2.ClearData();
    client3.ClearData();
  }

  [Fact]
  public void SplitOwnedSquads_DetachesPlayerSquad()
  {
    _client.CreatePlayerAndGetId("CtrlFA");
    var gameId = _client.CreateGameAndJoin(4);

    using var client2 = SpacetimeTestClient.Create();
    client2.CreatePlayerAndGetId("CtrlFB");
    client2.Call(r => r.JoinGame(gameId));

    var gpA = _client.GetGamePlayer(gameId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpA.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpB.Position.Y, 0f), 0f));

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(5f, gpA.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(7f, gpB.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsBefore = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(1, rootsBefore);

    _client.Call(r => r.SplitOwnedSquads(gameId));

    _client.Sync();
    var rootsAfter = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(2, rootsAfter);

    client2.ClearData();
  }
}

public class AiSquadTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public AiSquadTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void StartGame_SpawnsAiSquads()
  {
    _client.CreatePlayerAndGetId("AiHost");
    var gameId = _client.CreateGameAndJoin(4);
    _client.Call(r => r.StartGame(gameId));

    var aiSoldiers = _client.Db.Soldier.GameSessionId.Filter(gameId)
      .Where(s => s.OwnerPlayerId == null).ToList();

    Assert.True(aiSoldiers.Count >= 6, $"Expected at least 6 AI soldiers (3 squads x 2+), got {aiSoldiers.Count}");
    Assert.True(aiSoldiers.Count <= 9, $"Expected at most 9 AI soldiers (3 squads x 3), got {aiSoldiers.Count}");

    var aiComposites = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.OwnerPlayerId == null && s.GamePlayerId == 0 && s.SoldierId == 0).ToList();

    Assert.Equal(3, aiComposites.Count);
  }

  [Fact]
  public void AiSoldiers_HaveValidPositions()
  {
    _client.CreatePlayerAndGetId("AiPosHost");
    var gameId = _client.CreateGameAndJoin(4);
    _client.Call(r => r.StartGame(gameId));

    var aiSoldiers = _client.Db.Soldier.GameSessionId.Filter(gameId)
      .Where(s => s.OwnerPlayerId == null).ToList();

    foreach (var soldier in aiSoldiers)
    {
      Assert.True(Math.Abs(soldier.Position.X) <= 100f, $"AI soldier X out of bounds: {soldier.Position.X}");
      Assert.True(Math.Abs(soldier.Position.Z) <= 100f, $"AI soldier Z out of bounds: {soldier.Position.Z}");
      Assert.Equal(15, soldier.Health);
      Assert.False(soldier.Dead);
    }
  }

  [Fact]
  public void DeleteGame_CleansUpAiSquads()
  {
    _client.CreatePlayerAndGetId("AiCleanup");
    var gameId = _client.CreateGameAndJoin(4);
    _client.Call(r => r.StartGame(gameId));

    var aiSoldiersBefore = _client.Db.Soldier.GameSessionId.Filter(gameId)
      .Where(s => s.OwnerPlayerId == null).Count();
    Assert.True(aiSoldiersBefore > 0);

    _client.Call(r => r.EndGame(gameId));
    _client.Call(r => r.DeleteGame(gameId));

    var aiSoldiersAfter = _client.Db.Soldier.GameSessionId.Filter(gameId)
      .Where(s => s.OwnerPlayerId == null).Count();
    Assert.Equal(0, aiSoldiersAfter);

    var squadsAfter = _client.Db.Squad.GameSessionId.Filter(gameId).Count();
    Assert.Equal(0, squadsAfter);
  }
}

public class SquadCombatDistributionTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public SquadCombatDistributionTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void SingleDistribution_OnlyHitsTarget()
  {
    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("SingleTarget");

    _client.CreatePlayerAndGetId("SingleAttacker");
    var gameId = _client.CreateGameAndJoin(4);
    target.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var rifle = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, rifle.Id, skill.Id));

    _client.Sync();
    var targetPlayer = _client.Db.Player.Iter().First(p => p.Name == "SingleTarget");
    var targetGp = _client.Db.GamePlayer.GameSessionId.Filter(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    int healthBefore = targetGp.Health;

    var soldiersBefore = _client.Db.Soldier.GameSessionId.Filter(gameId)
      .Where(s => s.OwnerPlayerId == targetPlayer.Id).ToList();
    var soldierHealthBefore = soldiersBefore.Sum(s => s.Health);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, 3f, 0f), 0f));
    target.Call(r => r.MovePlayer(gameId, new DbVector3(50f, 3f, 0f), 0f));

    _client.Call(r => r.UseAbility(gameId, rifle.PrimaryAbilityId, targetGp.Id, null, null, null, null));

    var targetAfter = _client.Db.GamePlayer.Id.Find(targetGp.Id)!;
    Assert.True(targetAfter.Health < healthBefore);

    var soldiersAfter = _client.Db.Soldier.GameSessionId.Filter(gameId)
      .Where(s => s.OwnerPlayerId == targetPlayer.Id).ToList();
    var soldierHealthAfter = soldiersAfter.Sum(s => s.Health);
    Assert.Equal(soldierHealthBefore, soldierHealthAfter);

    target.ClearData();
  }

  [Fact]
  public void FireRifle_AllowSubSquadTargeting()
  {
    var fireRifle = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Fire Rifle");
    Assert.NotNull(fireRifle);
    Assert.True(fireRifle.AllowSubSquadTargeting);
    Assert.Equal(DamageDistribution.Single, fireRifle.Distribution);
  }

  [Fact]
  public void Fireball_HasEvenSplitDistribution()
  {
    var fireball = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Fireball");
    Assert.NotNull(fireball);
    Assert.Equal(DamageDistribution.EvenSplit, fireball.Distribution);
  }

  [Fact]
  public void TrenchGun_HasProximityFalloffDistribution()
  {
    var trenchGun = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Fire Trench Gun");
    Assert.NotNull(trenchGun);
    Assert.Equal(DamageDistribution.ProximityFalloff, trenchGun.Distribution);
  }

  [Fact]
  public void DefaultAbilities_HaveSingleDistribution()
  {
    var pistol = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Fire Pistol");
    Assert.NotNull(pistol);
    Assert.Equal(DamageDistribution.Single, pistol.Distribution);
    Assert.False(pistol.AllowSubSquadTargeting);
  }
}

public class SquadCleanupTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public SquadCleanupTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void LeaveGame_CleansUpSquadAndSoldiers()
  {
    _client.CreatePlayerAndGetId("Leaver");
    var gameId = _client.CreateGameAndJoin(4);

    var squadsBefore = _client.Db.Squad.GameSessionId.Filter(gameId).Count();
    var soldiersBefore = _client.Db.Soldier.GameSessionId.Filter(gameId).Count();
    Assert.Equal(4, squadsBefore);
    Assert.Equal(2, soldiersBefore);

    _client.Call(r => r.LeaveGame(gameId));

    var squadsAfter = _client.Db.Squad.GameSessionId.Filter(gameId).Count();
    var soldiersAfter = _client.Db.Soldier.GameSessionId.Filter(gameId).Count();
    Assert.Equal(0, squadsAfter);
    Assert.Equal(0, soldiersAfter);
  }

  [Fact]
  public void DeleteGame_CleansUpAllSquads()
  {
    _client.CreatePlayerAndGetId("Deleter");
    var gameId = _client.CreateGameAndJoin(4);

    using var client2 = SpacetimeTestClient.Create();
    client2.CreatePlayerAndGetId("Player2Del");
    client2.Call(r => r.JoinGame(gameId));

    _client.Sync();
    Assert.Equal(8, _client.Db.Squad.GameSessionId.Filter(gameId).Count());
    Assert.Equal(4, _client.Db.Soldier.GameSessionId.Filter(gameId).Count());

    client2.Call(r => r.LeaveGame(gameId));
    _client.Call(r => r.DeleteGame(gameId));

    Assert.Equal(0, _client.Db.Squad.GameSessionId.Filter(gameId).Count());
    Assert.Equal(0, _client.Db.Soldier.GameSessionId.Filter(gameId).Count());

    client2.ClearData();
  }

  [Fact]
  public void ClearData_CleansUpSquads()
  {
    _client.CreatePlayerAndGetId("ClearMe");
    var gameId = _client.CreateGameAndJoin(4);

    Assert.True(_client.Db.Squad.GameSessionId.Filter(gameId).Count() > 0);
    Assert.True(_client.Db.Soldier.GameSessionId.Filter(gameId).Count() > 0);

    _client.ClearData();

    Assert.Equal(0, _client.Db.Squad.GameSessionId.Filter(gameId).Count());
    Assert.Equal(0, _client.Db.Soldier.GameSessionId.Filter(gameId).Count());
  }
}
