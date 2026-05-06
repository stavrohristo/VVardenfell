using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using VVardenfell.Core.Cache;
using VVardenfell.Runtime.Components;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.MorrowindScript
{
    [UpdateInGroup(typeof(MorrowindMenuMutationSystemGroup))]
    [UpdateAfter(typeof(MorrowindDialogueSessionSystem))]
    public partial struct MorrowindScriptStartRequestSystem : ISystem
    {
        EntityQuery _globalScriptQuery;

        public void OnCreate(ref SystemState systemState)
        {
            _globalScriptQuery = systemState.GetEntityQuery(
                ComponentType.ReadWrite<MorrowindGlobalScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptLocalValue>(),
                ComponentType.ReadWrite<MorrowindScriptStackValue>());
            systemState.RequireForUpdate<MorrowindScriptRuntimeState>();
            systemState.RequireForUpdate<MorrowindScriptStartRequest>();
            systemState.RequireForUpdate<RuntimeContentBlobReference>();
        }

        public void OnUpdate(ref SystemState systemState)
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = systemState.EntityManager.GetBuffer<MorrowindScriptStartRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            using var snapshot = requests.ToNativeArray(Allocator.Temp);
            requests.Clear();
            for (int i = 0; i < snapshot.Length; i++)
                ApplyRequest(ref systemState, ref contentBlob, snapshot[i]);
        }

        void ApplyRequest(ref SystemState systemState, ref RuntimeContentBlob contentBlob, in MorrowindScriptStartRequest request)
        {
            if (!request.Program.IsValid
                || (uint)request.ProgramIndex >= (uint)contentBlob.MorrowindScriptPrograms.Length
                || request.ProgramIndex != request.Program.Index)
            {
                throw new InvalidOperationException($"[VVardenfell][MWScript] invalid StartScript request programIndex={request.ProgramIndex}.");
            }

            ref var program = ref RuntimeContentBlobUtility.Get(ref contentBlob, request.Program);
            if (program.Status != (byte)MorrowindScriptProgramStatus.Compiled)
                throw new InvalidOperationException($"[VVardenfell][MWScript] StartScript requested disabled script hash {program.IdHash}: {RuntimeFixedStringUtility.ToFixed128OrDefault(ref program.DisabledReason)}");

            Entity existing = FindGlobalScriptEntity(request.ProgramIndex);
            if (existing != Entity.Null)
            {
                RestartExisting(ref systemState, ref contentBlob, existing, request);
                return;
            }

            Entity entity = systemState.EntityManager.CreateEntity();
            systemState.EntityManager.SetName(entity, $"VVardenfell.GlobalScript.{request.ProgramIndex}");
            systemState.EntityManager.AddComponentData(entity, new MorrowindGlobalScriptInstance
            {
                TargetEntity = request.TargetEntity,
                TargetPlacedRefId = request.TargetPlacedRefId,
            });
            systemState.EntityManager.AddComponentData(entity, new MorrowindScriptInstance
            {
                Program = request.Program,
                ProgramIndex = request.ProgramIndex,
                ProgramCounter = 0,
                Status = (byte)MorrowindScriptInstanceStatus.Running,
            });
            MorrowindScriptRuntimeAuthoringUtility.AddRuntimeScriptBuffers(systemState.EntityManager, entity, ref contentBlob, request.Program);
        }

        void RestartExisting(ref SystemState systemState, 
            ref RuntimeContentBlob contentBlob,
            Entity entity,
            in MorrowindScriptStartRequest request)
        {
            MorrowindScriptRuntimeAuthoringUtility.EnsureRuntimeScriptBuffers(systemState.EntityManager, entity, ref contentBlob, request.Program);

            var instance = systemState.EntityManager.GetComponentData<MorrowindScriptInstance>(entity);
            if (instance.Status == (byte)MorrowindScriptInstanceStatus.Running)
                return;

            systemState.EntityManager.SetComponentData(entity, new MorrowindGlobalScriptInstance
            {
                TargetEntity = request.TargetEntity,
                TargetPlacedRefId = request.TargetPlacedRefId,
            });
            instance.Program = request.Program;
            instance.ProgramIndex = request.ProgramIndex;
            instance.ProgramCounter = 0;
            instance.Status = (byte)MorrowindScriptInstanceStatus.Running;
            instance.DisabledReason = default;
            systemState.EntityManager.SetComponentData(entity, instance);
            systemState.EntityManager.SetName(entity, $"VVardenfell.GlobalScript.{request.ProgramIndex}");
        }

        Entity FindGlobalScriptEntity(int programIndex)
        {
            using var entities = _globalScriptQuery.ToEntityArray(Allocator.Temp);
            using var instances = _globalScriptQuery.ToComponentDataArray<MorrowindScriptInstance>(Allocator.Temp);
            for (int i = 0; i < instances.Length; i++)
            {
                if (instances[i].ProgramIndex == programIndex)
                    return entities[i];
            }

            return Entity.Null;
        }
    }
}
