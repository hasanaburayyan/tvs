using Godot;

public partial class ResourceBar : HBoxContainer
{
  private Label _nameLabel;
  private ProgressBar _bar;
  private Label _valueLabel;

  public override void _Ready()
  {
    _nameLabel = GetNode<Label>("NameLabel");
    _bar = GetNode<ProgressBar>("Bar");
    _valueLabel = GetNode<Label>("ValueLabel");
  }

  public void Initialize(string label, Color color)
  {
    CallDeferred(nameof(DeferredInit), label, color);
  }

  private void DeferredInit(string label, Color color)
  {
    if (_nameLabel == null) _nameLabel = GetNode<Label>("NameLabel");
    if (_bar == null) _bar = GetNode<ProgressBar>("Bar");
    if (_valueLabel == null) _valueLabel = GetNode<Label>("ValueLabel");

    _nameLabel.Text = label;

    var stylebox = new StyleBoxFlat();
    stylebox.BgColor = color;
    stylebox.CornerRadiusBottomLeft = 2;
    stylebox.CornerRadiusBottomRight = 2;
    stylebox.CornerRadiusTopLeft = 2;
    stylebox.CornerRadiusTopRight = 2;
    _bar.AddThemeStyleboxOverride("fill", stylebox);
  }

  public void SetValues(int current, int max)
  {
    if (_bar == null || _valueLabel == null) return;
    _bar.MaxValue = max;
    _bar.Value = current;
    _valueLabel.Text = $"{current}/{max}";
  }
}
