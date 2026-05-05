using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Player;

namespace VVardenfell.Runtime.Inventory
{
    public struct MorrowindResolvedLeveledItem
    {
        public ContentReference Content;
        public int Count;
    }

    public static class MorrowindLeveledItemResolverUtility
    {
        const int MaxLeveledResolutionDepth = 16;
        const int ItemLeveledEachFlag = 0x01;
        const int ItemLeveledAllLevelsFlag = 0x02;

        public static int ResolvePlayerLevel(EntityManager entityManager)
        {
            using var query = entityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerTag>(),
                ComponentType.ReadOnly<ActorIdentitySet>());
            if (query.IsEmptyIgnoreFilter)
                return 1;

            var player = query.GetSingletonEntity();
            return math.max(1, entityManager.GetComponentData<ActorIdentitySet>(player).Level);
        }

        public static uint BuildResolutionSeed(uint sourceSeed, int authoredEntryIndex, int iteration)
        {
            return math.hash(new uint4(
                sourceSeed,
                unchecked((uint)authoredEntryIndex + 1u),
                unchecked((uint)iteration + 1u),
                0x9E3779B9u));
        }

        public static bool TryResolveDirectCarryableByIdHash(
            ref RuntimeContentBlob contentBlob,
            ulong idHash,
            out ContentReference content)
        {
            content = default;
            if (idHash == 0UL)
                return false;

            if (!RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref contentBlob, idHash, out var resolved))
                return false;

            if (resolved.Kind == ContentReferenceKind.Item || resolved.Kind == ContentReferenceKind.Light)
            {
                content = resolved;
                return true;
            }

            if (resolved.Kind == ContentReferenceKind.LeveledItem)
                return false;

            throw new InvalidOperationException($"[VVardenfell][LeveledItem] Resolved unsupported carryable target {Describe(resolved)} for hash {idHash}.");
        }

        public static bool TryResolveOne(
            ref RuntimeContentBlob contentBlob,
            ItemLeveledListDefHandle listHandle,
            int playerLevel,
            uint seed,
            out ContentReference content)
        {
            var visited = new NativeList<int>(Allocator.Temp);
            uint state = seed == 0u ? 0xA341316Cu : seed;
            try
            {
                return TryResolveOne(ref contentBlob, listHandle, math.max(1, playerLevel), ref state, 0, ref visited, out content);
            }
            finally
            {
                if (visited.IsCreated)
                    visited.Dispose();
            }
        }

        public static void ResolveIntoInventory(
            ref RuntimeContentBlob contentBlob,
            ItemLeveledListDefHandle listHandle,
            int playerLevel,
            uint seed,
            int count,
            NativeList<MorrowindResolvedLeveledItem> results)
        {
            if (count == int.MinValue)
                throw new InvalidOperationException("[VVardenfell][LeveledItem] Cannot resolve int.MinValue leveled item count.");
            int absoluteCount = math.abs(count);
            if (absoluteCount == 0)
                return;
            if (!listHandle.IsValid)
                throw new InvalidOperationException("[VVardenfell][LeveledItem] Cannot resolve an invalid leveled item handle.");
            if (!results.IsCreated)
                throw new InvalidOperationException("[VVardenfell][LeveledItem] ResolveIntoInventory requires a created result list.");

            ref RuntimeItemLeveledListDefBlob list = ref RuntimeContentBlobUtility.Get(ref contentBlob, listHandle);
            int iterations = ((list.Flags & ItemLeveledEachFlag) != 0 && absoluteCount > 1)
                ? absoluteCount
                : 1;
            int resultCount = iterations == 1 ? absoluteCount : 1;

            for (int i = 0; i < iterations; i++)
            {
                uint itemSeed = iterations == 1 ? seed : MixSeed(seed, unchecked((uint)i + 1u));
                if (!TryResolveOne(ref contentBlob, listHandle, playerLevel, itemSeed, out var content) || !content.IsValid)
                    continue;

                results.Add(new MorrowindResolvedLeveledItem
                {
                    Content = content,
                    Count = resultCount,
                });
            }
        }

        static bool TryResolveOne(
            ref RuntimeContentBlob contentBlob,
            ItemLeveledListDefHandle listHandle,
            int playerLevel,
            ref uint state,
            int depth,
            ref NativeList<int> visited,
            out ContentReference content)
        {
            content = default;
            if (!listHandle.IsValid)
                throw new InvalidOperationException("[VVardenfell][LeveledItem] Cannot resolve an invalid leveled item handle.");
            if (depth >= MaxLeveledResolutionDepth)
                throw new InvalidOperationException($"[VVardenfell][LeveledItem] Item leveled-list recursion cap reached at depth {MaxLeveledResolutionDepth}.");

            for (int i = 0; i < visited.Length; i++)
            {
                if (visited[i] == listHandle.Value)
                    throw new InvalidOperationException($"[VVardenfell][LeveledItem] Item leveled-list cycle detected at handle {listHandle.Value}.");
            }

            visited.Add(listHandle.Value);
            ref RuntimeItemLeveledListDefBlob list = ref RuntimeContentBlobUtility.Get(ref contentBlob, listHandle);
            RuntimeContentBlobUtility.RequireRange(list.FirstEntryIndex, list.EntryCount, contentBlob.ItemLeveledListEntries.Length, "item leveled-list entry");
            try
            {
                if (RollPercent(ref state) < list.ChanceNone || list.EntryCount == 0)
                    return false;

                int highestEligibleLevel = 0;
                bool hasEligible = false;
                for (int i = 0; i < list.EntryCount; i++)
                {
                    int level = contentBlob.ItemLeveledListEntries[list.FirstEntryIndex + i].Level;
                    if (level > highestEligibleLevel && level <= playerLevel)
                    {
                        highestEligibleLevel = level;
                        hasEligible = true;
                    }
                }

                if (!hasEligible)
                    return false;

                bool allLevels = (list.Flags & ItemLeveledAllLevelsFlag) != 0;
                Span<int> candidateEntryOffsets = stackalloc int[list.EntryCount];
                int candidateCount = 0;
                for (int i = 0; i < list.EntryCount; i++)
                {
                    int level = contentBlob.ItemLeveledListEntries[list.FirstEntryIndex + i].Level;
                    if (level > playerLevel)
                        continue;
                    if (allLevels || level == highestEligibleLevel)
                        candidateEntryOffsets[candidateCount++] = i;
                }

                if (candidateCount == 0)
                    return false;

                int candidateIndex = NextRandomIndex(ref state, candidateCount);
                ref RuntimeItemLeveledListEntryDefBlob selected = ref contentBlob.ItemLeveledListEntries[list.FirstEntryIndex + candidateEntryOffsets[candidateIndex]];
                if (selected.ItemIdHash == 0UL)
                    throw new InvalidOperationException($"[VVardenfell][LeveledItem] Leveled-list hash {list.IdHash} has an entry with no item id.");

                if (RuntimeContentBlobUtility.TryResolvePlaceableByIdHash(ref contentBlob, selected.ItemIdHash, out content))
                {
                    if (content.Kind == ContentReferenceKind.Item || content.Kind == ContentReferenceKind.Light)
                        return true;
                    if (content.Kind != ContentReferenceKind.LeveledItem)
                        throw new InvalidOperationException($"[VVardenfell][LeveledItem] Leveled-list hash {list.IdHash} selected unsupported target {Describe(content)}.");
                }

                if (!RuntimeContentBlobUtility.TryGetItemLeveledListHandleByIdHash(ref contentBlob, selected.ItemIdHash, out var nestedHandle))
                    throw new InvalidOperationException($"[VVardenfell][LeveledItem] Missing leveled-list target hash {selected.ItemIdHash} referenced by list hash {list.IdHash}.");

                return TryResolveOne(
                    ref contentBlob,
                    nestedHandle,
                    playerLevel,
                    ref state,
                    depth + 1,
                    ref visited,
                    out content);
            }
            finally
            {
                if (visited.Length > 0)
                    visited.RemoveAt(visited.Length - 1);
            }
        }

        static uint MixSeed(uint seed, uint salt)
        {
            return math.hash(new uint2(seed, salt));
        }

        static int RollPercent(ref uint state)
        {
            state = state * 1664525u + 1013904223u;
            return (int)(state % 100u);
        }

        static int NextRandomIndex(ref uint seed, int count)
        {
            seed = seed == 0u ? 0xC8013EA4u : seed;
            seed = seed * 1664525u + 1013904223u;
            return count <= 1 ? 0 : (int)(seed % (uint)count);
        }

        static string Describe(ContentReference content)
            => $"{content.Kind}:{content.HandleValue}";
    }
}
