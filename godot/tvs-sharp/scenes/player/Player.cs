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
  public const float ROTATION_SYNC_THRESHOLD = 0.01f;

  public ulong PlayerId;
  public ulong GamePlayerId;

  public ulong GameId;
  public bool IsLocal;
  public String username;

  public bool IsDead { get; private set; }

  private float _syncTimer = 0.0f;
  private Vector3 _lastSyncedPosition = Vector3.Zero;
  private float _lastSyncedRotationY = 0.0f;

  private float _lerpTime;
  private Vector3 _lerpStart;
  private Vector3 _lerpTarget;
  private float _rotLerpStart;
  private float _rotLerpTarget;
  private Label3D _nametag;
  public AnimationPlayer _animPlayer;

  public override void _Ready()
  {
	Camera = GetNode<Camera3D>("%Camera3D");
	_lerpStart = Position;
	_lerpTarget = Position;
	_rotLerpStart = Rotation.Y;
	_rotLerpTarget = Rotation.Y;
	_nametag = GetNode<Label3D>("%NameTag");
	_animPlayer = GetNode<Node3D>("%Nurse").GetNode<AnimationPlayer>("%AnimationPlayer");

	IsLocal = PlayerId == SpacetimeNetworkManager.Instance.ActivePlayerId;
	if (IsLocal)
	{
	  Camera.MakeCurrent();
	}
	_nametag.Text = username;

	var targetable = GetNode<Targetable>("%Targetable");
	targetable.Kind = TargetKind.Player;
	targetable.EntityId = GamePlayerId;
  }

  public void OnStateUpdated(Vector3 newPosition, float newRotationY)
  {
	if (IsDead) return;
	_lerpTime = 0.0f;
	_lerpStart = Position;
	_lerpTarget = newPosition;
	_rotLerpStart = Rotation.Y;
	_rotLerpTarget = newRotationY;
  }

  public void ApplyPositionOverride(Vector3 position)
  {
	Position = position;
	_lerpStart = position;
	_lerpTarget = position;
	_lerpTime = LERP_DURATION;
	Velocity = Vector3.Zero;
  }

  public void PlayDeath()
  {
	IsDead = true;
	_animPlayer.Play("Death");
  }

  public void Revive()
  {
	IsDead = false;
	if (_animPlayer.HasAnimation("RESET"))
	  _animPlayer.Play("RESET");
	else
	{
	  _animPlayer.Play("Rifle_Walk_Aiming");
	  _animPlayer.Seek(0, true);
	  _animPlayer.Stop();
	}
  }

  public void SetTeamColor(byte teamSlot)
  {
	_nametag.Modulate = teamSlot switch
	{
	  1 => new Color(0.3f, 0.5f, 1.0f),
	  2 => new Color(1.0f, 0.3f, 0.3f),
	  _ => new Color(1f, 1f, 1f),
	};
  }

  public override void _PhysicsProcess(double delta)
  {
	if (IsDead) return;

	if (!IsLocal)
	{
	  _lerpTime = Mathf.Min(_lerpTime + (float)delta, LERP_DURATION);
	  float t = _lerpTime / LERP_DURATION;
	  Position = _lerpStart.Lerp(_lerpTarget, t);
	  Rotation = new Vector3(Rotation.X, Mathf.LerpAngle(_rotLerpStart, _rotLerpTarget, t), Rotation.Z);

	  bool isMovingRemote = _lerpStart.DistanceSquaredTo(_lerpTarget) > 0.001f
				&& _lerpTime < LERP_DURATION;
	  UpdateAnimation(isMovingRemote);
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

	bool isMovingLocal = new Vector2(Velocity.X, Velocity.Z).LengthSquared() > 0.01f;
	UpdateAnimation(isMovingLocal);

	if (Input.IsActionJustPressed("squad_split"))
	{
	  SpacetimeNetworkManager.Instance.Conn.Reducers.SplitOwnedSquads(GameId);
	}

	_syncTimer += (float)delta;
	bool positionChanged = Position.DistanceSquaredTo(_lastSyncedPosition) > 0.001f;
	bool rotationChanged = Mathf.Abs(Rotation.Y - _lastSyncedRotationY) > ROTATION_SYNC_THRESHOLD;
	if (_syncTimer >= SYNC_INTERVAL && (positionChanged || rotationChanged))
	{
	  _lastSyncedPosition = Position;
	  _lastSyncedRotationY = Rotation.Y;
	  _syncTimer = 0.0f;
	  SpacetimeNetworkManager.Instance.Conn.Reducers.MovePlayer(
	  GameId,
	  new DbVector3(Position.X, Position.Y, Position.Z),
	  Rotation.Y
	  );
	}
  }

  private void UpdateAnimation(bool isMoving)
  {
	if (IsDead) return;

	if (isMoving)
	{
	  if (_animPlayer.CurrentAnimation != "Rifle_Walk_Aiming")
		_animPlayer.Play("Rifle_Walk_Aiming");
	}
	else
	{
	  	if (_animPlayer.IsPlaying())
		_animPlayer.Stop();
	}
	
  }
}
