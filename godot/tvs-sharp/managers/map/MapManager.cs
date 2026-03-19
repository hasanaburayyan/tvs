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

	foreach (var feature in conn.Db.TerrainFeature.GameSessionId.Filter(GameId))
	{
	  SpawnFeature(feature);
	}

	foreach (var cp in conn.Db.CapturePoint.GameSessionId.Filter(GameId))
	{
	  SpawnCaptureFlag(cp);
	}

	conn.Db.TerrainFeature.OnInsert += OnTerrainFeatureInsert;
	conn.Db.TerrainFeature.OnUpdate += OnTerrainFeatureUpdate;
	conn.Db.TerrainFeature.OnDelete += OnTerrainFeatureDelete;

	conn.Db.CapturePoint.OnInsert += OnCapturePointInsert;
	conn.Db.CapturePoint.OnUpdate += OnCapturePointUpdate;
	conn.Db.CapturePoint.OnDelete += OnCapturePointDelete;
  }

  private void SpawnFeature(TerrainFeature feature)
  {
	if (!TerrainScenes.TryGetValue(feature.Type, out var scene))
	{
	  GD.PrintErr($"No scene for terrain type {feature.Type}");
	  return;
	}

	var node = scene.Instantiate<Node3D>();
	node.Name = feature.Id.ToString();
	node.Position = new Vector3(feature.PosX, 1f + (feature.SizeY / 2f) + feature.PosY, feature.PosZ);
	node.RotationDegrees = new Vector3(0, feature.RotationY, 0);

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

  private Node3D FindFeatureNode(ulong featureId)
  {
	var name = featureId.ToString();
	var node = GetNodeOrNull<Node3D>(name);
	if (node != null) return node;
	if (_floor != null)
	  node = _floor.GetNodeOrNull<Node3D>(name);
	return node;
  }

  private void OnTerrainFeatureInsert(EventContext ctx, TerrainFeature feature)
  {
	if (feature.GameSessionId != GameId) return;
	SpawnFeature(feature);
  }

  private void OnTerrainFeatureUpdate(EventContext ctx, TerrainFeature oldFeature, TerrainFeature newFeature)
  {
	if (newFeature.GameSessionId != GameId) return;
	if (!oldFeature.Expired && newFeature.Expired)
	{
	  FindFeatureNode(oldFeature.Id)?.QueueFree();
	}
	else if (oldFeature.Expired && !newFeature.Expired)
	{
	  SpawnFeature(newFeature);
	}
  }

  private void OnTerrainFeatureDelete(EventContext ctx, TerrainFeature feature)
  {
	if (feature.GameSessionId != GameId) return;
	FindFeatureNode(feature.Id)?.QueueFree();
  }

  private void SpawnCaptureFlag(SpacetimeDB.Types.CapturePoint cp)
  {
	var node = CaptureFlagScene.Instantiate<CaptureFlag>();
	node.Name = $"CP_{cp.Id}";
	node.PointId = cp.Id;
	node.Position = new Vector3(cp.PosX, 1f, cp.PosZ);
	AddChild(node);
	node.SetOwningTeam(cp.OwningTeam);
	node.SetInfluence(cp.InfluenceTeam1, cp.InfluenceTeam2, cp.MaxInfluence);
	EmitSignal(SignalName.CapturePointUpdated, (long)cp.Id, cp.PosX, cp.PosZ, cp.Radius, cp.InfluenceTeam1, cp.InfluenceTeam2, cp.MaxInfluence, (int)cp.OwningTeam);
  }

  private void OnCapturePointInsert(EventContext ctx, SpacetimeDB.Types.CapturePoint cp)
  {
	if (cp.GameSessionId != GameId) return;
	SpawnCaptureFlag(cp);
  }

  private void OnCapturePointUpdate(EventContext ctx, SpacetimeDB.Types.CapturePoint oldCp, SpacetimeDB.Types.CapturePoint newCp)
  {
	if (newCp.GameSessionId != GameId) return;
	var node = GetNodeOrNull<CaptureFlag>($"CP_{newCp.Id}");
	if (node == null) return;
	node.SetOwningTeam(newCp.OwningTeam);
	node.SetInfluence(newCp.InfluenceTeam1, newCp.InfluenceTeam2, newCp.MaxInfluence);
	EmitSignal(SignalName.CapturePointUpdated, (long)newCp.Id, newCp.PosX, newCp.PosZ, newCp.Radius, newCp.InfluenceTeam1, newCp.InfluenceTeam2, newCp.MaxInfluence, (int)newCp.OwningTeam);
  }

  private void OnCapturePointDelete(EventContext ctx, SpacetimeDB.Types.CapturePoint cp)
  {
	if (cp.GameSessionId != GameId) return;
	GetNodeOrNull<CaptureFlag>($"CP_{cp.Id}")?.QueueFree();
	EmitSignal(SignalName.CapturePointRemoved, (long)cp.Id);
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
