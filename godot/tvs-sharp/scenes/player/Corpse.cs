using Godot;

public partial class Corpse : Node3D
{
  private static readonly PackedScene NurseScene = GD.Load<PackedScene>("res://assets/models/nurse.tscn");

  public ulong EntityId;
  public ulong? SourceEntityId;
  public ulong PlayerId;
  public string PlayerName = "";

  private AnimationPlayer _animPlayer;

  public override void _Ready()
  {
	var nurse = NurseScene.Instantiate<Node3D>();
	nurse.Transform = new Transform3D(
	  new Basis(new Vector3(0, 1, 0), Mathf.Pi),
	  Vector3.Zero
	);
	AddChild(nurse);

	_animPlayer = nurse.GetNode<AnimationPlayer>("%AnimationPlayer");

	var deathAnim = _animPlayer.GetAnimation("Death");
	if (deathAnim != null)
	  deathAnim.LoopMode = Animation.LoopModeEnum.None;

	_animPlayer.Play("Death");
	_animPlayer.Seek(3.6f, true);

	if (!string.IsNullOrEmpty(PlayerName))
	{
	  var label = new Label3D();
	  label.Text = PlayerName;
	  label.Position = new Vector3(0, 0.5f, 0);
	  label.Modulate = new Color(0.6f, 0.6f, 0.6f, 0.7f);
	  label.FontSize = 48;
	  label.OutlineSize = 4;
	  AddChild(label);
	}
  }
}
