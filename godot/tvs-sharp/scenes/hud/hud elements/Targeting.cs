using Godot;
using System.Collections.Generic;
using SpacetimeDB.Types;

public partial class Targeting : Control
{
  [Export]
  public float RayLength = 1000f;

  [Export]
  public float IndividualRingRadius = 0.7f;

  [Export]
  public float RingThickness = 0.05f;

  [Export]
  public int RingSegments = 64;

  [Export]
  public float SquadPadding = 1.0f;

  public static Targeting Instance { get; private set; }

  public ulong? CurrentTargetGamePlayerId =>
	_currentTargetable != null && !_currentTargetable.IsSoldier ? (ulong?)_currentTargetable.GamePlayerId : null;

  public ulong? CurrentTargetSoldierId =>
	_currentTargetable != null && _currentTargetable.IsSoldier ? (ulong?)_currentTargetable.SoldierId : null;

  private Camera3D _camera;
  private Node3D _currentTarget;
  private Targetable _currentTargetable;
  private MeshInstance3D _ringIndicator;

  public override void _Ready()
  {
	Instance = this;
	_camera = GetViewport().GetCamera3D();
	_ringIndicator = CreateUnitRing(new Color(1.0f, 0.3f, 0.1f, 0.85f));
  }

  private Camera3D GetActiveCamera()
  {
	if (!IsInstanceValid(_camera) || !_camera.IsInsideTree())
	  _camera = GetViewport().GetCamera3D();
	return _camera;
  }

  private bool IsCameraLocked()
  {
	var cam = GetActiveCamera();
	return cam is FreelookCamera fc && fc.CameraLocked;
  }

  public override void _UnhandledInput(InputEvent @event)
  {
	if (@event is not InputEventMouseButton mb || !mb.Pressed) return;
	if (PlacementMode.Instance?.IsActive == true) return;

	var cam = GetActiveCamera();
	if (cam == null) return;

	if (IsCameraLocked())
	  HandleLockedInput(mb, cam);
	else
	  HandleUnlockedInput(mb, cam);
  }

  private void HandleLockedInput(InputEventMouseButton mb, Camera3D cam)
  {
	if (mb.ButtonIndex == MouseButton.Right)
	{
	  var (target, targetable) = RaycastFromScreenCenter(cam);
	  SetTarget(target, targetable);
	}
	else if (mb.ButtonIndex == MouseButton.Left)
	{
	  var (target, targetable) = RaycastFromScreenCenter(cam);
	  SetTarget(target, targetable);
	  FirePrimaryAbility();
	}
  }

  private void HandleUnlockedInput(InputEventMouseButton mb, Camera3D cam)
  {
	if (mb.ButtonIndex != MouseButton.Left) return;

	var mousePos = mb.Position;
	var rayOrigin = cam.ProjectRayOrigin(mousePos);
	var rayEnd = rayOrigin + cam.ProjectRayNormal(mousePos) * RayLength;
	var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
	var result = GetViewport().World3D.DirectSpaceState.IntersectRay(query);

	var (target, targetable) = ResolveTarget(result);
	SetTarget(target, targetable);
  }

  private (Node3D, Targetable) RaycastFromScreenCenter(Camera3D cam)
  {
	var center = GetViewport().GetVisibleRect().Size / 2;
	var rayOrigin = cam.ProjectRayOrigin(center);
	var rayEnd = rayOrigin + cam.ProjectRayNormal(center) * RayLength;
	var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
	var result = GetViewport().World3D.DirectSpaceState.IntersectRay(query);
	return ResolveTarget(result);
  }

  private void FirePrimaryAbility()
  {
	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || mgr.ActivePlayerId == null) return;

	var conn = mgr.Conn;

	GamePlayer localGp = null;
	foreach (var gp in conn.Db.GamePlayer.PlayerId.Filter(mgr.ActivePlayerId.Value))
	{
	  if (gp.Active)
	  {
		localGp = gp;
		break;
	  }
	}
	if (localGp == null) return;

	Loadout loadout = null;
	foreach (var lo in conn.Db.Loadout.GameSessionId.Filter(localGp.GameSessionId))
	{
	  if (lo.PlayerId == mgr.ActivePlayerId)
	  {
		loadout = lo;
		break;
	  }
	}
	if (loadout == null) return;

	var weapon = conn.Db.WeaponDef.Id.Find(loadout.WeaponDefId);
	if (weapon == null) return;

	conn.Reducers.UseAbility(
	  localGp.GameSessionId,
	  weapon.PrimaryAbilityId,
	  CurrentTargetGamePlayerId,
	  CurrentTargetSoldierId,
	  null, null, null
	);
  }

  public override void _Process(double delta)
  {
	if (_currentTarget == null
	  || !IsInstanceValid(_currentTarget)
	  || !_currentTarget.IsInsideTree())
	{
	  if (_currentTarget != null)
		SetTarget(null, null);
	  return;
	}

	if (_currentTargetable != null && !ActiveWeaponAllowsSubSquadTargeting())
	{
	  ulong gpId = _currentTargetable.IsSoldier
		? FindOwnerGamePlayerId(_currentTargetable.SoldierId)
		: _currentTargetable.GamePlayerId;

	  if (gpId != 0)
	  {
		var bounds = ComputeSquadBounds(gpId);
		if (bounds.HasValue)
		{
		  float ringScale = Mathf.Max(bounds.Value.radius, IndividualRingRadius);
		  _ringIndicator.GlobalPosition = bounds.Value.center with { Y = 0.05f };
		  _ringIndicator.Scale = new Vector3(ringScale, 1f, ringScale);
		  _ringIndicator.Visible = true;
		  return;
		}
	  }
	}

	_ringIndicator.GlobalPosition = _currentTarget.GlobalPosition with { Y = _currentTarget.GlobalPosition.Y + 0.05f };
	_ringIndicator.Scale = new Vector3(IndividualRingRadius, 1f, IndividualRingRadius);
	_ringIndicator.Visible = true;
  }

  public void ClearTarget()
  {
	_currentTarget = null;
	_currentTargetable = null;
	_ringIndicator.Visible = false;
  }

  private void SetTarget(Node3D target, Targetable targetable)
  {
	_currentTarget = target;
	_currentTargetable = targetable;

	if (target == null)
	{
	  _ringIndicator.Visible = false;
	  SyncTargetToServer(null);
	  return;
	}

	_ringIndicator.GlobalPosition = target.GlobalPosition with { Y = target.GlobalPosition.Y + 0.05f };
	_ringIndicator.Scale = new Vector3(IndividualRingRadius, 1f, IndividualRingRadius);
	_ringIndicator.Visible = true;

	ulong? gpTarget;
	if (targetable.IsSoldier)
	{
	  ulong ownerId = FindOwnerGamePlayerId(targetable.SoldierId);
	  gpTarget = ownerId != 0 ? ownerId : null;
	}
	else
	{
	  gpTarget = targetable.GamePlayerId;
	}
	SyncTargetToServer(gpTarget);
  }

  private static void SyncTargetToServer(ulong? gamePlayerId)
  {
	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || mgr.ActivePlayerId == null) return;

	var conn = mgr.Conn;

	foreach (var gp in conn.Db.GamePlayer.PlayerId.Filter(mgr.ActivePlayerId.Value))
	{
	  if (gp.Active)
	  {
		conn.Reducers.SetTarget(gp.GameSessionId, gamePlayerId);
		return;
	  }
	}
  }

  private (Node3D, Targetable) ResolveTarget(Godot.Collections.Dictionary result)
  {
	if (result == null || result.Count == 0) return (null, null);
	if (!result.TryGetValue("collider", out var colliderVariant)) return (null, null);
	if (colliderVariant.Obj is not Node colliderNode) return (null, null);

	var targetable = Targetable.FindIn(colliderNode);
	if (targetable == null) return (null, null);

	var owner = targetable.GetOwner<Node3D>();
	if (owner == null) return (null, null);

	if (IsOwnEntity(owner, targetable)) return (null, null);

	return (owner, targetable);
  }

  private static bool IsOwnEntity(Node3D node, Targetable targetable)
  {
	if (node is Player player)
	  return player.IsLocal;

	if (targetable.IsSoldier)
	{
	  var mgr = SpacetimeNetworkManager.Instance;
	  if (mgr?.ActivePlayerId != null && node is Soldier soldier)
		return soldier.OwnerPlayerId == mgr.ActivePlayerId;
	}

	return false;
  }

  private static ulong FindOwnerGamePlayerId(ulong soldierId)
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return 0;

	foreach (var sq in conn.Db.Squad.SoldierId.Filter(soldierId))
	{
	  if (sq.ParentSquadId == 0) return 0;
	  var parent = conn.Db.Squad.Id.Find(sq.ParentSquadId);
	  if (parent == null) return 0;

	  foreach (var sibling in conn.Db.Squad.ParentSquadId.Filter(parent.Id))
	  {
		if (sibling.GamePlayerId != 0) return sibling.GamePlayerId;
	  }

	  var root = parent;
	  int depth = 0;
	  while (root.ParentSquadId != 0 && depth < 100)
	  {
		var p = conn.Db.Squad.Id.Find(root.ParentSquadId);
		if (p == null) break;
		root = p;
		depth++;
	  }
	  foreach (var leaf in IterateLeaves(conn, root.Id))
	  {
		if (leaf.GamePlayerId != 0) return leaf.GamePlayerId;
	  }
	}
	return 0;
  }

  private static List<Squad> IterateLeaves(DbConnection conn, ulong squadId)
  {
	var leaves = new List<Squad>();
	CollectLeaves(conn, squadId, leaves);
	return leaves;
  }

  private static void CollectLeaves(DbConnection conn, ulong squadId, List<Squad> leaves)
  {
	var squad = conn.Db.Squad.Id.Find(squadId);
	if (squad == null) return;

	if (squad.GamePlayerId != 0 || squad.SoldierId != 0)
	{
	  leaves.Add(squad);
	  return;
	}

	foreach (var child in conn.Db.Squad.ParentSquadId.Filter(squadId))
	  CollectLeaves(conn, child.Id, leaves);
  }

  private bool ActiveWeaponAllowsSubSquadTargeting()
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return false;

	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr.ActivePlayerId == null) return false;

	GamePlayer localGp = null;
	foreach (var gp in conn.Db.GamePlayer.PlayerId.Filter(mgr.ActivePlayerId.Value))
	{
	  if (gp.Active) { localGp = gp; break; }
	}
	if (localGp == null) return false;

	Loadout loadout = null;
	foreach (var lo in conn.Db.Loadout.GameSessionId.Filter(localGp.GameSessionId))
	{
	  if (lo.PlayerId == mgr.ActivePlayerId) { loadout = lo; break; }
	}
	if (loadout == null) return false;

	var weapon = conn.Db.WeaponDef.Id.Find(loadout.WeaponDefId);
	if (weapon == null) return false;

	var ability = conn.Db.AbilityDef.Id.Find(weapon.PrimaryAbilityId);
	if (ability == null) return false;

	return ability.AllowSubSquadTargeting;
  }

  private (Vector3 center, float radius)? ComputeSquadBounds(ulong targetGamePlayerId)
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return null;

	Squad leafSquad = null;
	foreach (var sq in conn.Db.Squad.GamePlayerId.Filter(targetGamePlayerId))
	{
	  leafSquad = sq;
	  break;
	}
	if (leafSquad == null) return null;

	var root = leafSquad;
	int depth = 0;
	while (root.ParentSquadId != 0 && depth < 100)
	{
	  var parent = conn.Db.Squad.Id.Find(root.ParentSquadId);
	  if (parent == null) break;
	  root = parent;
	  depth++;
	}

	var positions = new List<Vector3>();
	CollectEntityPositions(conn, root.Id, positions);

	if (positions.Count == 0) return null;

	var center = Vector3.Zero;
	foreach (var pos in positions)
	  center += pos;
	center /= positions.Count;

	float maxDistSq = 0f;
	foreach (var pos in positions)
	{
	  float dSq = new Vector2(pos.X - center.X, pos.Z - center.Z).LengthSquared();
	  if (dSq > maxDistSq) maxDistSq = dSq;
	}

	return (center, Mathf.Sqrt(maxDistSq) + SquadPadding);
  }

  private static void CollectEntityPositions(DbConnection conn, ulong squadId, List<Vector3> positions)
  {
	var squad = conn.Db.Squad.Id.Find(squadId);
	if (squad == null) return;

	if (squad.GamePlayerId != 0)
	{
	  var gp = conn.Db.GamePlayer.Id.Find(squad.GamePlayerId);
	  if (gp != null && !gp.Dead)
		positions.Add(new Vector3(gp.Position.X, gp.Position.Y, gp.Position.Z));
	  return;
	}

	if (squad.SoldierId != 0)
	{
	  var sol = conn.Db.Soldier.Id.Find(squad.SoldierId);
	  if (sol != null && !sol.Dead)
		positions.Add(new Vector3(sol.Position.X, sol.Position.Y, sol.Position.Z));
	  return;
	}

	foreach (var child in conn.Db.Squad.ParentSquadId.Filter(squadId))
	{
	  CollectEntityPositions(conn, child.Id, positions);
	}
  }

  private MeshInstance3D CreateUnitRing(Color color)
  {
	var mesh = new ImmediateMesh();
	var mat = new StandardMaterial3D
	{
	  ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
	  AlbedoColor = color,
	  Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
	  CullMode = BaseMaterial3D.CullModeEnum.Disabled,
	  NoDepthTest = true,
	};

	float innerR = 1.0f - RingThickness;
	float outerR = 1.0f + RingThickness;

	mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, mat);
	for (int i = 0; i <= RingSegments; i++)
	{
	  float angle = i * Mathf.Tau / RingSegments;
	  float cos = Mathf.Cos(angle);
	  float sin = Mathf.Sin(angle);

	  mesh.SurfaceSetNormal(Vector3.Up);
	  mesh.SurfaceAddVertex(new Vector3(cos * innerR, 0, sin * innerR));
	  mesh.SurfaceSetNormal(Vector3.Up);
	  mesh.SurfaceAddVertex(new Vector3(cos * outerR, 0, sin * outerR));
	}
	mesh.SurfaceEnd();

	var instance = new MeshInstance3D { Mesh = mesh, Visible = false };
	GetTree().Root.CallDeferred(Node.MethodName.AddChild, instance);
	return instance;
  }
}
