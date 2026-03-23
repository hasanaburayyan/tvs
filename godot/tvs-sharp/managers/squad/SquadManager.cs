using Godot;
using System.Collections.Generic;
using System.Linq;
using SpacetimeDB;
using SpacetimeDB.Types;

public partial class SquadManager : Node
{
  private static readonly PackedScene SoldierScene = GD.Load<PackedScene>("uid://bpvamikdrthr8");

  public ulong GameId { get; set; } = 0;

  [Export]
  public Node SoldierSpawnPath;

  private readonly Dictionary<ulong, Soldier> _soldiers = new();
  private MeshInstance3D _cohesionCircle;
  private bool _handlersRegistered;

  public void LoadSquads()
  {
	var conn = SpacetimeNetworkManager.Instance.Conn;

	UnsubscribeHandlers(conn);

	foreach (var entity in conn.Db.Entity.GameSessionId.Filter(GameId))
	{
	  if (entity.Type != EntityType.Soldier) continue;
	  var soldier = conn.Db.Soldier.EntityId.Find(entity.EntityId);
	  if (soldier == null) continue;
	  var targetable = conn.Db.Targetable.EntityId.Find(entity.EntityId);
	  if (targetable != null && targetable.Dead) continue;
	  SpawnSoldier(entity, soldier);
	}

	conn.Db.Soldier.OnInsert += OnSoldierInsert;
	conn.Db.Soldier.OnDelete += OnSoldierDelete;
	conn.Db.Entity.OnUpdate += OnEntityUpdate;
	conn.Db.Targetable.OnUpdate += OnTargetableUpdate;
	_handlersRegistered = true;
  }

  private void UnsubscribeHandlers(DbConnection conn)
  {
	if (!_handlersRegistered) return;
	conn.Db.Soldier.OnInsert -= OnSoldierInsert;
	conn.Db.Soldier.OnDelete -= OnSoldierDelete;
	conn.Db.Entity.OnUpdate -= OnEntityUpdate;
	conn.Db.Targetable.OnUpdate -= OnTargetableUpdate;
	_handlersRegistered = false;
  }

  private void SpawnSoldier(Entity entity, SpacetimeDB.Types.Soldier soldierData)
  {
	if (_soldiers.ContainsKey(entity.EntityId)) return;

	var soldier = SoldierScene.Instantiate<Soldier>();
	soldier.Name = $"Soldier_{entity.EntityId}";
	soldier.EntityId = entity.EntityId;
	soldier.OwnerPlayerId = soldierData.OwnerPlayerId;
	soldier.Position = new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z);
	soldier.Rotation = new Vector3(0, entity.RotationY, 0);

	var spawnParent = SoldierSpawnPath ?? GetParent();
	spawnParent.AddChild(soldier);
	_soldiers[entity.EntityId] = soldier;
  }

  private void RemoveSoldier(ulong entityId)
  {
	if (_soldiers.TryGetValue(entityId, out var soldier))
	{
	  soldier.QueueFree();
	  _soldiers.Remove(entityId);
	}
  }

  private void OnSoldierInsert(EventContext ctx, SpacetimeDB.Types.Soldier soldierData)
  {
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(soldierData.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;
	SpawnSoldier(entity, soldierData);
  }

  private void OnEntityUpdate(EventContext ctx, Entity oldEntity, Entity newEntity)
  {
	if (newEntity.GameSessionId != GameId) return;
	if (newEntity.Type != EntityType.Soldier) return;

	if (_soldiers.TryGetValue(newEntity.EntityId, out var soldier))
	{
	  soldier.OnStateUpdated(
		new Vector3(newEntity.Position.X, newEntity.Position.Y, newEntity.Position.Z),
		newEntity.RotationY
	  );
	}
  }

  private void OnTargetableUpdate(EventContext ctx, SpacetimeDB.Types.Targetable oldT, SpacetimeDB.Types.Targetable newT)
  {
	var conn = SpacetimeNetworkManager.Instance.Conn;
	var entity = conn.Db.Entity.EntityId.Find(newT.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;
	if (entity.Type != EntityType.Soldier) return;

	if (!oldT.Dead && newT.Dead)
	{
	  if (_soldiers.TryGetValue(newT.EntityId, out var soldier))
	  {
		soldier.PlayDeath();
		var id = newT.EntityId;
		GetTree().CreateTimer(3.0).Timeout += () => RemoveSoldier(id);
	  }
	}
	else if (oldT.Dead && !newT.Dead)
	{
	  if (_soldiers.TryGetValue(newT.EntityId, out var soldier))
	  {
		soldier.Revive(new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z));
	  }
	  else
	  {
		var soldierData = conn.Db.Soldier.EntityId.Find(newT.EntityId);
		if (soldierData != null)
		  SpawnSoldier(entity, soldierData);
	  }
	}
  }

  private void OnSoldierDelete(EventContext ctx, SpacetimeDB.Types.Soldier soldierData)
  {
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(soldierData.EntityId);
	if (entity != null && entity.GameSessionId != GameId) return;
	RemoveSoldier(soldierData.EntityId);
  }

  public override void _Process(double delta)
  {
	UpdateCohesionCircle();
  }

  private void UpdateCohesionCircle()
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null || GameId == 0) return;

	var activePlayerId = SpacetimeNetworkManager.Instance.ActivePlayerId;
	if (activePlayerId == 0) return;

	var gp = conn.Db.GamePlayer.PlayerId.Filter(activePlayerId.Value)
	  .FirstOrDefault(g => g.Active);
	if (gp == null) return;

	SpacetimeDB.Types.Squad playerLeaf = null;
	foreach (var sq in conn.Db.Squad.EntityId.Filter(gp.EntityId))
	{
	  playerLeaf = sq;
	  break;
	}
	if (playerLeaf == null) return;

	var current = playerLeaf;
	int depth = 0;
	while (current.ParentSquadId != 0 && depth < 100)
	{
	  var parent = conn.Db.Squad.Id.Find(current.ParentSquadId);
	  if (parent == null) break;
	  current = parent;
	  depth++;
	}

	if (_cohesionCircle == null)
	{
	  _cohesionCircle = CreateCohesionCircleMesh();
	  var spawnParent = SoldierSpawnPath ?? GetParent();
	  spawnParent.AddChild(_cohesionCircle);
	}

	var center = current.CenterPosition;
	var targetable = conn.Db.Targetable.EntityId.Find(gp.EntityId);
	bool playerDead = targetable?.Dead ?? false;
	bool allDead = center.X == 0 && center.Y == 0 && center.Z == 0;

	if (depth == 0 && !playerDead)
	{
	  var playerEntity = conn.Db.Entity.EntityId.Find(gp.EntityId);
	  if (playerEntity != null)
		center = playerEntity.Position;
	  allDead = false;
	}

	_cohesionCircle.Visible = !allDead;
	if (allDead) return;

	_cohesionCircle.GlobalPosition = new Vector3(center.X, center.Y + 0.1f, center.Z);

	float radius = current.CohesionRadius;
	_cohesionCircle.Scale = new Vector3(radius, 1f, radius);
  }

  private static MeshInstance3D CreateCohesionCircleMesh()
  {
	const int segments = 64;
	const float thickness = 0.04f;
	float innerR = 1.0f - thickness;
	float outerR = 1.0f + thickness;

	var immMesh = new ImmediateMesh();
	var mat = new StandardMaterial3D
	{
	  ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
	  AlbedoColor = new Color(0.2f, 0.8f, 0.2f, 0.6f),
	  Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
	  CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	  NoDepthTest = true,
	};

	immMesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, mat);
	for (int i = 0; i <= segments; i++)
	{
	  float angle = i * Mathf.Tau / segments;
	  float cos = Mathf.Cos(angle);
	  float sin = Mathf.Sin(angle);

	  immMesh.SurfaceSetNormal(Vector3.Up);
	  immMesh.SurfaceAddVertex(new Vector3(cos * innerR, 0, sin * innerR));
	  immMesh.SurfaceSetNormal(Vector3.Up);
	  immMesh.SurfaceAddVertex(new Vector3(cos * outerR, 0, sin * outerR));
	}
	immMesh.SurfaceEnd();

	var instance = new MeshInstance3D
	{
	  Mesh = immMesh,
	  Visible = true,
	  CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
	};
	return instance;
  }

  public void DestroyAll()
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn != null)
	  UnsubscribeHandlers(conn);

	foreach (var kvp in _soldiers)
	  kvp.Value.QueueFree();
	_soldiers.Clear();

	if (_cohesionCircle != null)
	{
	  _cohesionCircle.QueueFree();
	  _cohesionCircle = null;
	}
  }
}
