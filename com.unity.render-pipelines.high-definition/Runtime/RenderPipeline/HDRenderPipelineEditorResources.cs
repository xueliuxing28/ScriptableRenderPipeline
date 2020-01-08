#if UNITY_EDITOR //file must be in realtime assembly folder to be found in HDRPAsset
using System;

namespace UnityEngine.Rendering.HighDefinition
{
    [HelpURL(Documentation.baseURL + Documentation.version + Documentation.subURL + "HDRP-Asset" + Documentation.endURL)]
    public partial class HDRenderPipelineEditorResources : ScriptableObject
    {
        [Reload("Editor/DefaultScene/DefaultSceneRoot.prefab")]
        public GameObject defaultScene;
        [Reload("Editor/DefaultDXRScene/DefaultSceneRoot.prefab")]
        public GameObject defaultDXRScene;
        [Reload("Editor/DefaultScene/Sky and Fog Settings Profile.asset")]
        public VolumeProfile defaultSkyAndFogProfile;
        [Reload("Editor/DefaultDXRScene/Sky and Fog Settings Profile.asset")]
        public VolumeProfile defaultDXRSkyAndFogProfile;
        [Reload("Editor/DefaultScene/Scene PostProcess Profile.asset")]
        public VolumeProfile defaultPostProcessingProfile;
        [Reload("Editor/DefaultDXRScene/Scene PostProcess Profile.asset")]
        public VolumeProfile defaultDXRPostProcessingProfile;
        [Reload(new[]
        {
            "Runtime/RenderPipelineResources/Skin Diffusion Profile.asset",
            "Runtime/RenderPipelineResources/Foliage Diffusion Profile.asset"
        })]
        public DiffusionProfileSettings[] defaultDiffusionProfileSettingsList;

        [Serializable, ReloadGroup]
        public sealed class ShaderResources
        {
            public Shader terrainDetailLitShader;
            public Shader terrainDetailGrassShader;
            public Shader terrainDetailGrassBillboardShader;
        }

        [Serializable, ReloadGroup]
        public sealed class MaterialResources
        {
            // Defaults
            [Reload("Runtime/RenderPipelineResources/Material/DefaultHDMaterial.mat")]
            public Material defaultDiffuseMat;
            [Reload("Runtime/RenderPipelineResources/Material/DefaultHDMirrorMaterial.mat")]
            public Material defaultMirrorMat;
            [Reload("Runtime/RenderPipelineResources/Material/DefaultHDDecalMaterial.mat")]
            public Material defaultDecalMat;
            [Reload("Runtime/RenderPipelineResources/Material/DefaultHDTerrainMaterial.mat")]
            public Material defaultTerrainMat;
            [Reload("Editor/RenderPipelineResources/Materials/GUITextureBlit2SRGB.mat")]
            public Material GUITextureBlit2SRGB;
        }

        [Serializable, ReloadGroup]
        public sealed class TextureResources
        {
        }

        [Serializable, ReloadGroup]
        public sealed class ShaderGraphResources
        {
            [Reload("Runtime/RenderPipelineResources/ShaderGraph/AutodeskInteractive.ShaderGraph")]
            public Shader autodeskInteractive;
            [Reload("Runtime/RenderPipelineResources/ShaderGraph/AutodeskInteractiveMasked.ShaderGraph")]
            public Shader autodeskInteractiveMasked;
            [Reload("Runtime/RenderPipelineResources/ShaderGraph/AutodeskInteractiveTransparent.ShaderGraph")]
            public Shader autodeskInteractiveTransparent;
        }

        [Serializable, ReloadGroup]
        public sealed class LookDevResources
        {
            [Reload("Editor/RenderPipelineResources/DefaultLookDevProfile.asset")]
            public VolumeProfile defaultLookDevVolumeProfile;
        }

        public ShaderResources shaders;
        public MaterialResources materials;
        public TextureResources textures;
        public ShaderGraphResources shaderGraphs;
        public LookDevResources lookDev;
    }



    [UnityEditor.CustomEditor(typeof(HDRenderPipelineEditorResources))]
    class HDRenderPipelineEditorResourcesEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            // Add a "Reload All" button in inspector when we are in developer's mode
            if (UnityEditor.EditorPrefs.GetBool("DeveloperMode")
                && GUILayout.Button("Reload All"))
            {
                foreach(var field in typeof(HDRenderPipelineEditorResources).GetFields())
                    field.SetValue(target, null);

                ResourceReloader.ReloadAllNullIn(target, HDUtils.GetHDRenderPipelinePath());
            }
        }
    }
}
#endif
