using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;

public partial class HotbarSlot : VBoxContainer
{
  private static readonly Dictionary<string, Key> LabelToKey = new()
  {
	{ "1", Key.Key1 }, { "2", Key.Key2 }, { "3", Key.Key3 },
	{ "4", Key.Key4 }, { "5", Key.Key5 }, { "6", Key.Key6 },
	{ "7", Key.Key7 }, { "8", Key.Key8 }, { "9", Key.Key9 },
	{ "0", Key.Key0 }, { "-", Key.Minus }, { "=", Key.Equal },
  };

  private Label _label;
  private Label _nameLabel;
  private TextureButton _image;
  private Key _keybind;

  private ulong _abilityId;
  private string _abilityName;
  private List<TargetType> _validTargets = new();
  private ulong _gameSessionId;

  public override void _Ready()
  {
	_label = GetNode<Label>("Label");
	_image = GetNode<TextureButton>("Image");
	_nameLabel = GetNodeOrNull<Label>("NameLabel");
	_image.Pressed += CastAbility;
  }

  public void SetAbility(AbilityDef ability, string keybindLabel, ulong gameSessionId)
  {
	_abilityId = ability.Id;
	_abilityName = ability.Name;
	_validTargets = ability.ValidTargets;
	_gameSessionId = gameSessionId;

	_label.Text = keybindLabel;
	if (_nameLabel != null)
	  _nameLabel.Text = ability.Name;

	_keybind = LabelToKey.GetValueOrDefault(keybindLabel, Key.None);
	TooltipText = $"{ability.Name}\n{ability.Description}";

	var iconPath = $"res://assets/icons/abilities/{ability.Name.ToLower().Replace(" ", "_")}.png";
	if (ResourceLoader.Exists(iconPath))
	  _image.TextureNormal = GD.Load<Texture2D>(iconPath);
  }

  public override void _UnhandledInput(InputEvent @event)
  {
	if (_abilityId == 0 || _keybind == Key.None) return;
	if (@event is not InputEventKey key) return;
	if (!key.Pressed || key.IsEcho()) return;
	if (key.Keycode != _keybind) return;

	CastAbility();
	GetViewport().SetInputAsHandled();
  }

  private bool RequiresTarget()
  {
	return _validTargets.Contains(TargetType.Enemy) || _validTargets.Contains(TargetType.Ally);
  }

  private void CastAbility()
  {
	if (_abilityId == 0) return;

	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || mgr.ActivePlayerId == null) return;

	var conn = mgr.Conn;

	ulong? targetId = RequiresTarget() ? Targeting.Instance?.CurrentTargetGamePlayerId : null;

	if (RequiresTarget() && targetId == null)
	{
	  GD.Print($"[Hotbar] {_abilityName} requires a target");
	  return;
	}

	conn.Reducers.UseAbility(_gameSessionId, _abilityId, targetId);
	GD.Print($"[Hotbar] Casting {_abilityName}");
  }
}
