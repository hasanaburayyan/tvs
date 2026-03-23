using Godot;
using SpacetimeDB.Types;

public partial class UpgradeMode : Node3D
{
  public static UpgradeMode Instance { get; private set; }
  public bool IsActive { get; private set; }

  private const byte MaxRoadLevel = 3;
  private const byte MaxBaseLevel = 3;
  private const float RayLength = 1000f;

  private ulong _abilityId;
  private ulong _gameSessionId;
  private float _range;

  private Camera3D _camera;
  private ulong _hoveredEntityId;
  private MeshInstance3D _hoveredMesh;
  private StandardMaterial3D _highlightMat;
  private Label _costLabel;

  public static UpgradeMode EnsureExists()
  {
	if (Instance != null && IsInstanceValid(Instance) && Instance.IsInsideTree())
	  return Instance;

	var root = ((SceneTree)Engine.GetMainLoop()).Root;
	var node = new UpgradeMode { Name = "UpgradeMode" };
	root.AddChild(node);
	return node;
  }

  public override void _Ready()
  {
	Instance = this;
	Visible = false;

	_highlightMat = new StandardMaterial3D
	{
	  Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
	  AlbedoColor = new Color(0.2f, 0.7f, 1.0f, 0.25f),
	  EmissionEnabled = true,
	  Emission = new Color(0.2f, 0.7f, 1.0f),
	  EmissionEnergyMultiplier = 2.0f,
	  CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	  NoDepthTest = true,
	};

	_costLabel = new Label
	{
	  HorizontalAlignment = HorizontalAlignment.Center,
	  VerticalAlignment = VerticalAlignment.Center,
	  Visible = false,
	};
	_costLabel.AddThemeColorOverride("font_color", Colors.White);
	_costLabel.AddThemeFontSizeOverride("font_size", 20);
	_costLabel.AddThemeColorOverride("font_shadow_color", Colors.Black);
	_costLabel.AddThemeConstantOverride("shadow_offset_x", 2);
	_costLabel.AddThemeConstantOverride("shadow_offset_y", 2);
  }

  public void Activate(ulong abilityId, ulong gameSessionId, float range)
  {
	_abilityId = abilityId;
	_gameSessionId = gameSessionId;
	_range = range;
	_hoveredEntityId = 0;
	_hoveredMesh = null;
	_camera = GetViewport().GetCamera3D();
	IsActive = true;
	Visible = true;

	var canvas = GetTree().Root.GetNodeOrNull<CanvasLayer>("Main/HUD");
	if (canvas != null && _costLabel.GetParent() == null)
	  canvas.AddChild(_costLabel);
	else if (_costLabel.GetParent() == null)
	  GetTree().Root.AddChild(_costLabel);

	_costLabel.Visible = false;
  }

  public void Deactivate()
  {
	ClearHighlight();
	IsActive = false;
	Visible = false;
	_costLabel.Visible = false;
  }

  public override void _Process(double delta)
  {
	if (!IsActive) return;

	if (!IsInstanceValid(_camera) || !_camera.IsInsideTree())
	  _camera = GetViewport().GetCamera3D();
	if (_camera == null) return;

	var mousePos = GetViewport().GetMousePosition();
	var rayOrigin = _camera.ProjectRayOrigin(mousePos);
	var rayEnd = rayOrigin + _camera.ProjectRayNormal(mousePos) * RayLength;
	var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
	var result = GetViewport().World3D.DirectSpaceState.IntersectRay(query);

	ulong hitEntityId = 0;
	if (result != null && result.Count > 0 && result.TryGetValue("collider", out var colliderVariant))
	{
	  if (colliderVariant.Obj is Node colliderNode)
	  {
		var parent = colliderNode is StaticBody3D ? colliderNode : colliderNode.GetParentOrNull<StaticBody3D>();
		if (parent != null && ulong.TryParse(parent.Name, out ulong eid))
		  hitEntityId = eid;
	  }
	}

	if (hitEntityId != 0 && IsUpgradable(hitEntityId))
	{
	  if (hitEntityId != _hoveredEntityId)
	  {
		ClearHighlight();
		_hoveredEntityId = hitEntityId;
		ApplyHighlight(hitEntityId);
		ShowCost(hitEntityId, mousePos);
	  }
	  else
	  {
		ShowCost(hitEntityId, mousePos);
	  }
	}
	else
	{
	  if (_hoveredEntityId != 0)
		ClearHighlight();
	}
  }

  private bool IsUpgradable(ulong entityId)
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return false;

	var ent = conn.Db.Entity.EntityId.Find(entityId);
	if (ent == null || ent.GameSessionId != _gameSessionId) return false;

	byte myTeam = GetCasterTeamSlot();
	if (ent.TeamSlot != myTeam && ent.TeamSlot != 0) return false;

	var rs = conn.Db.RoadSegment.EntityId.Find(entityId);
	if (rs != null && rs.Level < MaxRoadLevel) return true;

	var brs = conn.Db.BaseResourceStore.EntityId.Find(entityId);
	if (brs != null && brs.Level < MaxBaseLevel) return true;

	return false;
  }

  private void ApplyHighlight(ulong entityId)
  {
	var node = FindTerrainNode(entityId);
	if (node == null) return;

	var mesh = node.GetNodeOrNull<MeshInstance3D>("MeshInstance3D");
	if (mesh == null)
	{
	  foreach (var child in node.GetChildren())
	  {
		if (child is MeshInstance3D m) { mesh = m; break; }
	  }
	}
	if (mesh == null) return;

	_hoveredMesh = mesh;
	mesh.MaterialOverlay = _highlightMat;
  }

  private void ClearHighlight()
  {
	if (_hoveredMesh != null && IsInstanceValid(_hoveredMesh))
	  _hoveredMesh.MaterialOverlay = null;
	_hoveredMesh = null;
	_hoveredEntityId = 0;
	_costLabel.Visible = false;
  }

  private void ShowCost(ulong entityId, Vector2 screenPos)
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;

	string text = "";
	var rs = conn.Db.RoadSegment.EntityId.Find(entityId);
	if (rs != null)
	{
	  int cost = 10 * rs.Level;
	  text = $"Upgrade Road (Lv {rs.Level} -> {rs.Level + 1})\nCost: {cost} Supplies";
	}
	else
	{
	  var brs = conn.Db.BaseResourceStore.EntityId.Find(entityId);
	  if (brs != null)
	  {
		int cost = 15 * brs.Level;
		text = $"Upgrade Base (Lv {brs.Level} -> {brs.Level + 1})\nCost: {cost} Supplies";
	  }
	}

	if (string.IsNullOrEmpty(text))
	{
	  _costLabel.Visible = false;
	  return;
	}

	_costLabel.Text = text;
	_costLabel.Visible = true;
	_costLabel.Position = screenPos + new Vector2(20, -30);
  }

  private Node3D FindTerrainNode(ulong entityId)
  {
	var mapMgr = GetTree().Root.GetNodeOrNull<Node>("Main/MapManager");
	if (mapMgr == null) return null;
	return mapMgr.GetNodeOrNull<Node3D>(entityId.ToString());
  }

  private byte GetCasterTeamSlot()
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

  public override void _UnhandledInput(InputEvent @event)
  {
	if (!IsActive) return;

	if (@event is InputEventMouseButton mb && mb.Pressed)
	{
	  if (mb.ButtonIndex == MouseButton.Left && _hoveredEntityId != 0)
	  {
		ConfirmUpgrade();
		GetViewport().SetInputAsHandled();
	  }
	  else if (mb.ButtonIndex == MouseButton.Right)
	  {
		Deactivate();
		GetViewport().SetInputAsHandled();
	  }
	}
	else if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
	{
	  Deactivate();
	  GetViewport().SetInputAsHandled();
	}
  }

  private void ConfirmUpgrade()
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;

	var ent = conn.Db.Entity.EntityId.Find(_hoveredEntityId);
	if (ent == null) return;

	var targetPos = new DbVector3 { X = ent.Position.X, Y = 0, Z = ent.Position.Z };
	conn.Reducers.UseAbility(_gameSessionId, _abilityId, null, targetPos, null, null);
	GD.Print($"[UpgradeMode] Upgrading entity {_hoveredEntityId}");

	Deactivate();
  }
}
