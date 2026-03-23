using SpacetimeDB;

public static partial class Module
{
    static Entity InsertTerrainEntity(ReducerContext ctx, ulong gameSessionId, TerrainType type,
        float posX, float posY, float posZ,
        float sizeX, float sizeY, float sizeZ,
        float rotationY, byte teamIndex,
        int health = 0, int maxHealth = 0, int armor = 0)
    {
        var ent = CreateEntity(ctx, gameSessionId, EntityType.Terrain, new DbVector3(posX, posY, posZ), rotationY, teamIndex);
        if (maxHealth > 0)
            CreateTargetable(ctx, ent.EntityId, health, maxHealth, armor);
        ctx.Db.terrain_feature.Insert(new TerrainFeature
        {
            EntityId = ent.EntityId,
            Type = type,
            SizeX = sizeX,
            SizeY = sizeY,
            SizeZ = sizeZ,
        });
        return ent;
    }

    public static void GenerateMap(ReducerContext ctx, ulong gameSessionId, ulong mapDefId)
    {
        foreach (var def in ctx.Db.map_terrain_def.MapDefId.Filter(mapDefId))
        {
            var ent = InsertTerrainEntity(ctx, gameSessionId, def.TerrainType,
                def.PositionX, def.PositionY, def.PositionZ,
                def.SizeX, def.SizeY, def.SizeZ,
                def.RotationY, def.TeamSlot,
                def.MaxHealth, def.MaxHealth, def.Armor);

            if (def.HasOutpostRegen)
                ScheduleOutpostRegen(ctx, ent.EntityId);

            if (def.TerrainType == TerrainType.CommandCenter)
            {
                ctx.Db.base_resource_store.Insert(new BaseResourceStore
                {
                    EntityId = ent.EntityId,
                    GameSessionId = gameSessionId,
                    TeamSlot = def.TeamSlot,
                    Supplies = HomeBaseSuppliesMax / 2,
                    SuppliesMax = HomeBaseSuppliesMax,
                    GenerationPerSecond = HomeBaseGenerationPerSecond,
                    Level = 1,
                });
            }
            else if (def.TerrainType == TerrainType.Outpost)
            {
                ctx.Db.base_resource_store.Insert(new BaseResourceStore
                {
                    EntityId = ent.EntityId,
                    GameSessionId = gameSessionId,
                    TeamSlot = def.TeamSlot,
                    Supplies = FobInitialStash,
                    SuppliesMax = FobSuppliesMax,
                    GenerationPerSecond = 0,
                    Level = 1,
                });
            }
        }

        foreach (var def in ctx.Db.map_capture_point_def.MapDefId.Filter(mapDefId))
        {
            InsertCapturePoint(ctx, gameSessionId,
                def.PositionX, def.PositionY, def.PositionZ,
                def.Radius, def.MaxInfluence);
        }

        ScheduleCaptureTick(ctx, gameSessionId);
        ScheduleLogisticsTick(ctx, gameSessionId);
    }

    static void InsertCapturePoint(ReducerContext ctx, ulong gameSessionId,
        float posX, float posY, float posZ, float radius, int maxInfluence)
    {
        var ent = CreateEntity(ctx, gameSessionId, EntityType.CapturePoint, new DbVector3(posX, posY, posZ), 0f, 0);
        ctx.Db.capture_point.Insert(new CapturePoint
        {
            EntityId = ent.EntityId,
            Radius = radius,
            OwningTeam = 0,
            InfluenceTeam1 = 0,
            InfluenceTeam2 = 0,
            MaxInfluence = maxInfluence,
        });
    }
}
