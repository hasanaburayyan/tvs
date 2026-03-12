using Godot;
using System.Collections.Generic;

public partial class PlayerHud : MarginContainer
{
  private Control _hotbar;

  private static readonly PackedScene HotbarSlotScene = GD.Load<PackedScene>("uid://cg4w4jbcmtp2h");

  public override void _Ready()
  {
	_hotbar = GetNode<Control>("%Hotbar");
	LoadSpells(SpellRegistry.DefaultLoadout());
  }

  public void LoadSpells(List<SpellData> spells)
  {
	foreach (var child in _hotbar.GetChildren())
	  child.QueueFree();

	var labels = SpellRegistry.KeybindLabels;
	for (int i = 0; i < labels.Length; i++)
	{
	  var slot = HotbarSlotScene.Instantiate<HotbarSlot>();
	  _hotbar.AddChild(slot);

	  if (i < spells.Count)
		slot.SetSpell(spells[i], labels[i]);
	}
  }
}
