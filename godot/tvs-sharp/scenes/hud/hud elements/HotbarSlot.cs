using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

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
  private TextureButton _image;
  private SpellData _spell;
  private Key _keybind;

  public override void _Ready()
  {
	_label = GetNode<Label>("Label");
	_image = GetNode<TextureButton>("Image");
	_image.Pressed += CastSpell;
  }

  public void SetSpell(SpellData spell, string keybindLabel)
  {
	_spell = spell;
	_label.Text = keybindLabel;

	if (spell.Icon != null)
	  _image.TextureNormal = spell.Icon;

	_keybind = LabelToKey.GetValueOrDefault(keybindLabel, Key.None);
	TooltipText = $"{spell.Name}\n{spell.Description}";
  }

  public override void _UnhandledInput(InputEvent @event)
  {
	if (_spell == null || _keybind == Key.None) return;
	if (@event is not InputEventKey key) return;
	if (!key.Pressed || key.IsEcho()) return;
	if (key.Keycode != _keybind) return;

	CastSpell();
	GetViewport().SetInputAsHandled();
  }

  private void CastSpell()
  {
	if (_spell == null) return;

	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || mgr.ActivePlayerId == null) return;

	var conn = mgr.Conn;
	ulong? gameSessionId = null;

	foreach (var gp in conn.Db.GamePlayer.Iter())
	{
	  if (gp.PlayerId == mgr.ActivePlayerId && gp.Active)
	  {
		gameSessionId = gp.GameSessionId;
		break;
	  }
	}

	if (gameSessionId == null) return;

	ulong? targetId = _spell.RequiresTarget ? Targeting.Instance?.CurrentTargetGamePlayerId : null;

	if (_spell.RequiresTarget && targetId == null)
	{
	  GD.Print($"[Hotbar] {_spell.Name} requires a target");
	  return;
	}

	conn.Reducers.CastSpell(gameSessionId.Value, _spell.Id, targetId);
	GD.Print($"[Hotbar] Casting {_spell.Name}");
  }
}
