using Godot;

public partial class FreelookCamera : Camera3D
{
  [Export(PropertyHint.Range, "0.0,1.0")]
  public float Sensitivity = 0.25f;

  public bool CameraLocked { get; private set; }
  public bool MenuOpen { get; set; }

  private Vector2 _mousePosition = Vector2.Zero;
  private float _totalPitch = 0.0f;
  private bool _inGame;

  public void SetCameraLocked(bool locked)
  {
    CameraLocked = locked;
    if (locked && !MenuOpen)
      Input.MouseMode = Input.MouseModeEnum.Captured;
    else
      Input.MouseMode = Input.MouseModeEnum.Visible;
  }

  public void SetInGame(bool inGame)
  {
    _inGame = inGame;
    if (!inGame)
      SetCameraLocked(false);
  }

  public override void _Input(InputEvent @event)
  {
    if (!Current) return;

    if (@event is InputEventMouseMotion mouseMotion)
    {
      _mousePosition = mouseMotion.Relative;
    }
    else if (@event is InputEventMouseButton mouseButton)
    {
      if (!CameraLocked)
      {
        switch (mouseButton.ButtonIndex)
        {
          case MouseButton.Right:
            Input.MouseMode = mouseButton.Pressed
              ? Input.MouseModeEnum.Captured
              : Input.MouseModeEnum.Visible;
            break;
        }
      }

      switch (mouseButton.ButtonIndex)
      {
        case MouseButton.WheelUp:
          Sensitivity = Mathf.Clamp(Sensitivity * 1.1f, 0.05f, 1.0f);
          break;
        case MouseButton.WheelDown:
          Sensitivity = Mathf.Clamp(Sensitivity / 1.1f, 0.05f, 1.0f);
          break;
      }
    }
    else if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.IsEcho())
    {
      if (keyEvent.Keycode == Key.V && _inGame && !MenuOpen)
        SetCameraLocked(!CameraLocked);
    }
  }

  public override void _Process(double delta)
  {
    if (!Current) return;

    if (CameraLocked && !MenuOpen && Input.MouseMode != Input.MouseModeEnum.Captured)
      Input.MouseMode = Input.MouseModeEnum.Captured;

    UpdateMouselook();
  }

  private void UpdateMouselook()
  {
    if (Input.MouseMode != Input.MouseModeEnum.Captured) return;

    var scaled = _mousePosition * Sensitivity;
    float yaw = scaled.X;
    float pitch = scaled.Y;
    _mousePosition = Vector2.Zero;

    pitch = Mathf.Clamp(pitch, -90f - _totalPitch, 90f - _totalPitch);
    _totalPitch += pitch;

    var player = GetOwner<Node3D>();
    player?.RotateY(Mathf.DegToRad(-yaw));
    RotateObjectLocal(Vector3.Right, Mathf.DegToRad(-pitch));
  }
}
