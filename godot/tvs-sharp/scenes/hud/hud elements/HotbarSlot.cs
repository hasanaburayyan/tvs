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
  private float _terrainSizeX;
  private float _terrainSizeY;
  private float _terrainSizeZ;
  private float _baseRange;
  public ulong _gamePlayerId;
  public ulong _gameSessionId;

  private TextureProgressBar _cooldownOverlay;
  private Label _cooldownLabel;
  private bool _onCooldown;
  private double _cooldownReadyAtSec;
  private double _cooldownDurationSec;

  public override void _Ready()
  {
	_label = GetNode<Label>("Label");
	_image = GetNode<TextureButton>("Image");
	_nameLabel = GetNodeOrNull<Label>("NameLabel");
	_cooldownOverlay = _image.GetNode<TextureProgressBar>("CooldownOverlay");
	_cooldownLabel = _image.GetNode<Label>("CooldownLabel");
	_image.Pressed += CastAbility;
  }

  public override void _Process(double delta)
  {
	if (!_onCooldown) return;

	double now = Time.GetUnixTimeFromSystem();
	double remaining = _cooldownReadyAtSec - now;

	if (remaining <= 0)
	{
	  ClearCooldown();
	  return;
	}

	double progress = _cooldownDurationSec > 0
	  ? Mathf.Clamp(remaining / _cooldownDurationSec, 0.0, 1.0)
	  : 0.0;
	_cooldownOverlay.Value = progress;
	_cooldownLabel.Text = remaining >= 10 ? $"{remaining:F0}" : $"{remaining:F1}";
  }

  private bool _subscribed;

  public void SetAbility(AbilityDef ability, string keybindLabel, ulong gameSessionId)
  {
	_abilityId = ability.Id;
	_abilityName = ability.Name;
	_validTargets = ability.ValidTargets;
	_gameSessionId = gameSessionId;
	_baseRange = ability.BaseRange;
	_terrainSizeX = ability.TerrainSizeX;
	_terrainSizeY = ability.TerrainSizeY;
	_terrainSizeZ = ability.TerrainSizeZ;
	_cooldownDurationSec = ability.CooldownMs / 1000.0;

	_label.Text = keybindLabel;
	if (_nameLabel != null)
	  _nameLabel.Text = ability.Name;

	_keybind = LabelToKey.GetValueOrDefault(keybindLabel, Key.None);
	TooltipText = $"{ability.Name}\n{ability.Description}";

	var iconPath = $"res://assets/icons/abilities/{ability.Name.ToLower().Replace(" ", "_")}.png";
	if (ResourceLoader.Exists(iconPath))
	  _image.TextureNormal = GD.Load<Texture2D>(iconPath);

	SubscribeToCooldowns();
  }

  private void SubscribeToCooldowns()
  {
	if (_subscribed) return;
	_subscribed = true;
	var conn = SpacetimeNetworkManager.Instance.Conn;
	conn.Db.AbilityCooldown.OnInsert += OnAbilityCoolDownInsert;
	conn.Db.AbilityCooldown.OnUpdate += OnAbilityCoolDownUpdate;
	conn.Db.AbilityCooldown.OnDelete += OnAbilityCoolDownDelete;
  }

  private void UnsubscribeFromCooldowns()
  {
	if (!_subscribed) return;
	_subscribed = false;
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;
	conn.Db.AbilityCooldown.OnInsert -= OnAbilityCoolDownInsert;
	conn.Db.AbilityCooldown.OnUpdate -= OnAbilityCoolDownUpdate;
	conn.Db.AbilityCooldown.OnDelete -= OnAbilityCoolDownDelete;
  }

  public override void _ExitTree()
  {
	UnsubscribeFromCooldowns();
  }

  public void OnAbilityCoolDownInsert(EventContext ctx, AbilityCooldown ac)
  {
	if (ac.GamePlayerId != _gamePlayerId) return;
	if (ac.AbilityId != _abilityId) return;
	StartCooldown(ac.ReadyAt);
  }

  public void OnAbilityCoolDownUpdate(EventContext ctx, AbilityCooldown oldAC, AbilityCooldown newAC)
  {
	if (newAC.GamePlayerId != _gamePlayerId) return;
	if (newAC.AbilityId != _abilityId) return;
	StartCooldown(newAC.ReadyAt);
  }

  public void OnAbilityCoolDownDelete(EventContext ctx, AbilityCooldown ac)
  {
	if (ac.GamePlayerId != _gamePlayerId) return;
	if (ac.AbilityId != _abilityId) return;
	ClearCooldown();
  }

  private void StartCooldown(SpacetimeDB.Timestamp readyAt)
  {
	_cooldownReadyAtSec = readyAt.MicrosecondsSinceUnixEpoch / 1_000_000.0;
	_onCooldown = true;
	_cooldownOverlay.Visible = true;
	_cooldownLabel.Visible = true;
  }

  private void ClearCooldown()
  {
	_onCooldown = false;
	_cooldownOverlay.Value = 0;
	_cooldownOverlay.Visible = false;
	_cooldownLabel.Visible = false;
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

  private bool IsGroundTargeted()
  {
	return _validTargets.Contains(TargetType.Ground)
	  && !_validTargets.Contains(TargetType.Enemy)
	  && !_validTargets.Contains(TargetType.Ally);
  }

  private void CastAbility()
  {
	if (_abilityId == 0) return;
	if (_onCooldown) return;

	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || mgr.ActivePlayerId == null) return;

	var conn = mgr.Conn;

	if (IsGroundTargeted() && _terrainSizeX > 0)
	{
	  PlacementMode.EnsureExists().Activate(_abilityId, _gameSessionId, _baseRange, _terrainSizeX, _terrainSizeY, _terrainSizeZ);
	  GD.Print($"[Hotbar] Entering placement mode for {_abilityName}");
	  return;
	}

	ulong? targetId = RequiresTarget() ? Targeting.Instance?.CurrentTargetGamePlayerId : null;

	if (RequiresTarget() && targetId == null)
	{
	  GD.Print($"[Hotbar] {_abilityName} requires a target");
	  return;
	}

	conn.Reducers.UseAbility(_gameSessionId, _abilityId, targetId, null, null, null);
	GD.Print($"[Hotbar] Casting {_abilityName}");
  }
}
