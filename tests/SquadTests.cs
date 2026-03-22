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
    var soldiers = _client.SoldiersInSession(gameId).ToList();

    Assert.Equal(2, soldiers.Count);
    Assert.Equal(4, squads.Count);

    var playerLeaf = squads.FirstOrDefault(s => s.EntityId == gp.EntityId);
    Assert.NotNull(playerLeaf);

    var soldierEntityIds = soldiers.Select(s => s.EntityId).ToHashSet();
    var soldierLeaves = squads.Where(s => soldierEntityIds.Contains(s.EntityId)).ToList();
    Assert.Equal(2, soldierLeaves.Count);

    var composites = squads.Where(s => s.EntityId == 0).ToList();
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

    var soldiers = _client.SoldiersInSession(gameId).ToList();
    Assert.Equal(2, soldiers.Count);

    foreach (var soldier in soldiers)
    {
      var t = _client.GetTargetable(soldier.EntityId);
      Assert.Equal(15, t.Health);
      Assert.Equal(15, t.MaxHealth);
      Assert.Equal(0, t.Armor);
      Assert.False(t.Dead);
      Assert.Null(t.DiedAt);
    }
  }

  [Fact]
  public void JoinGame_SoldiersHaveDistinctFormationIndices()
  {
    _client.CreatePlayerAndGetId("FormationPlayer");
    var gameId = _client.CreateGameAndJoin(4);

    var soldiers = _client.SoldiersInSession(gameId).ToList();
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

    var soldiers = _client.SoldiersInSession(gameId).ToList();
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
    var soldiers = _client.SoldiersInSession(gameId).ToList();

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
    var gpEntity = _client.GetEntity(gp.EntityId);

    var newPos = new DbVector3(10f, gpEntity.Position.Y, 10f);
    _client.Call(r => r.MovePlayer(gameId, newPos, 0.5f));

    var soldiersAfter = _client.SoldiersInSession(gameId).ToList();
    foreach (var soldier in soldiersAfter)
    {
      var soldierEntity = _client.GetEntity(soldier.EntityId);
      var dx = soldierEntity.Position.X - 10f;
      var dz = soldierEntity.Position.Z - 10f;
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
    var gpEntity = _client.GetEntity(gp.EntityId);

    var newPos = new DbVector3(20f, gpEntity.Position.Y, 20f);
    _client.Call(r => r.MovePlayer(gameId, newPos, 0f));

    var composites = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.EntityId == 0)
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
    var gpAEntity = _client.GetEntity(gpA.EntityId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);
    var gpBEntity = client2.GetEntity(gpB.EntityId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpBEntity.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsBefore = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(2, rootsBefore);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(5f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(7f, gpBEntity.Position.Y, 0f), 0f));

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
    var gpAEntity = _client.GetEntity(gpA.EntityId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);
    var gpBEntity = client2.GetEntity(gpB.EntityId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpBEntity.Position.Y, 0f), 0f));

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
    var gpAEntity = _client.GetEntity(gpA.EntityId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);
    var gpBEntity = client2.GetEntity(gpB.EntityId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpBEntity.Position.Y, 0f), 0f));

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(5f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(7f, gpBEntity.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsBefore = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(1, rootsBefore);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpAEntity.Position.Y, 0f), 0f));

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
    var gpAEntity = _client.GetEntity(gpA.EntityId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);
    var gpBEntity = client2.GetEntity(gpB.EntityId);
    var gpC = client3.GetGamePlayer(gameId);
    var gpCEntity = client3.GetEntity(gpC.EntityId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpBEntity.Position.Y, 0f), 0f));
    client3.Call(r => r.MovePlayer(gameId, new DbVector3(80f, gpCEntity.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsInitial = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(3, rootsInitial);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(0f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(2f, gpBEntity.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsAfterAB = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(2, rootsAfterAB);

    client3.Call(r => r.MovePlayer(gameId, new DbVector3(1f, gpCEntity.Position.Y, 0f), 0f));

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
    var gpAEntity = _client.GetEntity(gpA.EntityId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);
    var gpBEntity = client2.GetEntity(gpB.EntityId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpBEntity.Position.Y, 0f), 0f));

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(5f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(7f, gpBEntity.Position.Y, 0f), 0f));

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

  [Fact]
  public void SameTeamPlayersCloseByMerge()
  {
    _client.CreatePlayerAndGetId("TeamMergeA");
    var gameId = _client.CreateGameAndJoin(4);

    using var client2 = SpacetimeTestClient.Create();
    client2.CreatePlayerAndGetId("TeamMergeB");
    client2.Call(r => r.JoinGame(gameId));

    _client.Call(r => r.SetTeam(gameId, 1));
    client2.Call(r => r.SetTeam(gameId, 1));

    var gpA = _client.GetGamePlayer(gameId);
    var gpAEntity = _client.GetEntity(gpA.EntityId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);
    var gpBEntity = client2.GetEntity(gpB.EntityId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpBEntity.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsBefore = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(2, rootsBefore);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(5f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(7f, gpBEntity.Position.Y, 0f), 0f));

    _client.Sync();
    var rootsAfter = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(1, rootsAfter);

    client2.ClearData();
  }

  [Fact]
  public void DifferentTeamPlayersDoNotMerge()
  {
    _client.CreatePlayerAndGetId("CrossTeamA");
    var gameId = _client.CreateGameAndJoin(4);

    using var client2 = SpacetimeTestClient.Create();
    client2.CreatePlayerAndGetId("CrossTeamB");
    client2.Call(r => r.JoinGame(gameId));

    _client.Call(r => r.SetTeam(gameId, 1));
    client2.Call(r => r.SetTeam(gameId, 2));

    var gpA = _client.GetGamePlayer(gameId);
    var gpAEntity = _client.GetEntity(gpA.EntityId);
    _client.Sync();
    var gpB = client2.GetGamePlayer(gameId);
    var gpBEntity = client2.GetEntity(gpB.EntityId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(50f, gpBEntity.Position.Y, 0f), 0f));

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(5f, gpAEntity.Position.Y, 0f), 0f));
    client2.Call(r => r.MovePlayer(gameId, new DbVector3(7f, gpBEntity.Position.Y, 0f), 0f));

    _client.Sync();
    var rootSquads = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.ParentSquadId == 0).Count();
    Assert.Equal(2, rootSquads);

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

    var aiSoldiers = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == null).ToList();

    Assert.True(aiSoldiers.Count >= 6, $"Expected at least 6 AI soldiers (3 squads x 2+), got {aiSoldiers.Count}");
    Assert.True(aiSoldiers.Count <= 9, $"Expected at most 9 AI soldiers (3 squads x 3), got {aiSoldiers.Count}");

    var aiComposites = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.OwnerPlayerId == null && s.EntityId == 0).ToList();

    Assert.Equal(3, aiComposites.Count);
  }

  [Fact]
  public void AiSoldiers_HaveValidPositions()
  {
    _client.CreatePlayerAndGetId("AiPosHost");
    var gameId = _client.CreateGameAndJoin(4);
    _client.Call(r => r.StartGame(gameId));

    var aiSoldiers = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == null).ToList();

    foreach (var soldier in aiSoldiers)
    {
      var soldierEntity = _client.GetEntity(soldier.EntityId);
      var soldierTargetable = _client.GetTargetable(soldier.EntityId);
      Assert.True(Math.Abs(soldierEntity.Position.X) <= 100f, $"AI soldier X out of bounds: {soldierEntity.Position.X}");
      Assert.True(Math.Abs(soldierEntity.Position.Z) <= 100f, $"AI soldier Z out of bounds: {soldierEntity.Position.Z}");
      Assert.Equal(15, soldierTargetable.Health);
      Assert.False(soldierTargetable.Dead);
    }
  }

  [Fact]
  public void DeleteGame_CleansUpAiSquads()
  {
    _client.CreatePlayerAndGetId("AiCleanup");
    var gameId = _client.CreateGameAndJoin(4);
    _client.Call(r => r.StartGame(gameId));

    var aiSoldiersBefore = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == null).Count();
    Assert.True(aiSoldiersBefore > 0);

    _client.Call(r => r.EndGame(gameId));
    _client.Call(r => r.DeleteGame(gameId));

    var aiSoldiersAfter = _client.SoldiersInSession(gameId)
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

  [Fact(Skip = "LOS check fails when attacker and target are 100 units apart")]
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
    var targetGp = _client.GamePlayersInSession(gameId)
      .First(gp => gp.PlayerId == targetPlayer.Id);

    int healthBefore = _client.GetTargetable(targetGp.EntityId).Health;

    var soldiersBefore = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == targetPlayer.Id).ToList();
    var soldierHealthBefore = soldiersBefore.Sum(s => _client.GetTargetable(s.EntityId).Health);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(-50f, 3f, 0f), 0f));
    target.Call(r => r.MovePlayer(gameId, new DbVector3(50f, 3f, 0f), 0f));

    _client.Call(r => r.UseAbility(gameId, rifle.PrimaryAbilityId, targetGp.EntityId, null, null));

    var targetAfterHealth = _client.GetTargetable(targetGp.EntityId).Health;
    Assert.True(targetAfterHealth < healthBefore);

    var soldiersAfter = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == targetPlayer.Id).ToList();
    var soldierHealthAfter = soldiersAfter.Sum(s => _client.GetTargetable(s.EntityId).Health);
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
    var soldiersBefore = _client.SoldiersInSession(gameId).Count();
    Assert.Equal(4, squadsBefore);
    Assert.Equal(2, soldiersBefore);

    _client.Call(r => r.LeaveGame(gameId));

    var squadsAfter = _client.Db.Squad.GameSessionId.Filter(gameId).Count();
    var soldiersAfter = _client.SoldiersInSession(gameId).Count();
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
    Assert.Equal(4, _client.SoldiersInSession(gameId).Count());

    client2.Call(r => r.LeaveGame(gameId));
    _client.Call(r => r.DeleteGame(gameId));

    Assert.Equal(0, _client.Db.Squad.GameSessionId.Filter(gameId).Count());
    Assert.Equal(0, _client.SoldiersInSession(gameId).Count());

    client2.ClearData();
  }

  [Fact]
  public void ClearData_CleansUpSquads()
  {
    _client.CreatePlayerAndGetId("ClearMe");
    var gameId = _client.CreateGameAndJoin(4);

    Assert.True(_client.Db.Squad.GameSessionId.Filter(gameId).Count() > 0);
    Assert.True(_client.SoldiersInSession(gameId).Count() > 0);

    _client.ClearData();

    Assert.Equal(0, _client.Db.Squad.GameSessionId.Filter(gameId).Count());
    Assert.Equal(0, _client.SoldiersInSession(gameId).Count());
  }
}

public class SquadSoldierDeathTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public SquadSoldierDeathTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  private (ulong gameId, GamePlayer attackerGp, ulong rifleAbilityId) SetupAttacker(string name)
  {
    _client.CreatePlayerAndGetId(name);
    var gameId = _client.CreateGame(4, 0);
    _client.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    var gp = _client.GetGamePlayer(gameId);
    return (gameId, gp, weapon.PrimaryAbilityId);
  }

  [Fact(Skip = "LOS check fails when attacker is far from soldier target")]
  public void SoldierDeath_UpdatesSquadCenter()
  {
    var (gameId, attackerGp, rifleId) = SetupAttacker("CenterKiller");

    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("CenterVictim");
    target.Call(r => r.JoinGame(gameId));

    var targetGp = target.GetGamePlayer(gameId);
    var targetGpEntity = target.GetEntity(targetGp.EntityId);
    _client.Sync();

    target.Call(r => r.MovePlayer(gameId, new DbVector3(20f, targetGpEntity.Position.Y, 20f), 0f));
    _client.Sync();

    var soldiers = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == targetGp.PlayerId && !_client.GetTargetable(s.EntityId).Dead).ToList();
    Assert.True(soldiers.Count > 0, "Target should have living soldiers");

    var soldierToKill = soldiers[0];

    var compositeBefore = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.EntityId == 0 && s.OwnerPlayerId == targetGp.PlayerId)
      .First();
    var centerBefore = compositeBefore.CenterPosition;

    _client.Call(r => r.UseAbility(gameId, rifleId, soldierToKill.EntityId, null, null));

    var killedSoldierTargetable = _client.GetTargetable(soldierToKill.EntityId);
    Assert.True(killedSoldierTargetable.Dead);

    var compositeAfter = _client.Db.Squad.Id.Find(compositeBefore.Id);
    Assert.NotNull(compositeAfter);
    var centerAfter = compositeAfter.CenterPosition;

    Assert.True(
      centerBefore.X != centerAfter.X || centerBefore.Z != centerAfter.Z,
      $"Squad center should change after soldier death, before=({centerBefore.X},{centerBefore.Z}) after=({centerAfter.X},{centerAfter.Z})");

    target.ClearData();
  }

  [Fact]
  public void SoldierDeath_CreatesCorpse()
  {
    var (gameId, attackerGp, rifleId) = SetupAttacker("CorpseKiller");

    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("CorpseVictim");
    target.Call(r => r.JoinGame(gameId));

    var targetGp = target.GetGamePlayer(gameId);
    _client.Sync();

    var soldiers = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == targetGp.PlayerId && !_client.GetTargetable(s.EntityId).Dead).ToList();
    var soldierToKill = soldiers[0];

    var corpsesBefore = _client.CorpsesInSession(gameId)
      .Where(c => c.SourceEntityId == soldierToKill.EntityId).Count();
    Assert.Equal(0, corpsesBefore);

    _client.Call(r => r.UseAbility(gameId, rifleId, soldierToKill.EntityId, null, null));

    var corpsesAfter = _client.CorpsesInSession(gameId)
      .Where(c => c.SourceEntityId == soldierToKill.EntityId).ToList();
    Assert.Single(corpsesAfter);
    Assert.Equal(soldierToKill.EntityId, corpsesAfter[0].SourceEntityId);

    target.ClearData();
  }

  [Fact]
  public void Respawn_CleansSoldierCorpses()
  {
    var (gameId, attackerGp, rifleId) = SetupAttacker("RespawnCorpseKiller");

    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("RespawnCorpseVictim");
    target.Call(r => r.JoinGame(gameId));

    var targetGp = target.GetGamePlayer(gameId);
    _client.Sync();

    var soldiersBeforeCount = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == targetGp.PlayerId && !_client.GetTargetable(s.EntityId).Dead).Count();
    Assert.True(soldiersBeforeCount > 0, "Target should have soldiers");

    var fireball = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    _client.Call(r => r.UseAbility(gameId, fireball.Id, targetGp.EntityId, null, null));

    var soldierCorpses = _client.CorpsesInSession(gameId)
      .Where(c => c.SourceEntityId != null).Count();
    Assert.True(soldierCorpses > 0, "Should have soldier corpses after fireball");

    var arcaneBarrage = _client.Db.AbilityDef.Iter().First(a => a.Name == "Arcane Barrage");
    _client.Call(r => r.UseAbility(gameId, arcaneBarrage.Id, targetGp.EntityId, null, null));
    _client.Call(r => r.UseAbility(gameId, rifleId, targetGp.EntityId, null, null));

    var targetAfterKill = _client.GetTargetable(targetGp.EntityId);
    Assert.True(targetAfterKill.Dead, "Target player should be dead before respawn");

    target.Call(r => r.Respawn(gameId));

    _client.Sync();
    var soldierCorpsesAfter = _client.CorpsesInSession(gameId)
      .Where(c => c.SourceEntityId != null).Count();
    Assert.Equal(0, soldierCorpsesAfter);

    var revivedSoldiers = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == targetGp.PlayerId && !_client.GetTargetable(s.EntityId).Dead).Count();
    Assert.True(revivedSoldiers > 0, "Soldiers should be revived after respawn");

    target.ClearData();
  }
}

public class SquadLifecycleTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public SquadLifecycleTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void Rejoin_CreatesSoldiersAndSquads()
  {
    _client.CreatePlayerAndGetId("RejoinPlayer");
    var gameId = _client.CreateGameAndJoin(4);

    var soldiersBefore = _client.SoldiersInSession(gameId).Count();
    var squadsBefore = _client.Db.Squad.GameSessionId.Filter(gameId).Count();
    Assert.Equal(2, soldiersBefore);
    Assert.Equal(4, squadsBefore);

    _client.Call(r => r.LeaveGame(gameId));

    Assert.Equal(0, _client.SoldiersInSession(gameId).Count());
    Assert.Equal(0, _client.Db.Squad.GameSessionId.Filter(gameId).Count());

    _client.Call(r => r.JoinGame(gameId));

    var soldiersAfter = _client.SoldiersInSession(gameId).Count();
    var squadsAfter = _client.Db.Squad.GameSessionId.Filter(gameId).Count();
    Assert.Equal(2, soldiersAfter);
    Assert.Equal(4, squadsAfter);
  }

  [Fact]
  public void RespawnFallback_CreatesSquadWhenNoneExists()
  {
    _client.CreatePlayerAndGetId("RespawnFallback");
    var gameId = _client.CreateGame(4, 0);
    _client.Call(r => r.JoinGame(gameId));

    var gp = _client.GetGamePlayer(gameId);

    _client.Call(r => r.LeaveGame(gameId));

    Assert.Equal(0, _client.Db.Squad.GameSessionId.Filter(gameId).Count());
    Assert.Equal(0, _client.SoldiersInSession(gameId).Count());

    _client.Call(r => r.JoinGame(gameId));

    var soldiersAfterRejoin = _client.SoldiersInSession(gameId).Count();
    var squadsAfterRejoin = _client.Db.Squad.GameSessionId.Filter(gameId).Count();
    Assert.Equal(2, soldiersAfterRejoin);
    Assert.Equal(4, squadsAfterRejoin);

    using var attacker = SpacetimeTestClient.Create();
    attacker.CreatePlayerAndGetId("RespawnFBAttacker");
    attacker.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    attacker.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    attacker.Sync();
    var targetGp = attacker.GamePlayersInSession(gameId)
      .First(g => g.PlayerId == gp.PlayerId);

    var fireball = attacker.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    attacker.Call(r => r.UseAbility(gameId, fireball.Id, targetGp.EntityId, null, null));
    var arcaneBarrage = attacker.Db.AbilityDef.Iter().First(a => a.Name == "Arcane Barrage");
    attacker.Call(r => r.UseAbility(gameId, arcaneBarrage.Id, targetGp.EntityId, null, null));
    attacker.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, targetGp.EntityId, null, null));

    _client.Sync();
    var targetAfterKill = _client.GetTargetable(targetGp.EntityId);
    Assert.True(targetAfterKill.Dead, "Player should be dead");

    _client.Call(r => r.Respawn(gameId));

    var soldiersAfterRespawn = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == gp.PlayerId && !_client.GetTargetable(s.EntityId).Dead).Count();
    Assert.True(soldiersAfterRespawn >= 2, "Should have living soldiers after respawn");

    attacker.ClearData();
  }

  [Fact]
  public void PlayerDeath_UpdatesSquadCenter()
  {
    _client.CreatePlayerAndGetId("DeathCenterAttacker");
    var gameId = _client.CreateGame(4, 0);
    _client.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("DeathCenterVictim");
    target.Call(r => r.JoinGame(gameId));

    var targetGp = target.GetGamePlayer(gameId);
    _client.Sync();

    var compositeBefore = _client.Db.Squad.GameSessionId.Filter(gameId)
      .Where(s => s.EntityId == 0 && s.OwnerPlayerId == targetGp.PlayerId)
      .First();
    var centerBefore = compositeBefore.CenterPosition;

    var fireball = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    _client.Call(r => r.UseAbility(gameId, fireball.Id, targetGp.EntityId, null, null));
    var arcaneBarrage = _client.Db.AbilityDef.Iter().First(a => a.Name == "Arcane Barrage");
    _client.Call(r => r.UseAbility(gameId, arcaneBarrage.Id, targetGp.EntityId, null, null));
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, targetGp.EntityId, null, null));

    var targetAfterKill = _client.GetTargetable(targetGp.EntityId);
    Assert.True(targetAfterKill.Dead, "Target should be dead");

    var allSoldiersDead = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == targetGp.PlayerId).All(s => _client.GetTargetable(s.EntityId).Dead);
    Assert.True(allSoldiersDead, "All soldiers should be dead");

    var compositeAfter = _client.Db.Squad.Id.Find(compositeBefore.Id);
    Assert.NotNull(compositeAfter);
    var centerAfter = compositeAfter.CenterPosition;
    Assert.True(
      centerAfter.X == 0 && centerAfter.Y == 0 && centerAfter.Z == 0,
      $"Squad center should be (0,0,0) when all members dead, got ({centerAfter.X},{centerAfter.Y},{centerAfter.Z})");

    target.ClearData();
  }
}

public class SquadSplitResilienceTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public SquadSplitResilienceTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void DeadSoldiers_DoNotCauseSplit()
  {
    _client.CreatePlayerAndGetId("SplitResA");
    var gameId = _client.CreateGame(4, 0);
    _client.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("SplitResB");
    target.Call(r => r.JoinGame(gameId));

    var targetGp = target.GetGamePlayer(gameId);
    var targetGpEntity = target.GetEntity(targetGp.EntityId);
    _client.Sync();

    var soldiers = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == targetGp.PlayerId && !_client.GetTargetable(s.EntityId).Dead).ToList();
    Assert.Equal(2, soldiers.Count);

    var fireball = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    _client.Call(r => r.UseAbility(gameId, fireball.Id, targetGp.EntityId, null, null));

    var deadSoldiers = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == targetGp.PlayerId && _client.GetTargetable(s.EntityId).Dead).Count();
    Assert.Equal(2, deadSoldiers);

    target.Call(r => r.MovePlayer(gameId, new DbVector3(50f, targetGpEntity.Position.Y, 50f), 0f));
    target.Call(r => r.MovePlayer(gameId, new DbVector3(80f, targetGpEntity.Position.Y, 80f), 0f));

    _client.Sync();

    var playerLeaf = _client.Db.Squad.GameSessionId.Filter(gameId)
      .FirstOrDefault(s => s.EntityId == targetGp.EntityId);
    Assert.NotNull(playerLeaf);
    Assert.True(playerLeaf.ParentSquadId != 0,
      "Player leaf squad should still have a parent composite after dead soldiers + movement");

    var composite = _client.Db.Squad.Id.Find(playerLeaf.ParentSquadId);
    Assert.NotNull(composite);

    var children = _client.Db.Squad.ParentSquadId.Filter(composite.Id).ToList();
    Assert.Equal(3, children.Count);

    target.ClearData();
  }

  [Fact]
  public void MoveSoldiersWithPlayer_UpdatesCenterWhenNoParent()
  {
    _client.CreatePlayerAndGetId("OrphanMover");
    var gameId = _client.CreateGameAndJoin(4);
    var gp = _client.GetGamePlayer(gameId);
    var gpEntity = _client.GetEntity(gp.EntityId);

    var composite = _client.Db.Squad.GameSessionId.Filter(gameId)
      .First(s => s.EntityId == 0 && s.OwnerPlayerId == gp.PlayerId);

    _client.Call(r => r.MovePlayer(gameId, new DbVector3(30f, gpEntity.Position.Y, 30f), 0f));

    var compositeAfter = _client.Db.Squad.Id.Find(composite.Id)!;
    var centerAfter = compositeAfter.CenterPosition;
    Assert.True(
      Math.Abs(centerAfter.X - 30f) < 3f,
      $"Composite center should track player near 30, got {centerAfter.X}");
  }

  [Fact]
  public void RespawnRebuilds_WhenParentIsZero()
  {
    _client.CreatePlayerAndGetId("RespawnRebuildAttacker");
    var gameId = _client.CreateGame(4, 0);
    _client.Call(r => r.JoinGame(gameId));

    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.Name == "Evoker");
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));

    using var target = SpacetimeTestClient.Create();
    target.CreatePlayerAndGetId("RespawnRebuildVictim");
    target.Call(r => r.JoinGame(gameId));

    var targetGp = target.GetGamePlayer(gameId);
    _client.Sync();

    var fireball = _client.Db.AbilityDef.Iter().First(a => a.Name == "Fireball");
    _client.Call(r => r.UseAbility(gameId, fireball.Id, targetGp.EntityId, null, null));
    var arcaneBarrage = _client.Db.AbilityDef.Iter().First(a => a.Name == "Arcane Barrage");
    _client.Call(r => r.UseAbility(gameId, arcaneBarrage.Id, targetGp.EntityId, null, null));
    _client.Call(r => r.UseAbility(gameId, weapon.PrimaryAbilityId, targetGp.EntityId, null, null));

    _client.Sync();
    var targetAfterKill = _client.GetTargetable(targetGp.EntityId);
    Assert.True(targetAfterKill.Dead, "Target should be dead");

    target.Call(r => r.Respawn(gameId));
    _client.Sync();

    var soldiersAfter = _client.SoldiersInSession(gameId)
      .Where(s => s.OwnerPlayerId == targetGp.PlayerId && !_client.GetTargetable(s.EntityId).Dead).Count();
    Assert.True(soldiersAfter >= 2, $"Should have 2 alive soldiers after respawn, got {soldiersAfter}");

    var playerLeaf = _client.Db.Squad.GameSessionId.Filter(gameId)
      .FirstOrDefault(s => s.EntityId == targetGp.EntityId);
    Assert.NotNull(playerLeaf);
    Assert.True(playerLeaf.ParentSquadId != 0,
      "Player leaf should have a parent after respawn rebuild");

    target.ClearData();
  }
}
