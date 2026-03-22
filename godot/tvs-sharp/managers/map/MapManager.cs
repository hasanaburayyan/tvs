using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Collections.Generic;

public partial class MapManager : Node
{
  [Signal]
  public delegate void CapturePointUpdatedEventHandler(long pointId, float posX, float posZ, float radius, int inf1, int inf2, int max, int owner);

  [Signal]
  public delegate void CapturePointRemovedEventHandler(long pointId);

  private static readonly Dictionary<TerrainType, PackedScene> TerrainScenes = new()
  {
  { TerrainType.Trench, GD.Load<PackedScene>("res://scenes/terrain/Trench.tscn") },
  { TerrainType.Tree, GD.Load<PackedScene>("res://scenes/terrain/Tree.tscn") },
  { TerrainType.Wall, GD.Load<PackedScene>("res://scenes/terrain/Wall.tscn") },
  { TerrainType.Building, GD.Load<PackedScene>("res://scenes/terrain/Building.tscn") },
  { TerrainType.CommandCenter, GD.Load<PackedScene>("res://scenes/terrain/CommandCenter.tscn") },
  { TerrainType.Outpost, GD.Load<PackedScene>("res://scenes/terrain/Outpost.tscn") },
  { TerrainType.Fortification, GD.Load<PackedScene>("res://scenes/terrain/Fortification.tscn") },
  };

  private static readonly PackedScene CaptureFlagScene = GD.Load<PackedScene>("res://scenes/terrain/CaptureFlag.tscn");

  private CsgBox3D _floor;

  public ulong GameId { get; set; } = 0;

  public void LoadMap()
  {
	_floor = GetNode<CsgBox3D>("%Floor");

	var conn = SpacetimeNetworkManager.Instance.Conn;

	foreach (var entity in conn.Db.Entity.GameSessionId.Filter(GameId))
	{
	  if (entity.Type == EntityType.Terrain)
	  {
		var feature = conn.Db.TerrainFeature.EntityId.Find(entity.EntityId);
		if (feature != null)
		  SpawnFeature(entity, feature);
	  }
	  else if (entity.Type == EntityType.CapturePoint)
	  {
		var cp = conn.Db.CapturePoint.EntityId.Find(entity.EntityId);
		if (cp != null)
		  SpawnCaptureFlag(entity, cp);
	  }
	}

	conn.Db.TerrainFeature.OnInsert += OnTerrainFeatureInsert;
	conn.Db.TerrainFeature.OnUpdate += OnTerrainFeatureUpdate;
	conn.Db.TerrainFeature.OnDelete += OnTerrainFeatureDelete;

	conn.Db.CapturePoint.OnInsert += OnCapturePointInsert;
	conn.Db.CapturePoint.OnUpdate += OnCapturePointUpdate;
	conn.Db.CapturePoint.OnDelete += OnCapturePointDelete;
  }

  private void SpawnFeature(Entity entity, TerrainFeature feature)
  {
	if (!TerrainScenes.TryGetValue(feature.Type, out var scene))
	{
	  GD.PrintErr($"No scene for terrain type {feature.Type}");
	  return;
	}

	var node = scene.Instantiate<Node3D>();
	node.Name = entity.EntityId.ToString();
	node.Position = new Vector3(entity.Position.X, 1f + (feature.SizeY / 2f) + entity.Position.Y, entity.Position.Z);
	node.RotationDegrees = new Vector3(0, entity.RotationY, 0);

	if (feature.Type == TerrainType.Trench && node is CsgBox3D csgTrench)
	{
	  csgTrench.Size = new Vector3(feature.SizeX, feature.SizeY, feature.SizeZ);
	  _floor.AddChild(csgTrench);
	}
	else
	{
	  node.Scale = new Vector3(feature.SizeX, feature.SizeY, feature.SizeZ);
	  AddChild(node);
	}
  }

  private Node3D FindFeatureNode(ulong entityId)
  {
	var name = entityId.ToString();
	var node = GetNodeOrNull<Node3D>(name);
	if (node != null) return node;
	if (_floor != null)
	  node = _floor.GetNodeOrNull<Node3D>(name);
	return node;
  }

  private void OnTerrainFeatureInsert(EventContext ctx, TerrainFeature feature)
  {
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(feature.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;
	SpawnFeature(entity, feature);
  }

  private void OnTerrainFeatureUpdate(EventContext ctx, TerrainFeature oldFeature, TerrainFeature newFeature)
  {
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(newFeature.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;

	if (!oldFeature.Expired && newFeature.Expired)
	{
	  FindFeatureNode(newFeature.EntityId)?.QueueFree();
	}
	else if (oldFeature.Expired && !newFeature.Expired)
	{
	  SpawnFeature(entity, newFeature);
	}
  }

  private void OnTerrainFeatureDelete(EventContext ctx, TerrainFeature feature)
  {
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(feature.EntityId);
	if (entity != null && entity.GameSessionId != GameId) return;
	FindFeatureNode(feature.EntityId)?.QueueFree();
  }

  private void SpawnCaptureFlag(Entity entity, SpacetimeDB.Types.CapturePoint cp)
  {
	var node = CaptureFlagScene.Instantiate<CaptureFlag>();
	node.Name = $"CP_{entity.EntityId}";
	node.EntityId = entity.EntityId;
	node.Position = new Vector3(entity.Position.X, 1f, entity.Position.Z);
	AddChild(node);
	node.SetOwningTeam(cp.OwningTeam);
	node.SetInfluence(cp.InfluenceTeam1, cp.InfluenceTeam2, cp.MaxInfluence);
	EmitSignal(SignalName.CapturePointUpdated, (long)entity.EntityId, entity.Position.X, entity.Position.Z, cp.Radius, cp.InfluenceTeam1, cp.InfluenceTeam2, cp.MaxInfluence, (int)cp.OwningTeam);
  }

  private void OnCapturePointInsert(EventContext ctx, SpacetimeDB.Types.CapturePoint cp)
  {
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(cp.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;
	SpawnCaptureFlag(entity, cp);
  }

  private void OnCapturePointUpdate(EventContext ctx, SpacetimeDB.Types.CapturePoint oldCp, SpacetimeDB.Types.CapturePoint newCp)
  {
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(newCp.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;

	var node = GetNodeOrNull<CaptureFlag>($"CP_{newCp.EntityId}");
	if (node == null) return;
	node.SetOwningTeam(newCp.OwningTeam);
	node.SetInfluence(newCp.InfluenceTeam1, newCp.InfluenceTeam2, newCp.MaxInfluence);
	EmitSignal(SignalName.CapturePointUpdated, (long)newCp.EntityId, entity.Position.X, entity.Position.Z, newCp.Radius, newCp.InfluenceTeam1, newCp.InfluenceTeam2, newCp.MaxInfluence, (int)newCp.OwningTeam);
  }

  private void OnCapturePointDelete(EventContext ctx, SpacetimeDB.Types.CapturePoint cp)
  {
	var entity = SpacetimeNetworkManager.Instance.Conn.Db.Entity.EntityId.Find(cp.EntityId);
	if (entity != null && entity.GameSessionId != GameId) return;
	GetNodeOrNull<CaptureFlag>($"CP_{cp.EntityId}")?.QueueFree();
	EmitSignal(SignalName.CapturePointRemoved, (long)cp.EntityId);
  }

  public void DestroyMap()
  {
	foreach (var child in GetChildren())
	{
	  child.QueueFree();
	}
	if (_floor != null)
	{
	  foreach (var child in _floor.GetChildren())
	  {
		child.QueueFree();
	  }
	}
  }
}
