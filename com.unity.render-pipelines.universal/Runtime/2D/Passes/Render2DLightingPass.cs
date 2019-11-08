using System.Collections.Generic;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using UnityEngine.Rendering.Universal;

namespace UnityEngine.Experimental.Rendering.Universal
{
    internal class Render2DLightingPass : ScriptableRenderPass
    {
        static SortingLayer[] s_SortingLayers;
        Renderer2DData m_RendererData;
        static readonly ShaderTagId k_CombinedRenderingPassNameOld = new ShaderTagId("Lightweight2D");
        static readonly ShaderTagId k_CombinedRenderingPassName = new ShaderTagId("Universal2D");
        static readonly ShaderTagId k_NormalsRenderingPassName = new ShaderTagId("NormalsRendering");
        static readonly ShaderTagId k_LegacyPassName = new ShaderTagId("SRPDefaultUnlit");
        static readonly List<ShaderTagId> k_ShaderTags = new List<ShaderTagId>() { k_LegacyPassName, k_CombinedRenderingPassName, k_CombinedRenderingPassNameOld };

        Texture m_BlackTexture;
        
        public Render2DLightingPass(Renderer2DData rendererData)
        {
            if (s_SortingLayers == null)
                s_SortingLayers = SortingLayer.layers;

            m_BlackTexture = rendererData.blackTexture;
            m_RendererData = rendererData;
        }

        public void SetTransparencySortingMode(Camera camera, out TransparencySortMode sortingMode, out Vector3 sortingAxis)
        {
            sortingMode = camera.transparencySortMode;
            sortingAxis = camera.transparencySortAxis;

            if (camera.transparencySortMode == TransparencySortMode.Default)
            {
                TransparencySortMode defaultTransparencySortMode = camera.orthographic ? TransparencySortMode.Orthographic : TransparencySortMode.Perspective;
                camera.transparencySortMode = m_RendererData.transparencySortMode == TransparencySortMode.Default ? defaultTransparencySortMode : m_RendererData.transparencySortMode;
                camera.transparencySortAxis = m_RendererData.transparencySortAxis;
            }
        }

        public void RestoreTransparencySortingMode(Camera camera, TransparencySortMode sortingMode, Vector3 sortingAxis)
        {
            camera.transparencySortMode = sortingMode;
            camera.transparencySortAxis = sortingAxis;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {

            bool isLitView = true;

#if UNITY_EDITOR
            if (renderingData.cameraData.isSceneViewCamera)
                isLitView = UnityEditor.SceneView.currentDrawingSceneView.sceneLighting;

            if (renderingData.cameraData.camera.cameraType == CameraType.Preview)
                isLitView = false;

            if (!Application.isPlaying)
                s_SortingLayers = SortingLayer.layers;
#endif
            Camera camera = renderingData.cameraData.camera;

            FilteringSettings filterSettings = new FilteringSettings();
            filterSettings.renderQueueRange = RenderQueueRange.all;
            filterSettings.layerMask = -1;
            filterSettings.renderingLayerMask = 0xFFFFFFFF;
            filterSettings.sortingLayerRange = SortingLayerRange.all;

            TransparencySortMode savedSortingMode;
            Vector3 savedSortingAxis;
            SetTransparencySortingMode(camera, out savedSortingMode, out savedSortingAxis);

            bool isSceneLit = Light2D.IsSceneLit(camera);
            if (isSceneLit)
            {
                RendererLighting.Setup(m_RendererData);

                CommandBuffer cmd = CommandBufferPool.Get("Render 2D Lighting");
                cmd.Clear();

                Profiler.BeginSample("RenderSpritesWithLighting - Create Render Textures");
                ref var targetDescriptor = ref renderingData.cameraData.cameraTargetDescriptor;
                RendererLighting.CreateRenderTextures(cmd, targetDescriptor.width, targetDescriptor.height);
                Profiler.EndSample();

                cmd.SetGlobalFloat("_HDREmulationScale", m_RendererData.hdrEmulationScale);
                cmd.SetGlobalFloat("_InverseHDREmulationScale", 1.0f / m_RendererData.hdrEmulationScale);
                cmd.SetGlobalFloat("_UseSceneLighting", isLitView ? 1.0f : 0.0f);
                RendererLighting.SetShapeLightShaderGlobals(cmd);

                context.ExecuteCommandBuffer(cmd);

                Profiler.BeginSample("RenderSpritesWithLighting - Prepare");
                DrawingSettings combinedDrawSettings = CreateDrawingSettings(k_ShaderTags, ref renderingData, SortingCriteria.CommonTransparent);
                DrawingSettings normalsDrawSettings = CreateDrawingSettings(k_NormalsRenderingPassName, ref renderingData, SortingCriteria.CommonTransparent);

                Profiler.EndSample();

                for (int i = 0; i < s_SortingLayers.Length; i++)
                {
                    // Some renderers override their sorting layer value with short.MinValue or short.MaxValue.
                    // When drawing the first sorting layer, we should include the range from short.MinValue to layerValue.
                    // Similarly, when drawing the last sorting layer, include the range from layerValue to short.MaxValue.
                    short layerValue = (short)s_SortingLayers[i].value;
                    var lowerBound = (i == 0) ? short.MinValue : layerValue;
                    var upperBound = (i == s_SortingLayers.Length - 1) ? short.MaxValue : layerValue;
                    filterSettings.sortingLayerRange = new SortingLayerRange(lowerBound, upperBound);

                    int layerToRender = s_SortingLayers[i].id;

                    Light2D.LightStats lightStats;
                    lightStats = Light2D.GetLightStatsByLayer(layerToRender);

                    if (lightStats.totalNormalMapUsage > 0)
                        RendererLighting.RenderNormals(context, renderingData.cullResults, normalsDrawSettings, filterSettings);

                    cmd.Clear();
                    if (lightStats.totalLights > 0)
                    {
                        RendererLighting.RenderLights(camera, cmd, layerToRender);
                    }
                    else
                    {
                        RendererLighting.ClearDirtyLighting(cmd);
                    }

                    CoreUtils.SetRenderTarget(cmd, colorAttachment, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, ClearFlag.None, Color.white);
                    context.ExecuteCommandBuffer(cmd);

                    Profiler.BeginSample("RenderSpritesWithLighting - Draw Transparent Renderers");
                    context.DrawRenderers(renderingData.cullResults, ref combinedDrawSettings, ref filterSettings);
                    Profiler.EndSample();

                    if (lightStats.totalVolumetricUsage > 0)
                    {

                        cmd.Clear();
                        RendererLighting.RenderLightVolumes(camera, cmd, layerToRender, colorAttachment);
                        context.ExecuteCommandBuffer(cmd);
                        cmd.Clear();
                    }
                }

                cmd.Clear();
                Profiler.BeginSample("RenderSpritesWithLighting - Release RenderTextures");
                RendererLighting.ReleaseRenderTextures(cmd);
                Profiler.EndSample();

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                filterSettings.sortingLayerRange = SortingLayerRange.all;
                RenderingUtils.RenderObjectsWithError(context, ref renderingData.cullResults, camera, filterSettings, SortingCriteria.None);
            }
            else
            {
                CommandBuffer cmd = CommandBufferPool.Get("Render Unlit");
                DrawingSettings unlitDrawSettings = CreateDrawingSettings(k_ShaderTags, ref renderingData, SortingCriteria.CommonTransparent);

                CoreUtils.SetRenderTarget(cmd, colorAttachment, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store, ClearFlag.None, Color.white);
                cmd.SetGlobalTexture("_ShapeLightTexture0", m_BlackTexture);
                cmd.SetGlobalTexture("_ShapeLightTexture1", m_BlackTexture);
                cmd.SetGlobalTexture("_ShapeLightTexture2", m_BlackTexture);
                cmd.SetGlobalTexture("_ShapeLightTexture3", m_BlackTexture);
                cmd.SetGlobalFloat("_UseSceneLighting", isLitView ? 1.0f : 0.0f);
                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);

                Profiler.BeginSample("RenderSpritesWithLighting - Draw Transparent Renderers");
                context.DrawRenderers(renderingData.cullResults, ref unlitDrawSettings, ref filterSettings);
                Profiler.EndSample();

                RenderingUtils.RenderObjectsWithError(context, ref renderingData.cullResults, camera, filterSettings, SortingCriteria.None);
            }

            RestoreTransparencySortingMode(camera, savedSortingMode, savedSortingAxis);
        }
    }
}
