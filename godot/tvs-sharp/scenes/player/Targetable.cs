using Godot;
using SpacetimeDB.Types;

public partial class Targetable : Node
{
  public EntityType Type { get; set; }
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
