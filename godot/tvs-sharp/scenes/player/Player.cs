using Godot;
using SpacetimeDB;
using System;
using SpacetimeDB.Types;

public partial class Player : CharacterBody3D
{

  public Camera3D Camera;

  public const float SYNC_INTERVAL = 0.05f;
  public const float Speed = 5.0f;
  public const float JumpVelocity = 4.5f;
  public const float LERP_DURATION = 0.1f;

  public Identity OwnerIdentity;

  public ulong GameId;
  public bool IsLocal;
  public String username;

  private float _syncTimer = 0.0f;
  private Vector3 _lastSyncedPosition = Vector3.Zero;

  private float _lerpTime;
  private Vector3 _lerpStart;
  private Vector3 _lerpTarget;
  private Label3D _nametag;

  public override void _Ready()
  {
	Camera = GetNode<Camera3D>("%Camera3D");
	_lerpStart = Position;
	_lerpTarget = Position;
	_nametag = GetNode<Label3D>("%NameTag");
	

	IsLocal = OwnerIdentity == SpacetimeNetworkManager.Instance.Conn.Identity;
	if (IsLocal)
	{
	  Camera.MakeCurrent();
	}
	_nametag.Text = username;
  }

  public void OnPositionUpdated(Vector3 newPosition)
  {
	_lerpTime = 0.0f;
	_lerpStart = Position;
	_lerpTarget = newPosition;
  }

  public void ApplyPositionOverride(Vector3 position)
  {
	Position = position;
	_lerpStart = position;
	_lerpTarget = position;
	_lerpTime = LERP_DURATION;
	Velocity = Vector3.Zero;
  }

  public override void _PhysicsProcess(double delta)
  {
	if (!IsLocal)
	{
	  _lerpTime = Mathf.Min(_lerpTime + (float)delta, LERP_DURATION);
	  Position = _lerpStart.Lerp(_lerpTarget, _lerpTime / LERP_DURATION);
	  return;
	}

	Vector3 velocity = Velocity;

	if (!IsOnFloor())
	{
	  velocity += GetGravity() * (float)delta;
	}

	if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
	{
	  velocity.Y = JumpVelocity;
	}

	Vector2 inputDir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
	Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
	if (direction != Vector3.Zero)
	{
	  velocity.X = direction.X * Speed;
	  velocity.Z = direction.Z * Speed;
	}
	else
	{
	  velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
	  velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
	}

	Velocity = velocity;
	MoveAndSlide();

	_syncTimer += (float)delta;
	if (_syncTimer >= SYNC_INTERVAL && Position.DistanceSquaredTo(_lastSyncedPosition) > 0.001f)
	{
	  _lastSyncedPosition = Position;
	  _syncTimer = 0.0f;
	  SpacetimeNetworkManager.Instance.Conn.Reducers.MovePlayer(GameId, new DbVector3(Position.X, Position.Y, Position.Z));
	}
  }
}
