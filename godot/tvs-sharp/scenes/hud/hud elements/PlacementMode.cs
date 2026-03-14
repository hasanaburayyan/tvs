using Godot;
using SpacetimeDB.Types;

public partial class PlacementMode : Node3D
{
  public static PlacementMode Instance { get; private set; }
  public bool IsActive { get; private set; }

  private MeshInstance3D _ghost;
  private Camera3D _camera;

  private const float RotateStep = 15f;

  private ulong _abilityId;
  private ulong _gameSessionId;
  private float _range;
  private Vector3 _size;
  private float _rotationDeg;

  public static PlacementMode EnsureExists()
  {
	if (Instance != null && IsInstanceValid(Instance) && Instance.IsInsideTree())
	  return Instance;

	var root = ((SceneTree)Engine.GetMainLoop()).Root;
	var node = new PlacementMode { Name = "PlacementMode" };
	root.AddChild(node);
	return node;
  }

  public override void _Ready()
  {
	Instance = this;
	Visible = false;
  }

  public void Activate(ulong abilityId, ulong gameSessionId, float range, float sizeX, float sizeY, float sizeZ)
  {
	_abilityId = abilityId;
	_gameSessionId = gameSessionId;
	_range = range;
	_size = new Vector3(sizeX, sizeY, sizeZ);

	if (_ghost != null && IsInstanceValid(_ghost))
	  _ghost.QueueFree();

	var box = new BoxMesh();
	box.Size = _size;
	var mat = new StandardMaterial3D
	{
	  Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
	  AlbedoColor = new Color(0.2f, 0.8f, 0.2f, 0.4f),
	  CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	};
	_ghost = new MeshInstance3D { Mesh = box, MaterialOverride = mat };
	AddChild(_ghost);

	_rotationDeg = 0f;
	_camera = GetViewport().GetCamera3D();
	IsActive = true;
	Visible = true;
  }

  public void Deactivate()
  {
	IsActive = false;
	Visible = false;
	if (_ghost != null && IsInstanceValid(_ghost))
	{
	  _ghost.QueueFree();
	  _ghost = null;
	}
  }

  public override void _Process(double delta)
  {
	if (!IsActive || _ghost == null) return;

	if (!IsInstanceValid(_camera) || !_camera.IsInsideTree())
	  _camera = GetViewport().GetCamera3D();
	if (_camera == null) return;

	var mousePos = GetViewport().GetMousePosition();
	var rayOrigin = _camera.ProjectRayOrigin(mousePos);
	var rayDir = _camera.ProjectRayNormal(mousePos);

	const float floorSurfaceY = 1f;
	if (Mathf.Abs(rayDir.Y) < 0.001f) return;
	float t = (floorSurfaceY - rayOrigin.Y) / rayDir.Y;
	if (t < 0) return;

	var hitPoint = rayOrigin + rayDir * t;

	var casterPos = GetCasterPosition();
	if (casterPos == null) return;

	var caster = casterPos.Value;
	var toHit = hitPoint - caster;
	toHit.Y = 0;
	float dist = toHit.Length();

	var mat = _ghost.MaterialOverride as StandardMaterial3D;
	if (dist > _range)
	{
	  hitPoint = caster + toHit.Normalized() * _range;
	  hitPoint.Y = 0;
	  if (mat != null) mat.AlbedoColor = new Color(0.8f, 0.2f, 0.2f, 0.4f);
	}
	else
	{
	  if (mat != null) mat.AlbedoColor = new Color(0.2f, 0.8f, 0.2f, 0.4f);
	}

	_ghost.GlobalPosition = hitPoint + Vector3.Up * (_size.Y * 0.5f);
	_ghost.Rotation = new Vector3(0f, Mathf.DegToRad(_rotationDeg), 0f);
  }

  public override void _UnhandledInput(InputEvent @event)
  {
	if (!IsActive) return;

	if (@event is InputEventMouseButton mb && mb.Pressed)
	{
	  if (mb.ButtonIndex == MouseButton.Left)
	  {
		ConfirmPlacement();
		GetViewport().SetInputAsHandled();
	  }
	  else if (mb.ButtonIndex == MouseButton.Right)
	  {
		Deactivate();
		GetViewport().SetInputAsHandled();
	  }
	  else if (mb.ButtonIndex == MouseButton.WheelUp)
	  {
		_rotationDeg = (_rotationDeg + RotateStep) % 360f;
		GetViewport().SetInputAsHandled();
	  }
	  else if (mb.ButtonIndex == MouseButton.WheelDown)
	  {
		_rotationDeg = (_rotationDeg - RotateStep + 360f) % 360f;
		GetViewport().SetInputAsHandled();
	  }
	}
	else if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
	{
	  Deactivate();
	  GetViewport().SetInputAsHandled();
	}
  }

  private void ConfirmPlacement()
  {
	if (_ghost == null) return;

	var pos = _ghost.GlobalPosition;
	pos.Y = 0f;

	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null) return;

	var targetPos = new DbVector3 { X = pos.X, Y = pos.Y, Z = pos.Z };
	mgr.Conn.Reducers.UseAbility(_gameSessionId, _abilityId, null, null, targetPos, _rotationDeg);
	GD.Print($"[PlacementMode] Placed terrain at ({pos.X:F1}, {pos.Z:F1}) rot={_rotationDeg:F0}");

	Deactivate();
  }

  private Vector3? GetCasterPosition()
  {
	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || mgr.ActivePlayerId == null) return null;

	foreach (var gp in mgr.Conn.Db.GamePlayer.Iter())
	{
	  if (gp.PlayerId == mgr.ActivePlayerId && gp.Active)
		return new Vector3(gp.Position.X, gp.Position.Y, gp.Position.Z);
	}
	return null;
  }
}
