using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;
using VVardenfell.Runtime.Combat;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
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
        {
            var availability = GetContinueAvailability();
            if (!availability.Available)
            {
                error = availability.Reason;
                return false;
            }

            return TryRequest<ContinueGameInitializationSingleton>("VVardenfell.ContinueInitialization", out error);
        }

        public static bool TryRequestLoadGame(string slotId, out string error)
        {
            var availability = GetLoadGameAvailability();
            if (!availability.Available)
            {
                error = availability.Reason;
                return false;
            }

            return TryRequestLoad(slotId, out error);
        }

        public static RequestAvailability GetNewGameAvailability()
        {
            return TryGetInitializationPayload(out _, out string error)
                ? new RequestAvailability(true, string.Empty)
                : new RequestAvailability(false, error);
        }

        public static RequestAvailability GetContinueAvailability()
        {
            if (TryGetInitializationPayload(out var payload, out _)
                && BootstrapRuntimeModeUtility.IsSandboxMode((BootstrapRuntimeMode)payload.RuntimeMode))
                return new RequestAvailability(false, "Continue is unavailable in sandbox mode.");

            if (!WorldSaveStorage.TryGetContinueAvailability(out string saveError))
                return new RequestAvailability(false, saveError);

            return new RequestAvailability(true, string.Empty);
        }

        public static RequestAvailability GetLoadGameAvailability()
        {
            if (TryGetInitializationPayload(out var payload, out _)
                && BootstrapRuntimeModeUtility.IsSandboxMode((BootstrapRuntimeMode)payload.RuntimeMode))
                return new RequestAvailability(false, "Load Game is unavailable in sandbox mode.");

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
            MorrowindCombatSettingsBridge.PublishPersisted(em);

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
                SlotId = RuntimeFixedStringUtility.ToFixed128OrDefault(slotId),
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
                MorrowindCombatSettingsBridge.PublishPersisted(em);
                error = null;
                return true;
            }

            bool hasSerializedSavePayload = WorldSaveStorage.TryGetContinueAvailability(out string saveStatus);
            ResolveInitialPlayerData(
                RuntimeContentDatabase.Active,
                out var playerStats,
                out var playerIdentity,
                out var knownSpells,
                out var initialInventory);
            var initEntity = em.CreateEntity();
            em.SetName(initEntity, "VVardenfell.GameInitialization");
            em.AddComponentData(initEntity, new GameInitializationSingleton
            {
                PlayerSettings = BootstrapController.ResolvePlayerMovementSettings(),
                PlayerActorStats = playerStats,
                PlayerIdentity = playerIdentity,
                PlayerCrime = PlayerCrimeState.Default,
                PlayerPosition = WorldBootstrap.DefaultPlayerSpawnPosition(),
                PlayerRotation = quaternion.identity,
                PlayerPitchDegrees = 0f,
                RuntimeMode = (byte)BootstrapController.CurrentRuntimeMode,
                SpawnLocalPlayer = (byte)(ShouldSpawnLocalPlayerForCurrentMode() ? 1 : 0),
                HasSerializedSavePayload = hasSerializedSavePayload,
                SerializedSavePayloadStatus = RuntimeFixedStringUtility.ToFixed128OrDefault(hasSerializedSavePayload ? string.Empty : saveStatus ?? string.Empty),
            });
            PopulateInitializationSpellbook(em.AddBuffer<ActorKnownSpell>(initEntity), knownSpells);
            PopulateInitializationInventory(em.AddBuffer<PlayerInitialInventoryItem>(initEntity), initialInventory);
            MorrowindCombatSettingsBridge.PublishPersisted(em);
            error = null;
            return true;
        }

        static bool ShouldSpawnLocalPlayerForCurrentMode()
        {
            var mode = BootstrapController.CurrentRuntimeMode;
            if (!BootstrapRuntimeModeUtility.IsSandboxMode(mode))
                return true;

            var profile = SandboxWorldFixtures.Active;
            return profile?.SpawnLocalPlayer ?? true;
        }

        static void ResolveInitialPlayerData(
            RuntimeContentDatabase contentDb,
            out ActorRuntimeStatSeed stats,
            out ActorIdentitySet identity,
            out ActorKnownSpell[] knownSpells,
            out PlayerInitialInventoryItem[] initialInventory)
        {
            if (MorrowindActorMovementStats.TryCreatePlayerSeedFromContent(
                    contentDb,
                    out stats,
                    out identity,
                    out knownSpells,
                    out initialInventory))
            {
                return;
            }

            stats = MorrowindActorMovementStats.CreateDefaultPlayerSeed();
            identity = ActorIdentitySet.DefaultPlayer();
            knownSpells = System.Array.Empty<ActorKnownSpell>();
            initialInventory = System.Array.Empty<PlayerInitialInventoryItem>();
        }

        static void PopulateInitializationSpellbook(DynamicBuffer<ActorKnownSpell> buffer, ActorKnownSpell[] knownSpells)
        {
            if (knownSpells == null)
                return;

            for (int i = 0; i < knownSpells.Length; i++)
            {
                if (knownSpells[i].Spell.IsValid)
                    buffer.Add(knownSpells[i]);
            }
        }

        static void PopulateInitializationInventory(DynamicBuffer<PlayerInitialInventoryItem> buffer, PlayerInitialInventoryItem[] inventory)
        {
            if (inventory == null)
                return;

            for (int i = 0; i < inventory.Length; i++)
            {
                if (inventory[i].Count > 0 && inventory[i].Content.IsValid)
                    buffer.Add(inventory[i]);
            }
        }

    }
}
