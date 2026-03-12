using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;

public partial class SpellData : Resource
{
  [Export] public string Name = "";
  [Export] public string Description = "";
  [Export] public float CooldownSeconds = 1.0f;
  [Export] public bool RequiresTarget = true;
  [Export] public Texture2D Icon;

  public SpellId Id;
}

public static class SpellRegistry
{
  private static readonly string[] DefaultKeybindLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };

  public static string[] KeybindLabels => DefaultKeybindLabels;

  public static List<SpellData> DefaultLoadout()
  {
    return new List<SpellData>
    {
      new() { Id = SpellId.Fireball, Name = "Fireball", Description = "Hurls a ball of fire at the target", CooldownSeconds = 2.0f, RequiresTarget = true },
      new() { Id = SpellId.IceLance, Name = "Ice Lance", Description = "Pierces the target with a shard of ice", CooldownSeconds = 1.5f, RequiresTarget = true },
      new() { Id = SpellId.Heal, Name = "Heal", Description = "Restores health to yourself", CooldownSeconds = 3.0f, RequiresTarget = false },
      new() { Id = SpellId.Shield, Name = "Shield", Description = "Surrounds you with a protective barrier", CooldownSeconds = 8.0f, RequiresTarget = false },
      new() { Id = SpellId.Thunderbolt, Name = "Thunderbolt", Description = "Strikes the target with lightning", CooldownSeconds = 4.0f, RequiresTarget = true },
    };
  }
}
