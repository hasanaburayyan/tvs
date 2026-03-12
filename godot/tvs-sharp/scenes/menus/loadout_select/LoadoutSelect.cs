using Godot;
using SpacetimeDB.Types;
using System.Linq;

public partial class LoadoutSelect : PopulableMenu
{
  [Export]
  public Hud hud;

  private VBoxContainer _weaponList;
  private VBoxContainer _skillList;
  private Button _confirmButton;
  private Label _selectedWeaponLabel;
  private Label _selectedSkillLabel;

  private ulong? _selectedWeaponId;
  private ulong? _selectedSkillId;

  public override void _Ready()
  {
    _weaponList = GetNode<VBoxContainer>("%WeaponList");
    _skillList = GetNode<VBoxContainer>("%SkillList");
    _confirmButton = GetNode<Button>("%ConfirmButton");
    _selectedWeaponLabel = GetNode<Label>("%SelectedWeaponLabel");
    _selectedSkillLabel = GetNode<Label>("%SelectedSkillLabel");

    _confirmButton.Pressed += OnConfirmPressed;
  }

  public override void Populate()
  {
    _selectedWeaponId = null;
    _selectedSkillId = null;
    UpdateSelectionLabels();
    PopulateWeapons();
    PopulateSkills();
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
      btn.Pressed += () => SelectWeapon(id, weapon.Name);
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
      var btn = new Button();
      btn.Text = $"{skill.Name}\n{skill.Description}";
      btn.ClipText = true;
      btn.CustomMinimumSize = new Vector2(0, 48);
      var id = skill.Id;
      btn.Pressed += () => SelectSkill(id, skill.Name);
      _skillList.AddChild(btn);
    }
  }

  private void SelectWeapon(ulong id, string name)
  {
    _selectedWeaponId = id;
    UpdateSelectionLabels();
  }

  private void SelectSkill(ulong id, string name)
  {
    _selectedSkillId = id;
    UpdateSelectionLabels();
  }

  private void UpdateSelectionLabels()
  {
    var conn = SpacetimeNetworkManager.Instance?.Conn;

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
      _selectedSkillLabel.Text = s != null ? $"Skill: {s.Name}" : "Skill: ???";
    }
    else
    {
      _selectedSkillLabel.Text = "Skill: (none)";
    }

    _confirmButton.Disabled = _selectedWeaponId == null || _selectedSkillId == null;
  }

  private void OnConfirmPressed()
  {
    if (_selectedWeaponId == null || _selectedSkillId == null) return;

    var conn = SpacetimeNetworkManager.Instance?.Conn;
    if (conn == null) return;

    conn.Reducers.OnSetLoadout += OnSetLoadoutResult;
    conn.Reducers.SetLoadout(hud.sessionID, _selectedWeaponId.Value, _selectedSkillId.Value);
    _confirmButton.Disabled = true;
  }

  private void OnSetLoadoutResult(ReducerEventContext ctx, ulong gameId, ulong weaponDefId, ulong skillDefId)
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

    hud.EmitSignal(Hud.SignalName.StartLobby, (long)hud.sessionID);
    hud.CloseMenus();
  }

  private static void ClearList(Control container)
  {
    foreach (var child in container.GetChildren())
      child.QueueFree();
  }
}
