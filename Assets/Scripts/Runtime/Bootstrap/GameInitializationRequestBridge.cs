using Unity.Entities;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Bootstrap
{
    public static class GameInitializationRequestBridge
    {
        public readonly struct RequestAvailability
        {
            public RequestAvailability(bool available, string reason)
            {
                Available = available;
                Reason = reason ?? string.Empty;
            }

            public bool Available { get; }
            public string Reason { get; }
        }

        public static bool CanRequestNewGame(out string error)
        {
            var availability = GetNewGameAvailability();
            error = availability.Reason;
            return availability.Available;
        }

        public static bool CanRequestContinue(out string error)
        {
            var availability = GetContinueAvailability();
            error = availability.Reason;
            return availability.Available;
        }

        public static bool CanRequestLoadGame(out string error)
        {
            var availability = GetLoadGameAvailability();
            error = availability.Reason;
            return availability.Available;
        }

        public static bool TryRequestNewGame(out string error)
            => TryRequest<NewGameInitializationSingleton>("VVardenfell.NewGameInitialization", out error);

        public static bool TryRequestContinue(out string error)
            => TryRequest<ContinueGameInitializationSingleton>("VVardenfell.ContinueInitialization", out error);

        public static RequestAvailability GetNewGameAvailability()
        {
            return TryGetInitializationPayload(out _, out string error)
                ? new RequestAvailability(true, string.Empty)
                : new RequestAvailability(false, error);
        }

        public static RequestAvailability GetContinueAvailability()
        {
            if (!TryGetInitializationPayload(out var payload, out string error))
                return new RequestAvailability(false, error);

            if (!payload.HasSerializedSavePayload)
                return new RequestAvailability(false, payload.SerializedSavePayloadStatus.ToString());

            return new RequestAvailability(true, string.Empty);
        }

        public static RequestAvailability GetLoadGameAvailability()
            => new(false, "Load Game belongs to the future Save/Load milestone and is not wired into runtime bootstrap yet.");

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
