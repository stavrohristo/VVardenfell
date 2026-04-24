using Unity.Physics;

namespace VVardenfell.Runtime.Interactions
{
    static class InteractionCollisionLayers
    {
        public const uint Player = 1u << 1;
        public const uint ActivationProxy = 1u << 2;
        public const uint ActivationQuery = 1u << 3;
        public const uint SolidQuery = 1u << 4;

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
            CollidesWith = ~(Player | ActivationProxy),
            GroupIndex = 0,
        };

        public static CollisionFilter PlayerBodyFilter => new()
        {
            BelongsTo = Player,
            CollidesWith = ~(Player | ActivationProxy),
            GroupIndex = 0,
        };
    }
}
