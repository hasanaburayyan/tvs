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

    var visual = new ProjectileVisual();
    visual.Name = $"Proj_{proj.Id}";
    visual.SetData(proj);
    AddChild(visual);
    _visuals[proj.Id] = visual;
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

public partial class ProjectileVisual : MeshInstance3D
{
  private Vector3 _direction;
  private float _speed;

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

  public ProjectileVisual()
  {
    Mesh = SharedMesh;
    MaterialOverride = SharedMaterial;
  }

  public void SetData(Projectile proj)
  {
    Position = new Vector3(proj.Position.X, proj.Position.Y, proj.Position.Z);
    _direction = new Vector3(proj.DirectionX, proj.DirectionY, proj.DirectionZ);
    _speed = proj.Speed;
  }

  public void Extrapolate(float delta)
  {
    Position += _direction * _speed * delta;
  }
}
