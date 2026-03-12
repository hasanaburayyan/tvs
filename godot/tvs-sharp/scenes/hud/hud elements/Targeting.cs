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

  public override void _UnhandledInput(InputEvent @event)
  {
    if (@event is not InputEventMouseButton mb) return;
    if (mb.ButtonIndex != MouseButton.Left || !mb.Pressed) return;

    var cam = GetActiveCamera();
    if (cam == null) return;

    var mousePos = mb.Position;
    var rayOrigin = cam.ProjectRayOrigin(mousePos);
    var rayEnd = rayOrigin + cam.ProjectRayNormal(mousePos) * RayLength;
    var query = PhysicsRayQueryParameters3D.Create(rayOrigin, rayEnd);
    var result = GetViewport().World3D.DirectSpaceState.IntersectRay(query);

    var (target, targetable) = ResolveTarget(result);
    SetTarget(target, targetable);
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
    var localPlayer = conn.Db.Player.Id.Find(mgr.ActivePlayerId.Value);
    if (localPlayer == null) return;

    foreach (var gp in conn.Db.GamePlayer.Iter())
    {
      if (gp.PlayerId == mgr.ActivePlayerId && gp.Active)
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
