using System;
using System.Reflection;
using UnityEngine.Experimental.Rendering;

namespace UnityEngine.Rendering.HighDefinition
{
    class HDRuntimeReflectionSystem : ScriptableRuntimeReflectionSystem
    {
        static MethodInfo BuiltinUpdate;

        static HDRuntimeReflectionSystem()
        {
            var type =
                Type.GetType("UnityEngine.Experimental.Rendering.BuiltinRuntimeReflectionSystem,UnityEngine");
            var method = type.GetMethod("BuiltinUpdate", BindingFlags.Static | BindingFlags.NonPublic);
            BuiltinUpdate = method;
        }

        static HDRuntimeReflectionSystem k_instance = new HDRuntimeReflectionSystem();

        // We must use a static constructor and only set the system in the Initialize method
        // in case this method is called multiple times.
        // This will be the case when entering play mode without performing the domain reload.
        [RuntimeInitializeOnLoadMethod]
        static void Initialize()
        {
            if (GraphicsSettings.currentRenderPipeline is HDRenderPipelineAsset)
                ScriptableRuntimeReflectionSystemSettings.system = k_instance;
        }

        // Note: method bool TickRealtimeProbes() will create GC.Alloc due to Unity binding code
        // (bool as return type is not handled properly)
        // Will be fixed in future release of Unity.

        public override bool TickRealtimeProbes()
        {
            BuiltinUpdate.Invoke(null, new object[0]);
            return base.TickRealtimeProbes();
        }
    }
}
