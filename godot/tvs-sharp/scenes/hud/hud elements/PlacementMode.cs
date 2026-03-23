using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

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
  private bool _isSnapping;

  private struct SnapPoint
  {
	public Vector3 Position;
	public float RotationDeg;
  }

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

  public void Activate(ulong abilityId, ulong gameSessionId, float range, float sizeX, float sizeY, float sizeZ, bool snap = false)
  {
	_abilityId = abilityId;
	_gameSessionId = gameSessionId;
	_range = range;
	_size = new Vector3(sizeX, sizeY, sizeZ);
	_isSnapping = snap;

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

	if (_isSnapping)
	{
	  ProcessSnapping(hitPoint, caster);
	}
	else
	{
	  ProcessFreePlace(hitPoint, caster);
	}
  }

  private void ProcessFreePlace(Vector3 hitPoint, Vector3 caster)
  {
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

  private void ProcessSnapping(Vector3 hitPoint, Vector3 caster)
  {
	var snaps = ComputeSnapPoints();
	if (snaps.Count == 0)
	{
	  _ghost.Visible = false;
	  return;
	}

	_ghost.Visible = true;
	float bestDistSq = float.MaxValue;
	SnapPoint best = snaps[0];

	foreach (var snap in snaps)
	{
	  float dx = snap.Position.X - hitPoint.X;
	  float dz = snap.Position.Z - hitPoint.Z;
	  float dSq = dx * dx + dz * dz;
	  if (dSq < bestDistSq)
	  {
		bestDistSq = dSq;
		best = snap;
	  }
	}

	_rotationDeg = best.RotationDeg;
	var toSnap = best.Position - caster;
	toSnap.Y = 0;
	float dist = toSnap.Length();

	var mat = _ghost.MaterialOverride as StandardMaterial3D;
	if (dist > _range)
	{
	  if (mat != null) mat.AlbedoColor = new Color(0.8f, 0.2f, 0.2f, 0.4f);
	}
	else
	{
	  if (mat != null) mat.AlbedoColor = new Color(0.2f, 0.8f, 0.2f, 0.4f);
	}

	_ghost.GlobalPosition = best.Position + Vector3.Up * (_size.Y * 0.5f);
	_ghost.Rotation = new Vector3(0f, Mathf.DegToRad(_rotationDeg), 0f);
  }

  private List<SnapPoint> ComputeSnapPoints()
  {
	var points = new List<SnapPoint>();
	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null) return points;

	var conn = mgr.Conn;
	byte myTeam = GetCasterTeamSlot();
	float roadLongHalf = _size.Z / 2f;

	foreach (var rs in conn.Db.RoadSegment.Iter())
	{
	  if (rs.GameSessionId != _gameSessionId) continue;
	  AddSnapPointsForEntity(conn, rs.EntityId, myTeam, roadLongHalf, points);
	}

	foreach (var brs in conn.Db.BaseResourceStore.Iter())
	{
	  if (brs.GameSessionId != _gameSessionId) continue;

	  var tgt = conn.Db.Targetable.EntityId.Find(brs.EntityId);
	  if (tgt != null && (tgt.Dead || tgt.Health <= 0)) continue;

	  AddSnapPointsForEntity(conn, brs.EntityId, myTeam, roadLongHalf, points);
	}

	return points;
  }

  private void AddSnapPointsForEntity(DbConnection conn, ulong entityId, byte myTeam, float roadLongHalf, List<SnapPoint> points)
  {
	var ent = conn.Db.Entity.EntityId.Find(entityId);
	if (ent == null) return;
	if (ent.TeamSlot != myTeam && ent.TeamSlot != 0) return;

	var tf = conn.Db.TerrainFeature.EntityId.Find(entityId);
	if (tf == null) return;

	float px = ent.Position.X;
	float pz = ent.Position.Z;
	float rotRad = ent.RotationY * Mathf.Pi / 180f;
	float cos = Mathf.Abs(Mathf.Cos(rotRad));
	float sin = Mathf.Abs(Mathf.Sin(rotRad));
	float parentHalfW = tf.SizeX / 2f * cos + tf.SizeZ / 2f * sin;
	float parentHalfD = tf.SizeX / 2f * sin + tf.SizeZ / 2f * cos;

	const float floorY = 1f;
	points.Add(new SnapPoint { Position = new Vector3(px, floorY, pz - parentHalfD - roadLongHalf), RotationDeg = 0 });
	points.Add(new SnapPoint { Position = new Vector3(px, floorY, pz + parentHalfD + roadLongHalf), RotationDeg = 0 });
	points.Add(new SnapPoint { Position = new Vector3(px + parentHalfW + roadLongHalf, floorY, pz), RotationDeg = 90 });
	points.Add(new SnapPoint { Position = new Vector3(px - parentHalfW - roadLongHalf, floorY, pz), RotationDeg = 90 });
  }

  private byte GetCasterTeamSlot()
  {
	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || mgr.ActivePlayerId == null) return 0;

	foreach (var gp in mgr.Conn.Db.GamePlayer.PlayerId.Filter(mgr.ActivePlayerId.Value))
	{
	  if (!gp.Active) continue;
	  var entity = mgr.Conn.Db.Entity.EntityId.Find(gp.EntityId);
	  if (entity != null)
		return entity.TeamSlot;
	}
	return 0;
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
	  else if (!_isSnapping && mb.ButtonIndex == MouseButton.WheelUp)
	  {
		_rotationDeg = (_rotationDeg + RotateStep) % 360f;
		GetViewport().SetInputAsHandled();
	  }
	  else if (!_isSnapping && mb.ButtonIndex == MouseButton.WheelDown)
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
	mgr.Conn.Reducers.UseAbility(_gameSessionId, _abilityId, null, targetPos, _rotationDeg, null);
	GD.Print($"[PlacementMode] Placed terrain at ({pos.X:F1}, {pos.Z:F1}) rot={_rotationDeg:F0}");

	Deactivate();
  }

  private Vector3? GetCasterPosition()
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
}
