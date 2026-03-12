using Godot;

public partial class DamageNumber : Label3D
{
  private const float Duration = 1.2f;
  private const float RiseDistance = 1.5f;

  private float _elapsed;
  private Vector3 _startPos;

  public void Setup(int amount, bool isHeal)
  {
    Text = isHeal ? $"+{amount}" : $"-{amount}";
    Modulate = isHeal ? new Color(0.2f, 1.0f, 0.3f) : new Color(1.0f, 0.2f, 0.2f);
    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
    FontSize = 48;
    OutlineSize = 8;
    FixedSize = true;
    NoDepthTest = true;
  }

  public override void _Ready()
  {
    _startPos = GlobalPosition;
  }

  public override void _Process(double delta)
  {
    _elapsed += (float)delta;
    float t = _elapsed / Duration;

    if (t >= 1.0f)
    {
      QueueFree();
      return;
    }

    GlobalPosition = _startPos + new Vector3(0, RiseDistance * t, 0);
    Modulate = Modulate with { A = 1.0f - (t * t) };
  }
}
