using Godot;
using SpacetimeDB;
using SpacetimeDB.Types;
using System.Collections.Generic;

public partial class ProjectileManager : Node
{
  private readonly Dictionary<ulong, ProjectileVisual> _visuals = new();

  public ulong GameId { get; set; } = 0;

  public void Initialize()
  {
    var conn = SpacetimeNetworkManager.Instance.Conn;

    foreach (var proj in conn.Db.Projectile.GameSessionId.Filter(GameId))
      SpawnVisual(proj);

    conn.Db.Projectile.OnInsert += OnProjectileInsert;
    conn.Db.Projectile.OnUpdate += OnProjectileUpdate;
    conn.Db.Projectile.OnDelete += OnProjectileDelete;
  }

  public override void _Process(double delta)
  {
    foreach (var kv in _visuals)
      kv.Value.Extrapolate((float)delta);
  }

  private void SpawnVisual(Projectile proj)
  {
    if (_visuals.ContainsKey(proj.Id)) return;

    bool isLocal = IsLocalPlayerCaster(proj.CasterEntityId);

    var visual = new ProjectileVisual();
    visual.Name = $"Proj_{proj.Id}";
    visual.ProjectileId = proj.Id;
    visual.GameSessionId = proj.GameSessionId;
    visual.IsLocalCaster = isLocal;
    visual.CasterEntityId = proj.CasterEntityId;
    visual.CasterTeamSlot = proj.CasterTeamSlot;
    visual.SetData(proj);
    AddChild(visual);
    _visuals[proj.Id] = visual;

    if (isLocal)
      GD.Print($"[PROJ-DEBUG] SpawnVisual: ProjId={proj.Id} CasterEntityId={proj.CasterEntityId} CasterTeamSlot={proj.CasterTeamSlot} IsLocal={isLocal}");
  }

  private static bool IsLocalPlayerCaster(ulong casterEntityId)
  {
    var mgr = SpacetimeNetworkManager.Instance;
    if (mgr?.Conn == null || mgr.ActivePlayerId == null) return false;
    foreach (var gp in mgr.Conn.Db.GamePlayer.PlayerId.Filter(mgr.ActivePlayerId.Value))
    {
      if (gp.Active && gp.EntityId == casterEntityId) return true;
    }
    return false;
  }

  private void OnProjectileInsert(EventContext ctx, Projectile proj)
  {
    if (proj.GameSessionId != GameId) return;
    SpawnVisual(proj);
  }

  private void OnProjectileUpdate(EventContext ctx, Projectile oldProj, Projectile newProj)
  {
    if (newProj.GameSessionId != GameId) return;
    if (_visuals.TryGetValue(newProj.Id, out var visual))
      visual.SetData(newProj);
  }

  private void OnProjectileDelete(EventContext ctx, Projectile proj)
  {
    if (!_visuals.TryGetValue(proj.Id, out var visual)) return;
    _visuals.Remove(proj.Id);
    visual.QueueFree();
  }

  public override void _ExitTree()
  {
    var conn = SpacetimeNetworkManager.Instance?.Conn;
    if (conn == null) return;
    conn.Db.Projectile.OnInsert -= OnProjectileInsert;
    conn.Db.Projectile.OnUpdate -= OnProjectileUpdate;
    conn.Db.Projectile.OnDelete -= OnProjectileDelete;
  }
}

public partial class ProjectileVisual : Node3D
{
  private Vector3 _direction;
  private float _speed;
  private float _dropRate;
  private bool _hitReported;

  public ulong ProjectileId;
  public ulong GameSessionId;
  public ulong CasterEntityId;
  public byte CasterTeamSlot;
  public bool IsLocalCaster;

  private MeshInstance3D _mesh;
  private Area3D _area;

  private static readonly SphereMesh SharedMesh = new()
  {
    Radius = 0.15f,
    Height = 0.3f,
    RadialSegments = 8,
    Rings = 4,
  };

  private static readonly StandardMaterial3D SharedMaterial = new()
  {
    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
    AlbedoColor = new Color(1.0f, 0.85f, 0.3f),
    EmissionEnabled = true,
    Emission = new Color(1.0f, 0.7f, 0.2f),
    EmissionEnergyMultiplier = 2f,
  };

  public override void _Ready()
  {
    _mesh = new MeshInstance3D
    {
      Mesh = SharedMesh,
      MaterialOverride = SharedMaterial,
    };
    AddChild(_mesh);

    if (IsLocalCaster)
    {
      _area = new Area3D();
      _area.CollisionLayer = 0;
      _area.CollisionMask = 0b0000_0110;
      var shape = new CollisionShape3D();
      shape.Shape = new SphereShape3D { Radius = 0.5f };
      _area.AddChild(shape);
      AddChild(_area);
      _area.BodyEntered += OnBodyEntered;
    }
  }

  public void SetData(Projectile proj)
  {
    Position = new Vector3(proj.Position.X, proj.Position.Y, proj.Position.Z);
    _direction = new Vector3(proj.DirectionX, proj.DirectionY, proj.DirectionZ);
    _speed = proj.Speed;
    _dropRate = proj.DropRate;
  }

  public void Extrapolate(float delta)
  {
    if (_dropRate > 0)
    {
      _direction.Y -= _dropRate * delta;
      _direction = _direction.Normalized();
    }
    Position += _direction * _speed * delta;
  }

  private void OnBodyEntered(Node3D body)
  {
    if (_hitReported) return;
    if (!IsLocalCaster) return;

    var targetable = Targetable.FindIn(body);
    if (targetable == null) return;

    GD.Print($"[PROJ-DEBUG] OnBodyEntered: ProjId={ProjectileId} body={body.Name} targetEntityId={targetable.EntityId} casterEntityId={CasterEntityId} casterTeam={CasterTeamSlot}");

    if (targetable.EntityId == CasterEntityId)
    {
      GD.Print($"[PROJ-DEBUG]   SKIP: CasterEntityId match");
      return;
    }

    var conn = SpacetimeNetworkManager.Instance?.Conn;
    if (conn == null) return;

    var casterGp = conn.Db.GamePlayer.EntityId.Find(CasterEntityId);
    if (casterGp != null)
    {
      var hitGp = conn.Db.GamePlayer.EntityId.Find(targetable.EntityId);
      if (hitGp != null && hitGp.PlayerId == casterGp.PlayerId)
      {
        GD.Print($"[PROJ-DEBUG]   SKIP: Own GamePlayer (PlayerId={casterGp.PlayerId})");
        return;
      }

      var hitSoldier = conn.Db.Soldier.EntityId.Find(targetable.EntityId);
      if (hitSoldier != null && hitSoldier.OwnerPlayerId == casterGp.PlayerId)
      {
        GD.Print($"[PROJ-DEBUG]   SKIP: Own Soldier (PlayerId={casterGp.PlayerId}, SoldierEntityId={targetable.EntityId})");
        return;
      }
    }

    var hitEntity = conn.Db.Entity.EntityId.Find(targetable.EntityId);
    if (hitEntity == null)
    {
      GD.Print($"[PROJ-DEBUG]   SKIP: Entity not found in cache for targetEntityId={targetable.EntityId}");
      return;
    }

    GD.Print($"[PROJ-DEBUG]   HitEntity: type={hitEntity.Type} teamSlot={hitEntity.TeamSlot} casterTeam={CasterTeamSlot}");

    if (hitEntity.TeamSlot != 0 && hitEntity.TeamSlot == CasterTeamSlot)
    {
      GD.Print($"[PROJ-DEBUG]   SKIP: Friendly fire (team={hitEntity.TeamSlot})");
      return;
    }

    _hitReported = true;

    GD.Print($"[PROJ-DEBUG]   REPORTING HIT: ProjId={ProjectileId} hitEntityId={targetable.EntityId} hitType={hitEntity.Type} hitTeam={hitEntity.TeamSlot}");

    var hitPos = new DbVector3 { X = Position.X, Y = Position.Y, Z = Position.Z };
    conn.Reducers.ReportProjectileHit(GameSessionId, ProjectileId, targetable.EntityId, hitPos);
  }
}
