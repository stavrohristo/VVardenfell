using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Content;
using VVardenfell.Runtime.MorrowindScript;

namespace VVardenfell.Runtime.Combat
{
    public static class MorrowindCombatAudioUtility
    {
        public static void EmitRequiredSound(
            RuntimeContentDatabase contentDb,
            string soundId,
            Entity sourceEntity,
            uint sourcePlacedRefId,
            float3 position,
            float volume,
            float pitch,
            ref InteractionAudioRequestState audioState,
            bool hasAudioState,
            ref EntityCommandBuffer ecb)
        {
            if (!hasAudioState)
                throw new InvalidOperationException($"[VVardenfell][Combat] Required combat sound '{soundId}' cannot play without InteractionAudioRequestState.");
            if (!contentDb.TryGetSoundHandle(soundId, out var sound) || !sound.IsValid)
                throw new InvalidOperationException($"[VVardenfell][Combat] Required combat sound '{soundId}' is missing.");

            audioState.NextSequence++;
            Entity requestEntity = ecb.CreateEntity();
            ecb.AddComponent(requestEntity, new MorrowindScriptAudioRequest
            {
                Sequence = audioState.NextSequence,
                Sound = sound,
                SourceEntity = sourceEntity,
                SourcePlacedRefId = sourcePlacedRefId,
                Position = position,
                Volume = volume,
                Pitch = pitch,
                Kind = (byte)MorrowindScriptAudioKind.PlaySound3DVP,
                Spatial = 1,
                Looping = 0,
            });
        }
    }
}
