using Godot;

public partial class CaptureFlag : Node3D
{
	public ulong PointId;

	private MeshInstance3D _flag;
	private MeshInstance3D _circle;
	private StandardMaterial3D _flagMat;
	private StandardMaterial3D _circleMat;

	private byte _pendingTeam;
	private int _pendingInf1;
	private int _pendingInf2;
	private int _pendingMax;
	private bool _ready;

	private static readonly Color NeutralColor = new(1f, 1f, 1f);
	private static readonly Color EntenteColor = new(0.3f, 0.5f, 1f);
	private static readonly Color CentralColor = new(1f, 0.3f, 0.3f);

	public override void _Ready()
	{
		_flag = GetNode<MeshInstance3D>("Flag");
		_circle = GetNode<MeshInstance3D>("CaptureRadius");

		_flagMat = new StandardMaterial3D
		{
			AlbedoColor = NeutralColor,
		};
		_flag.SetSurfaceOverrideMaterial(0, _flagMat);

		_circleMat = new StandardMaterial3D
		{
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			AlbedoColor = new Color(1f, 1f, 1f, 0.18f),
		};
		_circle.SetSurfaceOverrideMaterial(0, _circleMat);

		_ready = true;
		ApplyColors();
	}

	public void SetOwningTeam(byte team)
	{
		_pendingTeam = team;
		if (_ready) ApplyColors();
	}

	public void SetInfluence(int inf1, int inf2, int max)
	{
		_pendingInf1 = inf1;
		_pendingInf2 = inf2;
		_pendingMax = max;
		if (_ready) ApplyColors();
	}

	private void ApplyColors()
	{
		Color flagColor = _pendingTeam switch
		{
			1 => EntenteColor,
			2 => CentralColor,
			_ => NeutralColor,
		};

		Color circleColor = NeutralColor;

		if (_pendingMax > 0)
		{
			float t1 = _pendingInf1 / (float)_pendingMax;
			float t2 = _pendingInf2 / (float)_pendingMax;

			if (t1 > t2 && t1 > 0)
			{
				circleColor = NeutralColor.Lerp(EntenteColor, t1);
				if (_pendingTeam == 0)
					flagColor = NeutralColor.Lerp(EntenteColor, t1);
			}
			else if (t2 > t1 && t2 > 0)
			{
				circleColor = NeutralColor.Lerp(CentralColor, t2);
				if (_pendingTeam == 0)
					flagColor = NeutralColor.Lerp(CentralColor, t2);
			}
		}

		_flagMat.AlbedoColor = flagColor;
		_circleMat.AlbedoColor = new Color(circleColor.R, circleColor.G, circleColor.B, 0.18f);
	}
}
