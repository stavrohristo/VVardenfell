using Unity.Entities;
using VVardenfell.Runtime.Systems;

namespace VVardenfell.Runtime.Bootstrap
{
    [UpdateInGroup(typeof(MorrowindInitializationSystemGroup), OrderFirst = true)]
    public partial struct RuntimeSessionTeardownMarkSystem : ISystem
    {
        EntityQuery _sessionTeardownTargets;

        public void OnCreate(ref SystemState state)
        {
            _sessionTeardownTargets = SystemAPI.QueryBuilder()
                .WithPresent<SessionTeardown>()
                .Build();

            state.RequireForUpdate<TeardownCurrentSession>();
        }

        public void OnUpdate(ref SystemState state)
        {
            state.EntityManager.SetComponentEnabled<SessionTeardown>(_sessionTeardownTargets, true);
            RuntimeBootstrapRequestUtility.Consume<TeardownCurrentSession>(state.EntityManager);
        }
    }
}
