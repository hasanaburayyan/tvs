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
  { TerrainType.Road, GD.Load<PackedScene>("res://scenes/terrain/road.tscn") },
  };

  private static readonly PackedScene CaptureFlagScene = GD.Load<PackedScene>("res://scenes/terrain/CaptureFlag.tscn");

  private CsgBox3D _floor;
  private readonly Dictionary<ulong, Label3D> _resourceIndicators = new();
  private readonly Dictionary<ulong, Label3D> _hpIndicators = new();
  private const float IndicatorVisibleRange = 30f;
  private const float IndicatorVisibleRangeSq = IndicatorVisibleRange * IndicatorVisibleRange;

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

	foreach (var store in conn.Db.BaseResourceStore.GameSessionId.Filter(GameId))
	  CreateOrUpdateResourceIndicator(store);

	conn.Db.BaseResourceStore.OnInsert += OnBaseResourceStoreInsert;
	conn.Db.BaseResourceStore.OnUpdate += OnBaseResourceStoreUpdate;
	conn.Db.BaseResourceStore.OnDelete += OnBaseResourceStoreDelete;

	foreach (var entity in conn.Db.Entity.GameSessionId.Filter(GameId))
	{
	  if (entity.Type != EntityType.Terrain) continue;
	  var targetable = conn.Db.Targetable.EntityId.Find(entity.EntityId);
	  if (targetable != null && targetable.MaxHealth > 0)
		CreateOrUpdateHpIndicator(entity.EntityId);
	}

	conn.Db.Targetable.OnInsert += OnTargetableInsertForHp;
	conn.Db.Targetable.OnUpdate += OnTargetableUpdateForHp;
  }

  public override void _Process(double delta)
  {
	if (_resourceIndicators.Count == 0 && _hpIndicators.Count == 0) return;

	var playerPos = GetLocalPlayerPosition();
	if (playerPos == null)
	{
	  foreach (var label in _resourceIndicators.Values)
		label.Visible = false;
	  foreach (var label in _hpIndicators.Values)
		label.Visible = false;
	  return;
	}

	var pp = playerPos.Value;
	foreach (var kv in _resourceIndicators)
	{
	  float dx = kv.Value.Position.X - pp.X;
	  float dz = kv.Value.Position.Z - pp.Z;
	  kv.Value.Visible = dx * dx + dz * dz <= IndicatorVisibleRangeSq;
	}

	byte myTeam = GetLocalTeamSlot();
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	foreach (var kv in _hpIndicators)
	{
	  float dx = kv.Value.Position.X - pp.X;
	  float dz = kv.Value.Position.Z - pp.Z;
	  bool inRange = dx * dx + dz * dz <= IndicatorVisibleRangeSq;

	  if (!inRange) { kv.Value.Visible = false; continue; }

	  var entity = conn?.Db.Entity.EntityId.Find(kv.Key);
	  if (entity == null) { kv.Value.Visible = false; continue; }

	  bool isFriendly = myTeam != 0 && entity.TeamSlot == myTeam;
	  if (isFriendly)
	  {
		kv.Value.Visible = true;
	  }
	  else
	  {
		var t = conn?.Db.Targetable.EntityId.Find(kv.Key);
		kv.Value.Visible = t != null && t.Health < t.MaxHealth;
	  }
	}
  }

  private Vector3? GetLocalPlayerPosition()
  {
	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || mgr.ActivePlayerId == null) return null;

	foreach (var gp in mgr.Conn.Db.GamePlayer.PlayerId.Filter(mgr.ActivePlayerId.Value))
	{
	  if (!gp.Active) continue;
	  var entity = mgr.Conn.Db.Entity.EntityId.Find(gp.EntityId);
	  if (entity != null)
		return new Vector3(entity.Position.X, entity.Position.Y, entity.Position.Z);
	}
	return null;
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

	var conn = SpacetimeNetworkManager.Instance?.Conn;
	var targetable = conn?.Db.Targetable.EntityId.Find(entity.EntityId);
	if (targetable != null && targetable.MaxHealth > 0)
	{
	  var t = new Targetable { EntityId = entity.EntityId, Type = EntityType.Terrain };
	  node.AddChild(t);

	  if (node is StaticBody3D sb)
		sb.CollisionLayer |= 0b0000_0010;
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

  private void OnBaseResourceStoreInsert(EventContext ctx, BaseResourceStore store)
  {
	if (store.GameSessionId != GameId) return;
	CreateOrUpdateResourceIndicator(store);
  }

  private void OnBaseResourceStoreUpdate(EventContext ctx, BaseResourceStore oldStore, BaseResourceStore newStore)
  {
	if (newStore.GameSessionId != GameId) return;
	CreateOrUpdateResourceIndicator(newStore);
  }

  private void OnBaseResourceStoreDelete(EventContext ctx, BaseResourceStore store)
  {
	if (_resourceIndicators.Remove(store.EntityId, out var label))
	  label.QueueFree();
  }

  private byte GetLocalTeamSlot()
  {
	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || mgr.ActivePlayerId == null) return 0;

	foreach (var gp in mgr.Conn.Db.GamePlayer.PlayerId.Filter(mgr.ActivePlayerId.Value))
	{
	  if (!gp.Active) continue;
	  var entity = mgr.Conn.Db.Entity.EntityId.Find(gp.EntityId);
	  if (entity != null) return entity.TeamSlot;
	}
	return 0;
  }

  private void CreateOrUpdateResourceIndicator(BaseResourceStore store)
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;

	byte myTeam = GetLocalTeamSlot();
	if (myTeam != 0 && store.TeamSlot != 0 && store.TeamSlot != myTeam)
	{
	  if (_resourceIndicators.Remove(store.EntityId, out var old))
		old.QueueFree();
	  return;
	}

	var entity = conn.Db.Entity.EntityId.Find(store.EntityId);
	if (entity == null) return;

	var feature = conn.Db.TerrainFeature.EntityId.Find(store.EntityId);

	if (!_resourceIndicators.TryGetValue(store.EntityId, out var label))
	{
	  label = new Label3D
	  {
		Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
		FixedSize = true,
		PixelSize = 0.003f,
		FontSize = 18,
		OutlineSize = 4,
		NoDepthTest = false,
		HorizontalAlignment = HorizontalAlignment.Center,
		VerticalAlignment = VerticalAlignment.Bottom,
		Visible = false,
	  };

	  float topY = 1f + (feature?.SizeY ?? 4f) + 1.5f;
	  label.Position = new Vector3(entity.Position.X, topY, entity.Position.Z);

	  AddChild(label);
	  _resourceIndicators[store.EntityId] = label;
	}

	const int barLen = 10;
	float ratio = store.SuppliesMax > 0 ? Mathf.Clamp((float)store.Supplies / store.SuppliesMax, 0f, 1f) : 0f;
	int filled = (int)(ratio * barLen);
	string bar = new string('\u2588', filled) + new string('\u2591', barLen - filled);

	string typeName = store.GenerationPerSecond > 0 ? "HQ" : "FOB";
	string genText = store.GenerationPerSecond > 0 ? $" +{store.GenerationPerSecond:F0}/s" : "";
	label.Text = $"{typeName} Lv{store.Level}{genText}\n{bar} {store.Supplies}/{store.SuppliesMax}";

	label.Modulate = store.TeamSlot switch
	{
	  1 => new Color(0.5f, 0.7f, 1.0f),
	  2 => new Color(1.0f, 0.5f, 0.5f),
	  _ => Colors.White,
	};
  }

  private void CreateOrUpdateHpIndicator(ulong entityId)
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;

	var entity = conn.Db.Entity.EntityId.Find(entityId);
	if (entity == null) return;

	var feature = conn.Db.TerrainFeature.EntityId.Find(entityId);
	if (feature == null) return;

	var targetable = conn.Db.Targetable.EntityId.Find(entityId);
	if (targetable == null || targetable.MaxHealth <= 0) return;

	if (!_hpIndicators.TryGetValue(entityId, out var label))
	{
	  label = new Label3D
	  {
		Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
		FixedSize = true,
		PixelSize = 0.003f,
		FontSize = 18,
		OutlineSize = 4,
		NoDepthTest = false,
		HorizontalAlignment = HorizontalAlignment.Center,
		VerticalAlignment = VerticalAlignment.Bottom,
		Visible = false,
	  };

	  float topY = 1f + (feature.SizeY) + 3.5f;
	  label.Position = new Vector3(entity.Position.X, topY, entity.Position.Z);

	  AddChild(label);
	  _hpIndicators[entityId] = label;
	}

	const int barLen = 10;
	float ratio = targetable.MaxHealth > 0 ? Mathf.Clamp((float)targetable.Health / targetable.MaxHealth, 0f, 1f) : 0f;
	int filled = (int)(ratio * barLen);
	string bar = new string('\u2588', filled) + new string('\u2591', barLen - filled);

	string typeName = feature.Type.ToString();
	string status = targetable.Dead ? "DESTROYED" : $"{targetable.Health}/{targetable.MaxHealth}";
	label.Text = $"{typeName} HP\n{bar} {status}";

	if (targetable.Dead)
	  label.Modulate = new Color(0.5f, 0.5f, 0.5f);
	else if (ratio > 0.6f)
	  label.Modulate = new Color(0.3f, 1.0f, 0.3f);
	else if (ratio > 0.3f)
	  label.Modulate = new Color(1.0f, 0.9f, 0.2f);
	else
	  label.Modulate = new Color(1.0f, 0.3f, 0.3f);
  }

  private void OnTargetableInsertForHp(EventContext ctx, SpacetimeDB.Types.Targetable targetable)
  {
	var entity = SpacetimeNetworkManager.Instance?.Conn?.Db.Entity.EntityId.Find(targetable.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;
	if (entity.Type != EntityType.Terrain) return;
	CreateOrUpdateHpIndicator(targetable.EntityId);
  }

  private void OnTargetableUpdateForHp(EventContext ctx, SpacetimeDB.Types.Targetable oldT, SpacetimeDB.Types.Targetable newT)
  {
	var entity = SpacetimeNetworkManager.Instance?.Conn?.Db.Entity.EntityId.Find(newT.EntityId);
	if (entity == null || entity.GameSessionId != GameId) return;
	if (entity.Type != EntityType.Terrain) return;
	CreateOrUpdateHpIndicator(newT.EntityId);
  }

  public void DestroyMap()
  {
	_resourceIndicators.Clear();
	_hpIndicators.Clear();
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
