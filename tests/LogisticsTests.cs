using System;
using System.Linq;
using SpacetimeDB.Types;
using Xunit;

public class LogisticsDefinitionTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public LogisticsDefinitionTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void EngineerArchetype_Exists()
  {
    var engineer = _client.Db.ArchetypeDef.Iter().FirstOrDefault(a => a.Name == "Engineer");
    Assert.NotNull(engineer);
    Assert.Equal(ArchetypeKind.Engineer, engineer.Kind);
  }

  [Fact]
  public void EngineerArchetype_HasBuildRoadAndUpgrade()
  {
    var engineer = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Engineer");
    var abilityNames = engineer.InnateAbilityIds
      .Select(id => _client.Db.AbilityDef.Id.Find(id)?.Name)
      .ToList();
    Assert.Contains("Build Road", abilityNames);
    Assert.Contains("Upgrade Feature", abilityNames);
  }

  [Fact]
  public void BuildRoad_AbilityDef_IsTerrainRoad()
  {
    var buildRoad = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Build Road");
    Assert.NotNull(buildRoad);
    Assert.Equal(AbilityType.Terrain, buildRoad.Type);
    Assert.Equal(TerrainType.Road, buildRoad.SpawnedTerrainType);
    Assert.Equal(TargetingMode.GroundTarget, buildRoad.Targeting);
  }

  [Fact]
  public void UpgradeFeature_AbilityDef_IsUtility()
  {
    var upgrade = _client.Db.AbilityDef.Iter().FirstOrDefault(a => a.Name == "Upgrade Feature");
    Assert.NotNull(upgrade);
    Assert.Equal(AbilityType.Utility, upgrade.Type);
    Assert.Equal(TargetingMode.UpgradeTarget, upgrade.Targeting);
  }

  [Fact]
  public void EngineerSkillsets_Exist()
  {
    var engineer = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Engineer");
    var skills = _client.Db.SkillDef.Iter().Where(s => s.ArchetypeDefId == engineer.Id).ToList();
    Assert.True(skills.Count >= 2, $"Expected at least 2 engineer skillsets, got {skills.Count}");
  }
}

public class BaseResourceStoreTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public BaseResourceStoreTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void MapGeneration_CreatesBaseStoresForCommandCenters()
  {
    _client.CreatePlayerAndGetId("StoreTest");
    var gameId = _client.CreateGameAndJoin(4);

    var stores = _client.Db.BaseResourceStore.GameSessionId.Filter(gameId).ToList();
    var homeStores = stores.Where(s => s.GenerationPerSecond > 0).ToList();
    Assert.True(homeStores.Count >= 2, $"Expected 2 home base stores (team 1 + team 2), got {homeStores.Count}");

    foreach (var store in homeStores)
    {
      Assert.Equal(500, store.SuppliesMax);
      Assert.True(store.GenerationPerSecond > 0);
      Assert.Equal((byte)1, store.Level);
    }
  }

  [Fact]
  public void MapGeneration_CreatesBaseStoresForOutposts()
  {
    _client.CreatePlayerAndGetId("OutpostStoreTest");
    var gameId = _client.CreateGameAndJoin(4);

    var stores = _client.Db.BaseResourceStore.GameSessionId.Filter(gameId).ToList();
    var fobStores = stores.Where(s => s.GenerationPerSecond == 0).ToList();
    Assert.True(fobStores.Count >= 4, $"Expected at least 4 FOB stores, got {fobStores.Count}");

    foreach (var store in fobStores)
    {
      Assert.Equal(100, store.SuppliesMax);
      Assert.Equal(50, store.Supplies);
      Assert.Equal((byte)1, store.Level);
    }
  }
}

public class RoadPlacementTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public RoadPlacementTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  private (ulong gameId, ulong gpEntityId, ArchetypeDef archetype) SetupEngineer()
  {
    _client.CreatePlayerAndGetId("EngineerPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var engineer = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Engineer");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Pistol");
    var skill = _client.Db.SkillDef.Iter().First(s => s.ArchetypeDefId == engineer.Id);
    _client.Call(r => r.SetLoadout(gameId, engineer.Id, weapon.Id, skill.Id));

    _client.Call(r => r.SetTeam(gameId, 1));
    var gp = _client.GetGamePlayer(gameId);

    var ccEntity = _client.Db.Entity.GameSessionId.Filter(gameId)
      .Where(e => e.Type == EntityType.Terrain && e.TeamSlot == 1)
      .First(e =>
      {
        var tf = _client.Db.TerrainFeature.EntityId.Find(e.EntityId);
        return tf != null && tf.Type == TerrainType.CommandCenter;
      });
    _client.Call(r => r.TeleportPlayer(gameId, "EngineerPlayer", new DbVector3(ccEntity.Position.X, 0f, ccEntity.Position.Z)));

    _client.Call(r => r.StartGame(gameId));

    return (gameId, gp.EntityId, engineer);
  }

  [Fact]
  public void BuildRoad_NearCommandCenter_Succeeds()
  {
    var (gameId, gpEntityId, engineer) = SetupEngineer();
    var buildRoad = _client.Db.AbilityDef.Iter().First(a => a.Name == "Build Road");

    var ccEntity = _client.Db.Entity.GameSessionId.Filter(gameId)
      .Where(e => e.Type == EntityType.Terrain && e.TeamSlot == 1)
      .FirstOrDefault(e =>
      {
        var tf = _client.Db.TerrainFeature.EntityId.Find(e.EntityId);
        return tf != null && tf.Type == TerrainType.CommandCenter;
      });
    Assert.NotNull(ccEntity);

    var roadPos = new DbVector3(ccEntity.Position.X + 10f, 0f, ccEntity.Position.Z);
    _client.Call(r => r.UseAbility(gameId, buildRoad.Id, null, roadPos, null, null));

    var roads = _client.Db.RoadSegment.GameSessionId.Filter(gameId).ToList();
    Assert.Single(roads);
    Assert.Equal((byte)1, roads[0].Level);
    Assert.Equal((byte)1, roads[0].TeamSlot);
  }

  [Fact]
  public void BuildRoad_NoConnection_Fails()
  {
    var (gameId, gpEntityId, engineer) = SetupEngineer();
    var buildRoad = _client.Db.AbilityDef.Iter().First(a => a.Name == "Build Road");

    var farPos = new DbVector3(200f, 0f, 200f);
    _client.CallExpectFailure(r => r.UseAbility(gameId, buildRoad.Id, null, farPos, null, null));
  }

  [Fact]
  public void BuildRoad_ChainedRoads_Succeeds()
  {
    var (gameId, gpEntityId, engineer) = SetupEngineer();
    var buildRoad = _client.Db.AbilityDef.Iter().First(a => a.Name == "Build Road");

    var ccEntity = _client.Db.Entity.GameSessionId.Filter(gameId)
      .Where(e => e.Type == EntityType.Terrain && e.TeamSlot == 1)
      .First(e =>
      {
        var tf = _client.Db.TerrainFeature.EntityId.Find(e.EntityId);
        return tf != null && tf.Type == TerrainType.CommandCenter;
      });

    var road1Pos = new DbVector3(ccEntity.Position.X + 10f, 0f, ccEntity.Position.Z);
    _client.Call(r => r.UseAbility(gameId, buildRoad.Id, null, road1Pos, null, null));

    System.Threading.Thread.Sleep(3200);
    var road2Pos = new DbVector3(ccEntity.Position.X + 14f, 0f, ccEntity.Position.Z);
    _client.Call(r => r.UseAbility(gameId, buildRoad.Id, null, road2Pos, null, null));

    var roads = _client.Db.RoadSegment.GameSessionId.Filter(gameId).ToList();
    Assert.Equal(2, roads.Count);
  }
}

public class ResupplyTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public ResupplyTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  private (ulong gameId, ulong gpEntityId) SetupNearBase()
  {
    _client.CreatePlayerAndGetId("ResupplyPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.ArchetypeDefId == archetype.Id);
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));
    _client.Call(r => r.SetTeam(gameId, 1));

    var gp = _client.GetGamePlayer(gameId);
    var gpEnt = _client.GetEntity(gp.EntityId);

    var ccEntity = _client.Db.Entity.GameSessionId.Filter(gameId)
      .Where(e => e.Type == EntityType.Terrain && e.TeamSlot == 1)
      .FirstOrDefault(e =>
      {
        var tf = _client.Db.TerrainFeature.EntityId.Find(e.EntityId);
        return tf != null && tf.Type == TerrainType.CommandCenter;
      });

    if (ccEntity != null)
    {
      var nearPos = new DbVector3(ccEntity.Position.X + 2f, ccEntity.Position.Y, ccEntity.Position.Z);
      _client.Call(r => r.TeleportPlayer(gameId, "ResupplyPlayer", nearPos));
    }

    _client.Call(r => r.StartGame(gameId));

    return (gameId, gp.EntityId);
  }

  [Fact]
  public void StartResupply_NearBase_Succeeds()
  {
    var (gameId, gpEntityId) = SetupNearBase();
    _client.Call(r => r.StartResupply(gameId));

    var session = _client.Db.ResupplySession.EntityId.Find(gpEntityId);
    Assert.NotNull(session);
  }

  [Fact]
  public void StopResupply_Succeeds()
  {
    var (gameId, gpEntityId) = SetupNearBase();
    _client.Call(r => r.StartResupply(gameId));
    _client.Call(r => r.StopResupply(gameId));

    var session = _client.Db.ResupplySession.EntityId.Find(gpEntityId);
    Assert.Null(session);
  }

  [Fact]
  public void StartResupply_AlreadyResupplying_Fails()
  {
    var (gameId, _) = SetupNearBase();
    _client.Call(r => r.StartResupply(gameId));
    _client.CallExpectFailure(r => r.StartResupply(gameId));
  }

  [Fact]
  public void StartResupply_FarFromBase_Fails()
  {
    _client.CreatePlayerAndGetId("FarPlayer");
    var gameId = _client.CreateGameAndJoin(4);
    var archetype = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Infantry");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Rifle");
    var skill = _client.Db.SkillDef.Iter().First(s => s.ArchetypeDefId == archetype.Id);
    _client.Call(r => r.SetLoadout(gameId, archetype.Id, weapon.Id, skill.Id));
    _client.Call(r => r.SetTeam(gameId, 1));

    _client.Call(r => r.TeleportPlayer(gameId, "FarPlayer", new DbVector3(0f, 3f, 0f)));
    _client.Call(r => r.StartGame(gameId));

    _client.CallExpectFailure(r => r.StartResupply(gameId));
  }
}

public class UpgradeTests : IDisposable
{
  private readonly SpacetimeTestClient _client;

  public UpgradeTests()
  {
    _client = SpacetimeTestClient.Create();
  }

  public void Dispose()
  {
    _client.ClearData();
    _client.Dispose();
  }

  [Fact]
  public void UpgradeFeature_Road_IncreasesLevel()
  {
    _client.CreatePlayerAndGetId("UpgradeEngineer");
    var gameId = _client.CreateGameAndJoin(4);
    var engineer = _client.Db.ArchetypeDef.Iter().First(a => a.Name == "Engineer");
    var weapon = _client.Db.WeaponDef.Iter().First(w => w.Name == "Pistol");
    var skill = _client.Db.SkillDef.Iter().First(s => s.ArchetypeDefId == engineer.Id);
    _client.Call(r => r.SetLoadout(gameId, engineer.Id, weapon.Id, skill.Id));
    _client.Call(r => r.SetTeam(gameId, 1));

    var ccEntity = _client.Db.Entity.GameSessionId.Filter(gameId)
      .Where(e => e.Type == EntityType.Terrain && e.TeamSlot == 1)
      .First(e =>
      {
        var tf = _client.Db.TerrainFeature.EntityId.Find(e.EntityId);
        return tf != null && tf.Type == TerrainType.CommandCenter;
      });
    _client.Call(r => r.TeleportPlayer(gameId, "UpgradeEngineer", new DbVector3(ccEntity.Position.X, 0f, ccEntity.Position.Z)));

    _client.Call(r => r.StartGame(gameId));

    var buildRoad = _client.Db.AbilityDef.Iter().First(a => a.Name == "Build Road");
    var upgradeAbility = _client.Db.AbilityDef.Iter().First(a => a.Name == "Upgrade Feature");

    var roadPos = new DbVector3(ccEntity.Position.X + 10f, 0f, ccEntity.Position.Z);
    _client.Call(r => r.UseAbility(gameId, buildRoad.Id, null, roadPos, null, null));

    var road = _client.Db.RoadSegment.GameSessionId.Filter(gameId).First();
    Assert.Equal((byte)1, road.Level);

    _client.Call(r => r.UseAbility(gameId, upgradeAbility.Id, null, roadPos, null, null));

    road = _client.Db.RoadSegment.GameSessionId.Filter(gameId).First();
    Assert.Equal((byte)2, road.Level);
  }
}
