using Godot;
using SpacetimeDB.Types;

public partial class Targeting : Control
{
  [Export]
  public float RayLength = 1000f;

  [Export]
  public float RingRadius = 0.7f;

  [Export]
  public float RingThickness = 0.05f;

  [Export]
  public int RingSegments = 64;

  public static Targeting Instance { get; private set; }
  public ulong? CurrentTargetGamePlayerId => _currentTargetable?.GamePlayerId;

  private Camera3D _camera;
  private Node3D _currentTarget;
  private Targetable _currentTargetable;
  private MeshInstance3D _ringIndicator;

  public override void _Ready()
  {
    Instance = this;
    _camera = GetViewport().GetCamera3D();
    _ringIndicator = CreateRingIndicator();
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
      FirePrimaryAbility(targetable?.GamePlayerId);
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

  private void FirePrimaryAbility(ulong? targetGamePlayerId)
  {
    var mgr = SpacetimeNetworkManager.Instance;
    if (mgr?.Conn == null || mgr.ActivePlayerId == null) return;

    var conn = mgr.Conn;

    GamePlayer? localGp = null;
    foreach (var gp in conn.Db.GamePlayer.PlayerId.Filter(mgr.ActivePlayerId.Value))
    {
      if (gp.Active)
      {
        localGp = gp;
        break;
      }
    }
    if (localGp == null) return;

    Loadout? loadout = null;
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
      targetGamePlayerId,
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

    _ringIndicator.GlobalPosition = _currentTarget.GlobalPosition with { Y = _currentTarget.GlobalPosition.Y + 0.05f };
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
    _ringIndicator.Visible = true;
    SyncTargetToServer(targetable.GamePlayerId);
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

    if (IsLocalPlayer(owner)) return (null, null);

    return (owner, targetable);
  }

  private static bool IsLocalPlayer(Node3D node)
  {
    if (node is Player player)
      return player.IsLocal;
    return false;
  }

  private MeshInstance3D CreateRingIndicator()
  {
    var mesh = new ImmediateMesh();
    var mat = new StandardMaterial3D
    {
      ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
      AlbedoColor = new Color(1.0f, 0.3f, 0.1f, 0.85f),
      Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
      CullMode = BaseMaterial3D.CullModeEnum.Disabled,
      NoDepthTest = true,
    };

    mesh.SurfaceBegin(Mesh.PrimitiveType.TriangleStrip, mat);
    for (int i = 0; i <= RingSegments; i++)
    {
      float angle = i * Mathf.Tau / RingSegments;
      float cos = Mathf.Cos(angle);
      float sin = Mathf.Sin(angle);

      float innerR = RingRadius - RingThickness;
      float outerR = RingRadius + RingThickness;

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
