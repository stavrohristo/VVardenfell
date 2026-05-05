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
    public partial class MorrowindScriptStartRequestSystem : SystemBase
    {
        EntityQuery _globalScriptQuery;

        protected override void OnCreate()
        {
            _globalScriptQuery = GetEntityQuery(
                ComponentType.ReadWrite<MorrowindGlobalScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptInstance>(),
                ComponentType.ReadWrite<MorrowindScriptLocalValue>(),
                ComponentType.ReadWrite<MorrowindScriptStackValue>());
            RequireForUpdate<MorrowindScriptRuntimeState>();
            RequireForUpdate<MorrowindScriptStartRequest>();
            RequireForUpdate<RuntimeContentBlobReference>();
        }

        protected override void OnUpdate()
        {
            ref RuntimeContentBlob contentBlob = ref SystemAPI.GetSingleton<RuntimeContentBlobReference>().Blob.Value;

            Entity runtimeEntity = SystemAPI.GetSingletonEntity<MorrowindScriptRuntimeState>();
            var requests = EntityManager.GetBuffer<MorrowindScriptStartRequest>(runtimeEntity);
            if (requests.Length == 0)
                return;

            using var snapshot = requests.ToNativeArray(Allocator.Temp);
            requests.Clear();
            for (int i = 0; i < snapshot.Length; i++)
                ApplyRequest(ref contentBlob, snapshot[i]);
        }

        void ApplyRequest(ref RuntimeContentBlob contentBlob, in MorrowindScriptStartRequest request)
        {
            if (!request.Program.IsValid
                || (uint)request.ProgramIndex >= (uint)contentBlob.MorrowindScriptPrograms.Length
                || request.ProgramIndex != request.Program.Index)
            {
                throw new InvalidOperationException($"[VVardenfell][MWScript] invalid StartScript request programIndex={request.ProgramIndex}.");
            }

            ref var program = ref RuntimeContentBlobUtility.Get(ref contentBlob, request.Program);
            if (program.Status != (byte)MorrowindScriptProgramStatus.Compiled)
                throw new InvalidOperationException($"[VVardenfell][MWScript] StartScript requested disabled script '{program.Id.ToString()}': {program.DisabledReason.ToString()}");

            Entity existing = FindGlobalScriptEntity(request.ProgramIndex);
            if (existing != Entity.Null)
            {
                RestartExisting(ref contentBlob, existing, program.Id.ToString(), request);
                return;
            }

            Entity entity = EntityManager.CreateEntity();
            EntityManager.SetName(entity, $"VVardenfell.GlobalScript.{program.Id.ToString()}");
            EntityManager.AddComponentData(entity, new MorrowindGlobalScriptInstance
            {
                TargetEntity = request.TargetEntity,
                TargetPlacedRefId = request.TargetPlacedRefId,
            });
            EntityManager.AddComponentData(entity, new MorrowindScriptInstance
            {
                Program = request.Program,
                ProgramIndex = request.ProgramIndex,
                ProgramCounter = 0,
                Status = (byte)MorrowindScriptInstanceStatus.Running,
            });
            MorrowindScriptRuntimeAuthoringUtility.AddRuntimeScriptBuffers(EntityManager, entity, ref contentBlob, request.Program);
        }

        void RestartExisting(
            ref RuntimeContentBlob contentBlob,
            Entity entity,
            string scriptId,
            in MorrowindScriptStartRequest request)
        {
            MorrowindScriptRuntimeAuthoringUtility.EnsureRuntimeScriptBuffers(EntityManager, entity, ref contentBlob, request.Program);

            var instance = EntityManager.GetComponentData<MorrowindScriptInstance>(entity);
            if (instance.Status == (byte)MorrowindScriptInstanceStatus.Running)
                return;

            EntityManager.SetComponentData(entity, new MorrowindGlobalScriptInstance
            {
                TargetEntity = request.TargetEntity,
                TargetPlacedRefId = request.TargetPlacedRefId,
            });
            instance.Program = request.Program;
            instance.ProgramIndex = request.ProgramIndex;
            instance.ProgramCounter = 0;
            instance.Status = (byte)MorrowindScriptInstanceStatus.Running;
            instance.DisabledReason = default;
            EntityManager.SetComponentData(entity, instance);
            EntityManager.SetName(entity, $"VVardenfell.GlobalScript.{scriptId}");
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
