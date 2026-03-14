using Godot;
using SpacetimeDB.Types;
using System;
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
  private readonly Dictionary<ResourceKind, ResourceBar> _poolBars = new();

  private ulong _gameSessionId;
  private ulong _gamePlayerId;
  private bool _subscribedToUpdates;

  private ColorRect _reticle;

  private PanelContainer _deathOverlay;
  private Label _deathTimerLabel;
  private Button _respawnButton;
  private double _respawnCountdown = -1;
  private ulong _deathGameSessionId;

  private VBoxContainer _killFeed;
  private readonly List<(Label label, double ttl)> _killFeedEntries = new();

  private Control _captureBarContainer;
  private ColorRect _captureBarBg;
  private ColorRect _captureBarFill;
  private readonly Dictionary<ulong, (float posX, float posZ, float radius, int inf1, int inf2, int max, byte owner)> _captureStates = new();
  private ulong? _activeCapturePointId;

  public override void _Ready()
  {
	_hotbar = GetNode<Control>("%Hotbar");
	_resourceBars = GetNode<VBoxContainer>("%ResourceBars");
	_reticle = CreateReticle();
	BuildDeathOverlay();
	BuildKillFeed();
	BuildCaptureBar();
  }

  public override void _Process(double delta)
  {
	var cam = GetViewport().GetCamera3D();
	bool locked = cam is FreelookCamera fc && fc.CameraLocked;
	_reticle.Visible = Visible && locked && !_deathOverlay.Visible;

	if (_reticle.Visible)
	{
	  var center = GetViewport().GetVisibleRect().Size / 2;
	  _reticle.Position = new Vector2(center.X - 3, center.Y - 3);
	}

	if (_deathOverlay.Visible && _respawnCountdown > 0)
	{
	  _respawnCountdown -= delta;
	  if (_respawnCountdown <= 0)
	  {
		_respawnCountdown = 0;
		_deathTimerLabel.Text = "You may now respawn";
		_respawnButton.Visible = true;
	  }
	  else
	  {
		_deathTimerLabel.Text = $"Respawning in {Math.Ceiling(_respawnCountdown):0}";
	  }
	}

	PositionKillFeed();
	PositionCaptureBar();
	for (int i = _killFeedEntries.Count - 1; i >= 0; i--)
	{
	  var (label, ttl) = _killFeedEntries[i];
	  ttl -= delta;
	  if (ttl <= 0)
	  {
		label.QueueFree();
		_killFeedEntries.RemoveAt(i);
	  }
	  else
	  {
		float alpha = (float)Math.Min(1.0, ttl);
		label.Modulate = new Color(1f, 1f, 1f, alpha);
		_killFeedEntries[i] = (label, ttl);
	  }
	}
  }

  private void BuildDeathOverlay()
  {
	_deathOverlay = new PanelContainer();
	_deathOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
	_deathOverlay.Visible = false;
	_deathOverlay.MouseFilter = MouseFilterEnum.Stop;

	var stylebox = new StyleBoxFlat
	{
	  BgColor = new Color(0, 0, 0, 0.6f),
	};
	_deathOverlay.AddThemeStyleboxOverride("panel", stylebox);

	var vbox = new VBoxContainer();
	vbox.SetAnchorsPreset(LayoutPreset.Center);
	vbox.GrowHorizontal = GrowDirection.Both;
	vbox.GrowVertical = GrowDirection.Both;
	vbox.Alignment = BoxContainer.AlignmentMode.Center;
	vbox.CustomMinimumSize = new Vector2(400, 200);
	vbox.Position = new Vector2(-200, -100);
	_deathOverlay.AddChild(vbox);

	var titleLabel = new Label();
	titleLabel.Text = "YOU DIED";
	titleLabel.HorizontalAlignment = HorizontalAlignment.Center;
	titleLabel.AddThemeFontSizeOverride("font_size", 48);
	titleLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.15f, 0.15f));
	vbox.AddChild(titleLabel);

	var spacer = new Control();
	spacer.CustomMinimumSize = new Vector2(0, 20);
	vbox.AddChild(spacer);

	_deathTimerLabel = new Label();
	_deathTimerLabel.Text = "";
	_deathTimerLabel.HorizontalAlignment = HorizontalAlignment.Center;
	_deathTimerLabel.AddThemeFontSizeOverride("font_size", 24);
	vbox.AddChild(_deathTimerLabel);

	var spacer2 = new Control();
	spacer2.CustomMinimumSize = new Vector2(0, 16);
	vbox.AddChild(spacer2);

	_respawnButton = new Button();
	_respawnButton.Text = "Respawn";
	_respawnButton.Visible = false;
	_respawnButton.CustomMinimumSize = new Vector2(160, 40);
	_respawnButton.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
	_respawnButton.Pressed += OnRespawnPressed;
	vbox.AddChild(_respawnButton);

	AddChild(_deathOverlay);
  }

  public void ShowDeathOverlay(ulong gameSessionId, long diedAtMicros, uint respawnTimerSeconds)
  {
	_deathGameSessionId = gameSessionId;
	_respawnCountdown = respawnTimerSeconds;

	if (diedAtMicros > 0)
	{
	  var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
	  var diedAtMs = diedAtMicros / 1000;
	  var elapsedSec = (now - diedAtMs) / 1000.0;
	  _respawnCountdown = Math.Max(0, respawnTimerSeconds - elapsedSec);
	}

	_respawnButton.Visible = _respawnCountdown <= 0;
	_deathTimerLabel.Text = _respawnCountdown > 0
	  ? $"Respawning in {Math.Ceiling(_respawnCountdown):0}"
	  : "You may now respawn";
	_deathOverlay.Visible = true;
  }

  public void HideDeathOverlay()
  {
	_deathOverlay.Visible = false;
	_respawnCountdown = -1;
  }

  private void OnRespawnPressed()
  {
	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;
	conn.Reducers.Respawn(_deathGameSessionId);
  }

  private void BuildKillFeed()
  {
	_killFeed = new VBoxContainer
	{
	  TopLevel = true,
	  CustomMinimumSize = new Vector2(300, 0),
	};
	_killFeed.AddThemeConstantOverride("separation", 4);
	AddChild(_killFeed);
  }

  private void PositionKillFeed()
  {
	if (_killFeed == null) return;
	var viewSize = GetViewport().GetVisibleRect().Size;
	_killFeed.Position = new Vector2(viewSize.X - 310, 10);
  }

  public void AddKillFeedEntry(string killerName, string victimName, byte killerTeam, byte victimTeam)
  {
	var label = new Label
	{
	  HorizontalAlignment = HorizontalAlignment.Right,
	  MouseFilter = MouseFilterEnum.Ignore,
	};
	label.AddThemeFontSizeOverride("font_size", 14);

	var killerColor = GetTeamColorHex(killerTeam);
	var victimColor = GetTeamColorHex(victimTeam);
	label.Text = $"{killerName} killed {victimName}";
	label.Modulate = new Color(1f, 1f, 1f, 1f);

	_killFeed.AddChild(label);
	_killFeedEntries.Add((label, 5.0));

	while (_killFeedEntries.Count > 6)
	{
	  _killFeedEntries[0].label.QueueFree();
	  _killFeedEntries.RemoveAt(0);
	}
  }

  private static Color GetTeamColorHex(byte team)
  {
	return team switch
	{
	  1 => new Color(0.3f, 0.5f, 1f),
	  2 => new Color(1f, 0.3f, 0.3f),
	  _ => new Color(0.7f, 0.7f, 0.7f),
	};
  }

  private void BuildCaptureBar()
  {
	_captureBarContainer = new Control
	{
	  TopLevel = true,
	  MouseFilter = MouseFilterEnum.Ignore,
	};
	AddChild(_captureBarContainer);

	_captureBarBg = new ColorRect
	{
	  Color = new Color(0.15f, 0.15f, 0.15f, 0.7f),
	  Size = new Vector2(200, 14),
	  MouseFilter = MouseFilterEnum.Ignore,
	};
	_captureBarContainer.AddChild(_captureBarBg);

	_captureBarFill = new ColorRect
	{
	  Color = new Color(1f, 1f, 1f, 0.9f),
	  Size = new Vector2(0, 10),
	  Position = new Vector2(0, 2),
	  MouseFilter = MouseFilterEnum.Ignore,
	};
	_captureBarBg.AddChild(_captureBarFill);

	_captureBarContainer.Visible = false;
  }

  public void UpdateCapturePoint(ulong pointId, float posX, float posZ, float radius, int inf1, int inf2, int max, byte owner)
  {
	_captureStates[pointId] = (posX, posZ, radius, inf1, inf2, max, owner);
	if (_activeCapturePointId == pointId)
	  RefreshCaptureBar(pointId);
	else
	  CheckCaptureProximity();
  }

  public void RemoveCapturePoint(ulong pointId)
  {
	_captureStates.Remove(pointId);
	if (_activeCapturePointId == pointId)
	{
	  _activeCapturePointId = null;
	  _captureBarContainer.Visible = false;
	}
  }

  private void CheckCaptureProximity()
  {
	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || _gamePlayerId == 0)
	{
	  ClearActiveCapturePoint();
	  return;
	}

	var gp = mgr.Conn.Db.GamePlayer.Id.Find(_gamePlayerId);
	if (gp == null || gp.Dead)
	{
	  ClearActiveCapturePoint();
	  return;
	}

	CheckCaptureProximityAt(gp.Position.X, gp.Position.Z);
  }

  private void CheckCaptureProximityAt(float px, float pz)
  {
	ulong? inside = null;
	foreach (var (id, state) in _captureStates)
	{
	  float dx = px - state.posX;
	  float dz = pz - state.posZ;
	  if (dx * dx + dz * dz <= state.radius * state.radius)
	  {
		inside = id;
		break;
	  }
	}

	if (inside != _activeCapturePointId)
	{
	  _activeCapturePointId = inside;
	  if (inside == null)
		_captureBarContainer.Visible = false;
	  else
		RefreshCaptureBar(inside.Value);
	}
  }

  private void ClearActiveCapturePoint()
  {
	if (_activeCapturePointId != null)
	{
	  _activeCapturePointId = null;
	  _captureBarContainer.Visible = false;
	}
  }

  private void RefreshCaptureBar(ulong pointId)
  {
	if (!_captureStates.TryGetValue(pointId, out var state))
	{
	  _captureBarContainer.Visible = false;
	  return;
	}

	_captureBarContainer.Visible = true;

	float barWidth = 200f;
	float barInner = barWidth - 4f;

	if (state.max <= 0)
	{
	  _captureBarFill.Size = new Vector2(0, 10);
	  return;
	}

	float t1 = state.inf1 / (float)state.max;
	float t2 = state.inf2 / (float)state.max;

	if (t1 >= t2)
	{
	  float fillW = t1 * barInner;
	  _captureBarFill.Size = new Vector2(fillW, 10);
	  _captureBarFill.Position = new Vector2(2, 2);
	  _captureBarFill.Color = new Color(0.3f, 0.5f, 1f, 0.9f);
	}
	else
	{
	  float fillW = t2 * barInner;
	  _captureBarFill.Size = new Vector2(fillW, 10);
	  _captureBarFill.Position = new Vector2(barWidth - 2 - fillW, 2);
	  _captureBarFill.Color = new Color(1f, 0.3f, 0.3f, 0.9f);
	}
  }

  private void PositionCaptureBar()
  {
	if (_captureBarContainer == null || !_captureBarContainer.Visible) return;
	var viewSize = GetViewport().GetVisibleRect().Size;
	_captureBarContainer.Position = new Vector2((viewSize.X - 200) / 2f, 10);
  }

  private ColorRect CreateReticle()
  {
	var dot = new ColorRect
	{
	  Color = new Color(1f, 1f, 1f, 0.85f),
	  CustomMinimumSize = new Vector2(6, 6),
	  Size = new Vector2(6, 6),
	  Visible = false,
	  MouseFilter = Control.MouseFilterEnum.Ignore,
	  TopLevel = true,
	};
	AddChild(dot);
	return dot;
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
	UnsubscribeFromUpdates();
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

	var archetype = conn.Db.ArchetypeDef.Id.Find(loadout.ArchetypeDefId);
	var weapon = conn.Db.WeaponDef.Id.Find(loadout.WeaponDefId);
	var skill = conn.Db.SkillDef.Id.Find(loadout.SkillDefId);
	if (weapon == null || skill == null) return;

	var abilityIds = new List<ulong>();

	if (archetype != null)
	  abilityIds.AddRange(archetype.InnateAbilityIds);

	abilityIds.Add(weapon.PrimaryAbilityId);
	abilityIds.AddRange(skill.AbilityIds);

	int slotIndex = 0;
	foreach (var abilityId in abilityIds)
	{
	  if (slotIndex >= KeybindLabels.Length) break;

	  var ability = conn.Db.AbilityDef.Id.Find(abilityId);
	  if (ability == null) continue;

	  var slot = HotbarSlotScene.Instantiate<HotbarSlot>();
	  _hotbar.AddChild(slot);
	  slot._gamePlayerId = _gamePlayerId;
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

	var mgr = SpacetimeNetworkManager.Instance;
	if (mgr?.Conn == null || _gamePlayerId == 0) return;

	var conn = mgr.Conn;
	var gp = conn.Db.GamePlayer.Id.Find(_gamePlayerId);
	if (gp != null)
	  _healthBar.SetValues(gp.Health, gp.MaxHealth);

	foreach (var pool in conn.Db.ResourcePool.GamePlayerId.Filter(_gamePlayerId))
	{
	  var (label, color) = pool.Kind switch
	  {
		ResourceKind.Stamina => ("STA", new Color(0.8f, 0.6f, 0.2f)),
		ResourceKind.Supplies => ("SUP", new Color(0.8f, 0.8f, 0.2f)),
		ResourceKind.Mana => ("MP", new Color(0.2f, 0.4f, 0.8f)),
		ResourceKind.Command => ("CMD", new Color(0.6f, 0.2f, 0.8f)),
		_ => ((string?)null, new Color(1, 1, 1)),
	  };

	  if (label == null) continue;

	  var bar = CreateResourceBar(label, color);
	  bar.SetValues(pool.Current, pool.Max);
	  _poolBars[pool.Kind] = bar;
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
	_poolBars.Clear();
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

  private void UnsubscribeFromUpdates()
  {
	if (!_subscribedToUpdates) return;
	_subscribedToUpdates = false;

	var conn = SpacetimeNetworkManager.Instance?.Conn;
	if (conn == null) return;

	conn.Db.GamePlayer.OnUpdate -= OnGamePlayerUpdate;
	conn.Db.ResourcePool.OnUpdate -= OnResourcePoolUpdate;
  }

  private void OnGamePlayerUpdate(EventContext ctx, GamePlayer oldGp, GamePlayer newGp)
  {
	if (newGp.Id != _gamePlayerId) return;
	_healthBar?.SetValues(newGp.Health, newGp.MaxHealth);

	if (newGp.Dead)
	  ClearActiveCapturePoint();
	else if (oldGp.Position.X != newGp.Position.X || oldGp.Position.Z != newGp.Position.Z || oldGp.Dead != newGp.Dead)
	  CheckCaptureProximityAt(newGp.Position.X, newGp.Position.Z);
  }

  private void OnResourcePoolUpdate(EventContext ctx, ResourcePool oldPool, ResourcePool newPool)
  {
	if (newPool.GamePlayerId != _gamePlayerId) return;

	if (_poolBars.TryGetValue(newPool.Kind, out var bar))
	  bar.SetValues(newPool.Current, newPool.Max);
  }
}
