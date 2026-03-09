[SpacetimeDB.Type]
public partial struct DbVector3
{
  public float x;
  public float y;
  public float z;

  public DbVector3(float x, float y, float z) {
    this.x = x;
    this.y = y;
    this.z = z;
  }

  public static DbVector3 operator +(DbVector3 a, DbVector3 b) => new DbVector3(a.x + b.x, a.y + b.y, a.z + b.z);
  public static DbVector3 operator -(DbVector3 a, DbVector3 b) => new DbVector3(a.x - b.x, a.y - b.y, a.z - b.z);
  public static DbVector3 operator *(DbVector3 a, float b) => new DbVector3(a.x * b, a.y * b, a.z * b);
  public static DbVector3 operator /(DbVector3 a, float b) => new DbVector3(a.x / b, a.y / b, a.z / b);
}