using Unity.Entities;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Bootstrap
{
    public static class GameInitializationRequestBridge
    {
        public static bool CanRequestNewGame(out string error)
            => TryGetInitializationPayload(out _, out error);

        public static bool CanRequestContinue(out string error)
        {
            if (!TryGetInitializationPayload(out var payload, out error))
                return false;

            if (!payload.HasSerializedSavePayload)
            {
                error = "No serialized save payload is available.";
                return false;
            }

            return true;
        }

        public static bool CanRequestLoadGame(out string error)
        {
            error = "Load Game is not wired into runtime bootstrap yet.";
            return false;
        }

        public static bool TryRequestNewGame(out string error)
            => TryRequest<NewGameInitializationSingleton>("VVardenfell.NewGameInitialization", out error);

        public static bool TryRequestContinue(out string error)
            => TryRequest<ContinueGameInitializationSingleton>("VVardenfell.ContinueInitialization", out error);

        static bool TryRequest<T>(string entityName, out string error) where T : unmanaged, IComponentData
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            var em = world.EntityManager;
            using var payloadQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GameInitializationSingleton>());
            if (payloadQuery.IsEmptyIgnoreFilter)
            {
                error = "Game initialization payload is not ready.";
                return false;
            }

            using var requestQuery = em.CreateEntityQuery(ComponentType.ReadOnly<T>());
            if (requestQuery.IsEmptyIgnoreFilter)
            {
                var entity = em.CreateEntity();
                em.SetName(entity, entityName);
                em.AddComponent<T>(entity);
            }

            error = null;
            return true;
        }

        static bool TryGetInitializationPayload(out GameInitializationSingleton payload, out string error)
        {
            payload = default;

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            var em = world.EntityManager;
            using var payloadQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GameInitializationSingleton>());
            if (payloadQuery.IsEmptyIgnoreFilter)
            {
                error = "Game initialization payload is not ready.";
                return false;
            }

            payload = payloadQuery.GetSingleton<GameInitializationSingleton>();
            error = null;
            return true;
        }
    }
}
