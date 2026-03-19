using Godot;

public partial class Soldier : CharacterBody3D
{
  public const float LERP_DURATION = 0.15f;

  public ulong SoldierId;
  public ulong? OwnerPlayerId;

  public bool IsDead { get; private set; }

  private float _lerpTime;
  private Vector3 _lerpStart;
  private Vector3 _lerpTarget;
  private float _rotLerpStart;
  private float _rotLerpTarget;
  public AnimationPlayer _animPlayer;

  public override void _Ready()
  {
	_lerpStart = Position;
	_lerpTarget = Position;
	_rotLerpStart = Rotation.Y;
	_rotLerpTarget = Rotation.Y;
	_animPlayer = FindAnimationPlayer();

	var targetable = new Targetable();
	targetable.Name = "Targetable";
	targetable.SoldierId = SoldierId;
	AddChild(targetable);
  }

  private AnimationPlayer FindAnimationPlayer()
  {
	var nurse = GetNodeOrNull<Node3D>("%Nurse");
	if (nurse != null)
	{
	  var ap = nurse.GetNodeOrNull<AnimationPlayer>("%AnimationPlayer");
	  if (ap != null) return ap;
	}

	var direct = GetNodeOrNull<AnimationPlayer>("Nurse/AnimationPlayer");
	if (direct != null) return direct;

	var found = FindChildOfType<AnimationPlayer>(this);
	if (found == null)
	  GD.PrintErr($"[Soldier] Could not find AnimationPlayer in {Name}");
	return found;
  }

  private static T FindChildOfType<T>(Node node) where T : Node
  {
	foreach (var child in node.GetChildren())
	{
	  if (child is T t) return t;
	  var found = FindChildOfType<T>(child);
	  if (found != null) return found;
	}
	return null;
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

  public void PlayDeath()
  {
	IsDead = true;
	if (_animPlayer != null)
	  _animPlayer.Play("Death");
	else
	  GD.PrintErr($"[Soldier] PlayDeath called but _animPlayer is null on {Name}");
  }

  public void Revive(Vector3 position)
  {
	IsDead = false;
	Position = position;
	_lerpStart = position;
	_lerpTarget = position;
	_lerpTime = LERP_DURATION;
	if (_animPlayer != null)
	{
	  if (_animPlayer.HasAnimation("RESET"))
		_animPlayer.Play("RESET");
	  else
	  {
		_animPlayer.Play("Rifle_Walk_Aiming");
		_animPlayer.Seek(0, true);
		_animPlayer.Stop();
	  }
	}
  }

  public override void _PhysicsProcess(double delta)
  {
	if (IsDead) return;

	_lerpTime = Mathf.Min(_lerpTime + (float)delta, LERP_DURATION);
	float t = _lerpTime / LERP_DURATION;
	Position = _lerpStart.Lerp(_lerpTarget, t);
	Rotation = new Vector3(Rotation.X, Mathf.LerpAngle(_rotLerpStart, _rotLerpTarget, t), Rotation.Z);

	bool isMoving = _lerpStart.DistanceSquaredTo(_lerpTarget) > 0.001f
					&& _lerpTime < LERP_DURATION;
	UpdateAnimation(isMoving);
  }

  private void UpdateAnimation(bool isMoving)
  {
	if (IsDead || _animPlayer == null) return;

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
