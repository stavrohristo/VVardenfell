using Unity.Physics;

namespace VVardenfell.Runtime.Interactions
{
    public static class InteractionCollisionLayers
    {
        public const uint Player = 1u << 1;
        public const uint ActivationProxy = 1u << 2;
        public const uint ActivationQuery = 1u << 3;
        public const uint SolidQuery = 1u << 4;
        public const uint Geometry = 1u << 5;
        public const uint DynamicRef = 1u << 6;
        public const uint LineOfSightQuery = 1u << 7;
        public const uint InteractionPick = 1u << 8;
        public const uint InteractionPickQuery = 1u << 9;
        public const uint Projectile = 1u << 10;

        public static CollisionFilter ActivationProxyFilter => new()
        {
            BelongsTo = ActivationProxy,
            CollidesWith = ActivationQuery,
            GroupIndex = 0,
        };

        public static CollisionFilter ActivationQueryFilter => new()
        {
            BelongsTo = ActivationQuery,
            CollidesWith = ActivationProxy,
            GroupIndex = 0,
        };

        public static CollisionFilter SolidQueryFilter => new()
        {
            BelongsTo = SolidQuery,
            CollidesWith = Geometry | DynamicRef,
            GroupIndex = 0,
        };

        public static CollisionFilter InteractionPickFilter => new()
        {
            BelongsTo = InteractionPick,
            CollidesWith = InteractionPickQuery,
            GroupIndex = 0,
        };

        public static CollisionFilter InteractionPickQueryFilter => new()
        {
            BelongsTo = InteractionPickQuery | ActivationQuery,
            CollidesWith = InteractionPick | ActivationProxy,
            GroupIndex = 0,
        };

        public static CollisionFilter InteractionExactPickQueryFilter => new()
        {
            BelongsTo = InteractionPickQuery,
            CollidesWith = InteractionPick,
            GroupIndex = 0,
        };

        public static CollisionFilter InteractionActivationProxyQueryFilter => new()
        {
            BelongsTo = ActivationQuery,
            CollidesWith = ActivationProxy,
            GroupIndex = 0,
        };

        public static CollisionFilter PlayerBodyFilter => new()
        {
            BelongsTo = Player,
            CollidesWith = Geometry | DynamicRef,
            GroupIndex = 0,
        };

        public static CollisionFilter GeometryFilter => new()
        {
            BelongsTo = Geometry,
            CollidesWith = Player | SolidQuery | LineOfSightQuery | DynamicRef,
            GroupIndex = 0,
        };

        public static CollisionFilter DynamicRefFilter => new()
        {
            BelongsTo = DynamicRef,
            CollidesWith = Player | SolidQuery | DynamicRef | Projectile,
            GroupIndex = 0,
        };

        public static CollisionFilter ProjectileFilter => new()
        {
            BelongsTo = Projectile,
            CollidesWith = Geometry | DynamicRef | Player | Projectile,
            GroupIndex = 0,
        };

        public static CollisionFilter LineOfSightQueryFilter => new()
        {
            BelongsTo = LineOfSightQuery,
            CollidesWith = Geometry,
            GroupIndex = 0,
        };
    }
}
