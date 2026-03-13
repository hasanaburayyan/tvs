using Godot;
using SpacetimeDB.Types;
using System.Collections.Generic;
using System.Linq;

public partial class PlayerHud : MarginContainer
{
  private static readonly PackedScene HotbarSlotScene = GD.Load<PackedScene>("uid://cg4w4jbcmtp2h");
  private static readonly PackedScene ResourceBarScene = GD.Load<PackedScene>("res://scenes/hud/hud elements/resource_bar.tscn");

  private static readonly string[] KeybindLabels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "-", "=" };

  private Control _hotbar;
  private VBoxContainer _resourceBars;

  private ResourceBar _healthBar;
  private ResourceBar _manaBar;
  private ResourceBar _staminaBar;
  private ResourceBar _ammoBar;

  private ulong _gameSessionId;
  private ulong _gamePlayerId;
  private bool _subscribedToUpdates;

  public override void _Ready()
  {
    _hotbar = GetNode<Control>("%Hotbar");
    _resourceBars = GetNode<VBoxContainer>("%ResourceBars");
  }

  public void Initialize(ulong gameSessionId)
  {
    _gameSessionId = gameSessionId;
    _gamePlayerId = FindLocalGamePlayerId();

    SetupResourceBars();
    LoadHotbarFromLoadout();
    SubscribeToUpdates();
  }

  public void Teardown()
  {
    _subscribedToUpdates = false;
    ClearHotbar();
    ClearResourceBars();
  }

  private ulong FindLocalGamePlayerId()
  {
    var mgr = SpacetimeNetworkManager.Instance;
    if (mgr?.Conn == null || mgr.ActivePlayerId == null) return 0;

    foreach (var gp in mgr.Conn.Db.GamePlayer.GameSessionId.Filter(_gameSessionId))
    {
      if (gp.PlayerId == mgr.ActivePlayerId && gp.Active)
        return gp.Id;
    }
    return 0;
  }

  private void LoadHotbarFromLoadout()
  {
    ClearHotbar();

    var mgr = SpacetimeNetworkManager.Instance;
    if (mgr?.Conn == null || mgr.ActivePlayerId == null) return;

    var conn = mgr.Conn;
    Loadout? loadout = null;
    foreach (var lo in conn.Db.Loadout.GameSessionId.Filter(_gameSessionId))
    {
      if (lo.PlayerId == mgr.ActivePlayerId)
      {
        loadout = lo;
        break;
      }
    }
    if (loadout == null) return;

    var weapon = conn.Db.WeaponDef.Id.Find(loadout.WeaponDefId);
    var skill = conn.Db.SkillDef.Id.Find(loadout.SkillDefId);
    if (weapon == null || skill == null) return;

    var abilityIds = new List<ulong>();
    abilityIds.Add(weapon.PrimaryAbilityId);
    abilityIds.Add(weapon.SecondaryAbilityId);
    abilityIds.AddRange(weapon.BonusAbilityIds);
    abilityIds.AddRange(skill.AbilityIds);

    int slotIndex = 0;
    foreach (var abilityId in abilityIds)
    {
      if (slotIndex >= KeybindLabels.Length) break;

      var ability = conn.Db.AbilityDef.Id.Find(abilityId);
      if (ability == null) continue;

      var slot = HotbarSlotScene.Instantiate<HotbarSlot>();
      slot._gamePlayerId = _gamePlayerId;
      slot._gameSessionId = _gameSessionId;
      _hotbar.AddChild(slot);
      slot.SetAbility(ability, KeybindLabels[slotIndex], _gameSessionId);

      slotIndex++;
    }
  }

  private void ClearHotbar()
  {
    if (_hotbar == null) return;
    foreach (var child in _hotbar.GetChildren())
      child.QueueFree();
  }

  private void SetupResourceBars()
  {
    ClearResourceBars();

    _healthBar = CreateResourceBar("HP", new Color(0.8f, 0.2f, 0.2f));
    _manaBar = CreateResourceBar("MP", new Color(0.2f, 0.4f, 0.8f));
    _staminaBar = CreateResourceBar("STA", new Color(0.8f, 0.6f, 0.2f));
    _ammoBar = CreateResourceBar("AMMO", new Color(0.8f, 0.8f, 0.2f));

    var mgr = SpacetimeNetworkManager.Instance;
    if (mgr?.Conn == null || _gamePlayerId == 0) return;

    var conn = mgr.Conn;
    var gp = conn.Db.GamePlayer.Id.Find(_gamePlayerId);
    if (gp != null)
      _healthBar.SetValues(gp.Health, gp.MaxHealth);

    foreach (var pool in conn.Db.ResourcePool.GamePlayerId.Filter(_gamePlayerId))
    {
      switch (pool.Kind)
      {
        case ResourceKind.Mana: _manaBar.SetValues(pool.Current, pool.Max); break;
        case ResourceKind.Stamina: _staminaBar.SetValues(pool.Current, pool.Max); break;
        case ResourceKind.Ammo: _ammoBar.SetValues(pool.Current, pool.Max); break;
      }
    }
  }

  private ResourceBar CreateResourceBar(string label, Color color)
  {
    var bar = ResourceBarScene.Instantiate<ResourceBar>();
    _resourceBars.AddChild(bar);
    bar.Initialize(label, color);
    return bar;
  }

  private void ClearResourceBars()
  {
    if (_resourceBars == null) return;
    foreach (var child in _resourceBars.GetChildren())
      child.QueueFree();
    _healthBar = null;
    _manaBar = null;
    _staminaBar = null;
    _ammoBar = null;
  }

  private void SubscribeToUpdates()
  {
    if (_subscribedToUpdates) return;
    _subscribedToUpdates = true;

    var conn = SpacetimeNetworkManager.Instance?.Conn;
    if (conn == null) return;

    conn.Db.GamePlayer.OnUpdate += OnGamePlayerUpdate;
    conn.Db.ResourcePool.OnUpdate += OnResourcePoolUpdate;
  }

  private void OnGamePlayerUpdate(EventContext ctx, GamePlayer oldGp, GamePlayer newGp)
  {
    if (newGp.Id != _gamePlayerId) return;
    _healthBar?.SetValues(newGp.Health, newGp.MaxHealth);
  }

  private void OnResourcePoolUpdate(EventContext ctx, ResourcePool oldPool, ResourcePool newPool)
  {
    if (newPool.GamePlayerId != _gamePlayerId) return;

    switch (newPool.Kind)
    {
      case ResourceKind.Mana: _manaBar?.SetValues(newPool.Current, newPool.Max); break;
      case ResourceKind.Stamina: _staminaBar?.SetValues(newPool.Current, newPool.Max); break;
      case ResourceKind.Ammo: _ammoBar?.SetValues(newPool.Current, newPool.Max); break;
    }
  }
}
