using SpacetimeDB;

public struct SeededRng
{
    private uint state;
    public SeededRng(uint seed) { state = seed == 0 ? 1 : seed; }

    public uint Next()
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    public float Range(float min, float max)
    {
        return min + (Next() % 10000) / 10000f * (max - min);
    }

    public int RangeInt(int min, int max)
    {
        return min + (int)(Next() % (uint)(max - min));
    }
}

public static partial class Module
{
    const float MapSize = 200f;
    const float MapHalf = MapSize / 2f;

    public static void GenerateMap(ReducerContext ctx, ulong gameSessionId, uint seed)
    {
        var rng = new SeededRng(seed);

        // --- Team A command center (rear, z: -95 to -75) ---
        ctx.Db.terrain_feature.Insert(new TerrainFeature
        {
            Id = 0,
            GameSessionId = gameSessionId,
            Type = TerrainType.CommandCenter,
            PosX = rng.Range(-30f, 30f),
            PosY = 0f,
            PosZ = rng.Range(-95f, -75f),
            SizeX = 12f,
            SizeY = 6f,
            SizeZ = 8f,
            RotationY = 0f,
            TeamIndex = 1,
        });

        // --- Team B command center (mirrored, z: 75 to 95) ---
        ctx.Db.terrain_feature.Insert(new TerrainFeature
        {
            Id = 0,
            GameSessionId = gameSessionId,
            Type = TerrainType.CommandCenter,
            PosX = rng.Range(-30f, 30f),
            PosY = 0f,
            PosZ = rng.Range(75f, 95f),
            SizeX = 12f,
            SizeY = 6f,
            SizeZ = 8f,
            RotationY = 180f,
            TeamIndex = 2,
        });

        // --- Team A trenches (z: -70 to -30) ---
        int trenchCountA = rng.RangeInt(2, 5);
        for (int i = 0; i < trenchCountA; i++)
        {
            ctx.Db.terrain_feature.Insert(new TerrainFeature
            {
                Id = 0,
                GameSessionId = gameSessionId,
                Type = TerrainType.Trench,
                PosX = rng.Range(-80f, 80f),
                PosY = -1f,
                PosZ = rng.Range(-65f, -35f),
                SizeX = rng.Range(15f, 40f),
                SizeY = 2f,
                SizeZ = 3f,
                RotationY = rng.Range(-15f, 15f),
                TeamIndex = 1,
            });
        }

        // --- Team B trenches (z: 30 to 70, mirrored) ---
        int trenchCountB = rng.RangeInt(2, 5);
        for (int i = 0; i < trenchCountB; i++)
        {
            ctx.Db.terrain_feature.Insert(new TerrainFeature
            {
                Id = 0,
                GameSessionId = gameSessionId,
                Type = TerrainType.Trench,
                PosX = rng.Range(-80f, 80f),
                PosY = -1f,
                PosZ = rng.Range(35f, 65f),
                SizeX = rng.Range(15f, 40f),
                SizeY = 2f,
                SizeZ = 3f,
                RotationY = rng.Range(-15f, 15f),
                TeamIndex = 2,
            });
        }

        // --- No-man's land trees (z: -30 to 30) ---
        int treeCount = rng.RangeInt(5, 16);
        for (int i = 0; i < treeCount; i++)
        {
            ctx.Db.terrain_feature.Insert(new TerrainFeature
            {
                Id = 0,
                GameSessionId = gameSessionId,
                Type = TerrainType.Tree,
                PosX = rng.Range(-90f, 90f),
                PosY = 0f,
                PosZ = rng.Range(-30f, 30f),
                SizeX = 2f,
                SizeY = rng.Range(4f, 8f),
                SizeZ = 2f,
                RotationY = rng.Range(0f, 360f),
                TeamIndex = 0,
            });
        }

        // --- No-man's land walls (z: -30 to 30) ---
        int wallCount = rng.RangeInt(2, 6);
        for (int i = 0; i < wallCount; i++)
        {
            ctx.Db.terrain_feature.Insert(new TerrainFeature
            {
                Id = 0,
                GameSessionId = gameSessionId,
                Type = TerrainType.Wall,
                PosX = rng.Range(-80f, 80f),
                PosY = 0f,
                PosZ = rng.Range(-25f, 25f),
                SizeX = rng.Range(4f, 10f),
                SizeY = rng.Range(2f, 4f),
                SizeZ = 1f,
                RotationY = rng.Range(-30f, 30f),
                TeamIndex = 0,
            });
        }

        // --- Buildings (one per side, near trenches) ---
        ctx.Db.terrain_feature.Insert(new TerrainFeature
        {
            Id = 0,
            GameSessionId = gameSessionId,
            Type = TerrainType.Building,
            PosX = rng.Range(-70f, 70f),
            PosY = 0f,
            PosZ = rng.Range(-60f, -40f),
            SizeX = 8f,
            SizeY = 5f,
            SizeZ = 6f,
            RotationY = rng.Range(-10f, 10f),
            TeamIndex = 1,
        });

        ctx.Db.terrain_feature.Insert(new TerrainFeature
        {
            Id = 0,
            GameSessionId = gameSessionId,
            Type = TerrainType.Building,
            PosX = rng.Range(-70f, 70f),
            PosY = 0f,
            PosZ = rng.Range(40f, 60f),
            SizeX = 8f,
            SizeY = 5f,
            SizeZ = 6f,
            RotationY = rng.Range(-10f, 10f),
            TeamIndex = 2,
        });
    }
}