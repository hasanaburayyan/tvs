using Godot;

public enum TargetKind : byte
{
  Player,
  Soldier,
  TerrainFeature,
}

public partial class Targetable : Node
{
  public TargetKind Kind { get; set; }
  public ulong EntityId { get; set; }

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
