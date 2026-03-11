using Godot;
using System;

public partial class ProfileSelect : PopulableMenu
{
  [Export]
  public Hud hud;

  private VBoxContainer _profileListContainer;
  private LineEdit _newProfileInput;
  private Button _newProfileButton;

  public override void _Ready()
  {
    _profileListContainer = GetNode<VBoxContainer>("%ProfileListContainer");
    _newProfileInput = GetNode<LineEdit>("%NewProfileInput");
    _newProfileButton = GetNode<Button>("%NewProfileButton");

    _newProfileButton.Pressed += OnNewProfilePressed;
  }

  public override void Populate()
  {
    ClearProfileList();

    var profiles = SpacetimeNetworkManager.GetSavedProfiles();
    foreach (var profile in profiles)
    {
      AddProfileButton(profile);
    }
  }

  void ClearProfileList()
  {
    foreach (var child in _profileListContainer.GetChildren())
    {
      _profileListContainer.RemoveChild(child);
      child.QueueFree();
    }
  }

  void AddProfileButton(string profile)
  {
    var button = new Button();
    button.Text = profile;
    button.Pressed += () => SelectProfile(profile);
    _profileListContainer.AddChild(button);
  }

  void OnNewProfilePressed()
  {
    var name = _newProfileInput.Text.Trim();
    if (string.IsNullOrEmpty(name)) return;
    _newProfileInput.Text = "";
    SelectProfile(name);
  }

  void SelectProfile(string profile)
  {
    SetButtonsDisabled(true);
    SpacetimeNetworkManager.Instance.Connect(profile);
  }

  void SetButtonsDisabled(bool disabled)
  {
    _newProfileButton.Disabled = disabled;
    foreach (var child in _profileListContainer.GetChildren())
    {
      if (child is Button btn)
        btn.Disabled = disabled;
    }
  }
}
