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

	foreach (var soldier in conn.Db.Soldier.GameSessionId.Filter(GameId))
	{
	  if (!soldier.Dead)
		SpawnSoldier(soldier);
	}

	conn.Db.Soldier.OnInsert += OnSoldierInsert;
	conn.Db.Soldier.OnUpdate += OnSoldierUpdate;
	conn.Db.Soldier.OnDelete += OnSoldierDelete;
	_handlersRegistered = true;
  }

  private void UnsubscribeHandlers(DbConnection conn)
  {
	if (!_handlersRegistered) return;
	conn.Db.Soldier.OnInsert -= OnSoldierInsert;
	conn.Db.Soldier.OnUpdate -= OnSoldierUpdate;
	conn.Db.Soldier.OnDelete -= OnSoldierDelete;
	_handlersRegistered = false;
  }

  private void SpawnSoldier(SpacetimeDB.Types.Soldier soldierData)
  {
	if (_soldiers.ContainsKey(soldierData.Id)) return;

	var soldier = SoldierScene.Instantiate<Soldier>();
	soldier.Name = $"Soldier_{soldierData.Id}";
	soldier.SoldierId = soldierData.Id;
	soldier.OwnerPlayerId = soldierData.OwnerPlayerId;
	soldier.Position = new Vector3(soldierData.Position.X, soldierData.Position.Y, soldierData.Position.Z);
	soldier.Rotation = new Vector3(0, soldierData.RotationY, 0);

	var spawnParent = SoldierSpawnPath ?? GetParent();
	spawnParent.AddChild(soldier);
	_soldiers[soldierData.Id] = soldier;
  }

  private void RemoveSoldier(ulong soldierId)
  {
	if (_soldiers.TryGetValue(soldierId, out var soldier))
	{
	  soldier.QueueFree();
	  _soldiers.Remove(soldierId);
	}
  }

  private void OnSoldierInsert(EventContext ctx, SpacetimeDB.Types.Soldier soldierData)
  {
	if (soldierData.GameSessionId != GameId) return;
	SpawnSoldier(soldierData);
  }

  private void OnSoldierUpdate(EventContext ctx, SpacetimeDB.Types.Soldier oldSoldier, SpacetimeDB.Types.Soldier newSoldier)
  {
	if (newSoldier.GameSessionId != GameId) return;

	if (!oldSoldier.Dead && newSoldier.Dead)
	{
	  if (_soldiers.TryGetValue(newSoldier.Id, out var soldier))
	  {
		soldier.PlayDeath();
		var id = newSoldier.Id;
		GetTree().CreateTimer(3.0).Timeout += () => RemoveSoldier(id);
	  }
	  return;
	}

	if (oldSoldier.Dead && !newSoldier.Dead)
	{
	  if (_soldiers.TryGetValue(newSoldier.Id, out var soldier))
	  {
		soldier.Revive(new Vector3(newSoldier.Position.X, newSoldier.Position.Y, newSoldier.Position.Z));
	  }
	  else
	  {
		SpawnSoldier(newSoldier);
	  }
	  return;
	}

	if (_soldiers.TryGetValue(newSoldier.Id, out var existingSoldier))
	{
	  existingSoldier.OnStateUpdated(
		new Vector3(newSoldier.Position.X, newSoldier.Position.Y, newSoldier.Position.Z),
		newSoldier.RotationY
	  );
	}
  }

  private void OnSoldierDelete(EventContext ctx, SpacetimeDB.Types.Soldier soldierData)
  {
	if (soldierData.GameSessionId != GameId) return;
	RemoveSoldier(soldierData.Id);
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

	var gp = conn.Db.GamePlayer.GameSessionId.Filter(GameId)
	  .FirstOrDefault(g => g.PlayerId == activePlayerId && g.Active);
	if (gp == null) return;

	SpacetimeDB.Types.Squad playerLeaf = null;
	foreach (var sq in conn.Db.Squad.GamePlayerId.Filter(gp.Id))
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
	bool allDead = center.X == 0 && center.Y == 0 && center.Z == 0;

	if (depth == 0 && !gp.Dead)
	{
	  center = gp.Position;
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
