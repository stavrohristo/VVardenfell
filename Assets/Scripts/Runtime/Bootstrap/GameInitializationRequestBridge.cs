using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Movement;
using VVardenfell.Runtime.Player;
using VVardenfell.Runtime.Streaming;
using VVardenfell.Runtime.WorldState;

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

        public static bool TryRequestLoadGame(string slotId, out string error)
            => TryRequestLoad(slotId, out error);

        public static RequestAvailability GetNewGameAvailability()
        {
            return TryGetInitializationPayload(out _, out string error)
                ? new RequestAvailability(true, string.Empty)
                : new RequestAvailability(false, error);
        }

        public static RequestAvailability GetContinueAvailability()
        {
            if (!WorldSaveStorage.TryGetContinueAvailability(out string saveError))
                return new RequestAvailability(false, saveError);

            return new RequestAvailability(true, string.Empty);
        }

        public static RequestAvailability GetLoadGameAvailability()
        {
            var slots = WorldSaveStorage.EnumerateSlots();
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].IsValid)
                    return new RequestAvailability(true, string.Empty);
            }

            return new RequestAvailability(false, "No save slots are available.");
        }

        static bool TryRequest<T>(string entityName, out string error) where T : unmanaged, IComponentData
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            var em = world.EntityManager;
            if (!EnsureInitializationPayload(em, out error))
                return false;

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

        static bool TryRequestLoad(string slotId, out string error)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated)
            {
                error = "Default ECS world is not ready.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(slotId))
            {
                error = "No save slot is selected.";
                return false;
            }

            var em = world.EntityManager;
            if (!EnsureInitializationPayload(em, out error))
                return false;

            using var requestQuery = em.CreateEntityQuery(ComponentType.ReadOnly<LoadGameInitializationSingleton>());
            Entity entity = requestQuery.IsEmptyIgnoreFilter
                ? em.CreateEntity()
                : requestQuery.GetSingletonEntity();
            em.SetName(entity, "VVardenfell.LoadGameInitialization");
            if (!em.HasComponent<LoadGameInitializationSingleton>(entity))
                em.AddComponentData(entity, new LoadGameInitializationSingleton());

            em.SetComponentData(entity, new LoadGameInitializationSingleton
            {
                SlotId = ToFixed128(slotId),
            });

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
                if (!EnsureInitializationPayload(em, out error))
                    return false;
            }

            payload = payloadQuery.GetSingleton<GameInitializationSingleton>();
            error = null;
            return true;
        }

        static bool EnsureInitializationPayload(EntityManager em, out string error)
        {
            using var payloadQuery = em.CreateEntityQuery(ComponentType.ReadOnly<GameInitializationSingleton>());
            if (!payloadQuery.IsEmptyIgnoreFilter)
            {
                error = null;
                return true;
            }

            bool hasSerializedSavePayload = WorldSaveStorage.TryGetContinueAvailability(out string saveStatus);
            var initEntity = em.CreateEntity();
            em.SetName(initEntity, "VVardenfell.GameInitialization");
            em.AddComponentData(initEntity, new GameInitializationSingleton
            {
                PlayerSettings = BootstrapController.ResolvePlayerMovementSettings(),
                PlayerActorStats = MorrowindActorMovementStats.CreateDefaultPlayerSeed(),
                PlayerIdentity = ActorIdentitySet.DefaultPlayer(),
                PlayerPosition = WorldBootstrap.DefaultPlayerSpawnPosition(),
                PlayerRotation = quaternion.identity,
                PlayerPitchDegrees = 0f,
                HasSerializedSavePayload = hasSerializedSavePayload,
                SerializedSavePayloadStatus = ToFixed128(hasSerializedSavePayload ? string.Empty : saveStatus ?? string.Empty),
            });
            em.AddBuffer<PlayerKnownSpell>(initEntity);
            error = null;
            return true;
        }

        static FixedString128Bytes ToFixed128(string value)
        {
            if (string.IsNullOrEmpty(value))
                return default;

            var result = default(FixedString128Bytes);
            result.CopyFromTruncated(value);
            return result;
        }
    }
}
