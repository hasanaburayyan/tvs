using Godot;
using SpacetimeDB.Types;
using System;
using System.Linq;

public partial class LoadoutSelect : PopulableMenu
{
  [Export]
  public Hud hud;

  private VBoxContainer _archetypeList;
  private VBoxContainer _weaponList;
  private VBoxContainer _skillList;
  private Button _confirmButton;
  private Label _selectedArchetypeLabel;
  private Label _selectedWeaponLabel;
  private Label _selectedSkillLabel;

  private ulong? _selectedArchetypeId;
  private ulong? _selectedWeaponId;
  private ulong? _selectedSkillId;

  public override void _Ready()
  {
    _archetypeList = GetNode<VBoxContainer>("%ArchetypeList");
    _weaponList = GetNode<VBoxContainer>("%WeaponList");
    _skillList = GetNode<VBoxContainer>("%SkillList");
    _confirmButton = GetNode<Button>("%ConfirmButton");
    _selectedArchetypeLabel = GetNode<Label>("%SelectedArchetypeLabel");
    _selectedWeaponLabel = GetNode<Label>("%SelectedWeaponLabel");
    _selectedSkillLabel = GetNode<Label>("%SelectedSkillLabel");

    _confirmButton.Pressed += OnConfirmPressed;
  }

  public override void Populate()
  {
    _selectedArchetypeId = null;
    _selectedWeaponId = null;
    _selectedSkillId = null;
    UpdateSelectionLabels();
    PopulateArchetypes();
    PopulateWeapons();
    PopulateSkills();
  }

  private void PopulateArchetypes()
  {
    ClearList(_archetypeList);
    var conn = SpacetimeNetworkManager.Instance?.Conn;
    if (conn == null) return;

    foreach (var archetype in conn.Db.ArchetypeDef.Iter())
    {
      var btn = new Button();
      btn.Text = $"{archetype.Name}\n{archetype.Description}";
      btn.ClipText = true;
      btn.CustomMinimumSize = new Vector2(0, 48);
      var id = archetype.Id;
      btn.Pressed += () => SelectArchetype(id);
      _archetypeList.AddChild(btn);
    }
  }

  private void PopulateWeapons()
  {
    ClearList(_weaponList);
    var conn = SpacetimeNetworkManager.Instance?.Conn;
    if (conn == null) return;

    foreach (var weapon in conn.Db.WeaponDef.Iter())
    {
      var btn = new Button();
      btn.Text = $"{weapon.Name}\n{weapon.Description}";
      btn.ClipText = true;
      btn.CustomMinimumSize = new Vector2(0, 48);
      var id = weapon.Id;
      btn.Pressed += () => SelectWeapon(id);
      _weaponList.AddChild(btn);
    }
  }

  private void PopulateSkills()
  {
    ClearList(_skillList);
    var conn = SpacetimeNetworkManager.Instance?.Conn;
    if (conn == null) return;

    foreach (var skill in conn.Db.SkillDef.Iter())
    {
      if (_selectedArchetypeId != null && skill.ArchetypeDefId != _selectedArchetypeId)
        continue;

      var btn = new Button();
      btn.Text = $"{skill.Name}\n{skill.Description}";
      btn.ClipText = true;
      btn.CustomMinimumSize = new Vector2(0, 48);
      var id = skill.Id;
      btn.Pressed += () => SelectSkill(id);
      _skillList.AddChild(btn);
    }
  }

  private void SelectArchetype(ulong id)
  {
    _selectedArchetypeId = id;
    _selectedSkillId = null;
    UpdateSelectionLabels();
    PopulateSkills();
  }

  private void SelectWeapon(ulong id)
  {
    _selectedWeaponId = id;
    UpdateSelectionLabels();
  }

  private void SelectSkill(ulong id)
  {
    _selectedSkillId = id;
    UpdateSelectionLabels();
  }

  private void UpdateSelectionLabels()
  {
    var conn = SpacetimeNetworkManager.Instance?.Conn;

    if (_selectedArchetypeId != null && conn != null)
    {
      var a = conn.Db.ArchetypeDef.Id.Find(_selectedArchetypeId.Value);
      _selectedArchetypeLabel.Text = a != null ? $"Archetype: {a.Name}" : "Archetype: ???";
    }
    else
    {
      _selectedArchetypeLabel.Text = "Archetype: (none)";
    }

    if (_selectedWeaponId != null && conn != null)
    {
      var w = conn.Db.WeaponDef.Id.Find(_selectedWeaponId.Value);
      _selectedWeaponLabel.Text = w != null ? $"Weapon: {w.Name}" : "Weapon: ???";
    }
    else
    {
      _selectedWeaponLabel.Text = "Weapon: (none)";
    }

    if (_selectedSkillId != null && conn != null)
    {
      var s = conn.Db.SkillDef.Id.Find(_selectedSkillId.Value);
      _selectedSkillLabel.Text = s != null ? $"Skillset: {s.Name}" : "Skillset: ???";
    }
    else
    {
      _selectedSkillLabel.Text = "Skillset: (none)";
    }

    _confirmButton.Disabled = _selectedArchetypeId == null || _selectedWeaponId == null || _selectedSkillId == null;
  }

  private void OnConfirmPressed()
  {
    if (_selectedArchetypeId == null || _selectedWeaponId == null || _selectedSkillId == null) return;

    var conn = SpacetimeNetworkManager.Instance?.Conn;
    if (conn == null) return;

    conn.Reducers.OnSetLoadout += OnSetLoadoutResult;
    conn.Reducers.SetLoadout(hud.sessionID, _selectedArchetypeId.Value, _selectedWeaponId.Value, _selectedSkillId.Value);
    _confirmButton.Disabled = true;
  }

  private void OnSetLoadoutResult(ReducerEventContext ctx, ulong gameId, ulong archetypeDefId, ulong weaponDefId, ulong skillDefId)
  {
    var conn = SpacetimeNetworkManager.Instance?.Conn;
    if (conn == null) return;
    if (ctx.Event.CallerIdentity != conn.Identity) return;

    conn.Reducers.OnSetLoadout -= OnSetLoadoutResult;

    if (ctx.Event.Status is not SpacetimeDB.Status.Committed)
    {
      _confirmButton.Disabled = false;
      GD.PrintErr("SetLoadout failed");
      return;
    }

    var activePlayerId = SpacetimeNetworkManager.Instance?.ActivePlayerId;
    if (activePlayerId != null)
    {
      var gp = conn.Db.GamePlayer.PlayerId.Filter(activePlayerId.Value).FirstOrDefault(g => g.Active);
      if (gp != null)
      {
        var targetable = conn.Db.Targetable.EntityId.Find(gp.EntityId);
        if (targetable != null && targetable.Dead)
        {
          hud.CloseMenus();
          hud.ShowDeathOverlay(hud.sessionID, 0, 0);
          return;
        }
      }
    }

    hud.EmitSignal(Hud.SignalName.StartLobby, (long)hud.sessionID);
    hud.CloseMenus();
  }

  private static void ClearList(Control container)
  {
    foreach (var child in container.GetChildren())
      child.QueueFree();
  }
}
