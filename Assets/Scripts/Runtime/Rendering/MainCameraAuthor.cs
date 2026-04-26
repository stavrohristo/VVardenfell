using System;
using Unity.Entities;
using UnityEngine;

namespace VVardenfell.Runtime.Rendering
{
    [RequireComponent(typeof(Camera))]
    public class MainCameraAuthor : MonoBehaviour
    {
        private void Start()
        {
            World.DefaultGameObjectInjectionWorld.EntityManager.CreateSingleton(new MainCameraSingleton()
            {
                Ref = GetComponent<Camera>()
            });
        }
    }

    public struct MainCameraSingleton : IComponentData
    {
        public UnityObjectRef<Camera> Ref;
        
        public Camera Camera => Ref.Value;
    }
}