using Godot;
using SpacetimeDB;
using System;
using SpacetimeDB.Types;

public partial class Player : CharacterBody3D
{

  public Camera3D Camera;

  public const float SYNC_INTERVAL = 0.016f;
  public const float Speed = 5.0f;
  public const float JumpVelocity = 4.5f;

  public Identity OwnerIdentity;

  public ulong GameId;
  public bool IsLocal;

  private float _syncTimer = 0.0f;
  private Vector3 _lastSyncedPosition = Vector3.Zero;

  public override void _Ready()
  {
	Camera = GetNode<Camera3D>("%Camera3D");

	IsLocal = OwnerIdentity == SpacetimeNetworkManager.Instance.Conn.Identity;
	if (IsLocal)
	{
	  Camera.MakeCurrent();
	}
  }

  public override void _PhysicsProcess(double delta)
  {
	if (!IsLocal)
	{
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
