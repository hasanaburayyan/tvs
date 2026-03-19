using Godot;

public partial class Targetable : Node
{
  public ulong GamePlayerId { get; set; }
  public ulong SoldierId { get; set; }

  public bool IsSoldier => SoldierId != 0;

  public static Targetable FindIn(Node node)
  {
	if (node == null) return null;

	var current = node;
	while (current != null)
	{
	  foreach (var child in current.GetChildren())
	  {
		if (child is Targetable t) return t;
	  }
	  current = current.GetParent();
	}
	return null;
  }
}
