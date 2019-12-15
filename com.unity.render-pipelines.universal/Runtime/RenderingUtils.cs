using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine.Scripting.APIUpdating;

#if POST_PROCESSING_STACK_2_0_0_OR_NEWER
using UnityEngine.Rendering.PostProcessing;
#endif

namespace UnityEngine.Rendering.Universal
{
    /// <summary>
    /// Contains properties and helper functions that you can use when rendering.
    /// </summary>
    [MovedFrom("UnityEngine.Rendering.LWRP")] public static class RenderingUtils
    {
        static List<ShaderTagId> m_LegacyShaderPassNames = new List<ShaderTagId>()
        {
            new ShaderTagId("Always"),
            new ShaderTagId("ForwardBase"),
            new ShaderTagId("PrepassBase"),
            new ShaderTagId("Vertex"),
            new ShaderTagId("VertexLMRGBM"),
            new ShaderTagId("VertexLM"),
        };

        static Mesh s_FullscreenMesh = null;

        /// <summary>
        /// Returns a mesh that you can use with <see cref="CommandBuffer.DrawMesh(Mesh, Matrix4x4, Material)"/> to render full-screen effects.
        /// </summary>
        public static Mesh fullscreenMesh
        {
            get
            {
                if (s_FullscreenMesh != null)
                    return s_FullscreenMesh;

                float topV = 1.0f;
                float bottomV = 0.0f;

                s_FullscreenMesh = new Mesh { name = "Fullscreen Quad" };
                s_FullscreenMesh.SetVertices(new List<Vector3>
                {
                    new Vector3(-1.0f, -1.0f, 0.0f),
                    new Vector3(-1.0f,  1.0f, 0.0f),
                    new Vector3(1.0f, -1.0f, 0.0f),
                    new Vector3(1.0f,  1.0f, 0.0f)
                });

                s_FullscreenMesh.SetUVs(0, new List<Vector2>
                {
                    new Vector2(0.0f, bottomV),
                    new Vector2(0.0f, topV),
                    new Vector2(1.0f, bottomV),
                    new Vector2(1.0f, topV)
                });

                s_FullscreenMesh.SetIndices(new[] { 0, 1, 2, 2, 1, 3 }, MeshTopology.Triangles, 0, false);
                s_FullscreenMesh.UploadMeshData(true);
                return s_FullscreenMesh;
            }
        }

#if POST_PROCESSING_STACK_2_0_0_OR_NEWER
        static readonly int m_PostProcessingTemporaryTargetId = Shader.PropertyToID("_TemporaryColorTexture");
        static PostProcessRenderContext m_PostProcessRenderContext;

        [Obsolete("The use of the Post-processing Stack V2 is deprecated in the Universal Render Pipeline. Use the builtin post-processing effects instead.")]
        public static PostProcessRenderContext postProcessRenderContext
        {
            get => m_PostProcessRenderContext ?? (m_PostProcessRenderContext = new PostProcessRenderContext());
        }
#endif

        internal static bool useStructuredBuffer
        {
            // There are some performance issues with StructuredBuffers in some platforms.
            // We fallback to UBO in those cases.
            get
            {
                // TODO: For now disabling SSBO until figure out Vulkan binding issues.
                // When enabling this also enable it in shader side in Input.hlsl
                return false;

                // We don't use SSBO in D3D because we can't figure out without adding shader variants if platforms is D3D10.
                //GraphicsDeviceType deviceType = SystemInfo.graphicsDeviceType;
                //return !Application.isMobilePlatform &&
                //    (deviceType == GraphicsDeviceType.Metal || deviceType == GraphicsDeviceType.Vulkan ||
                //     deviceType == GraphicsDeviceType.PlayStation4 || deviceType == GraphicsDeviceType.XboxOne);
            }
            
        }

        static Material s_ErrorMaterial;
        static Material errorMaterial
        {
            get
            {
                if (s_ErrorMaterial == null)
                {
                    // TODO: When importing project, AssetPreviewUpdater::CreatePreviewForAsset will be called multiple times.
                    // This might be in a point that some resources required for the pipeline are not finished importing yet.
                    // Proper fix is to add a fence on asset import.
                    try
                    {
                        s_ErrorMaterial = new Material(Shader.Find("Hidden/Universal Render Pipeline/FallbackError"));
                    }
                    catch{ }
                }

                return s_ErrorMaterial;
            }
        }

#if POST_PROCESSING_STACK_2_0_0_OR_NEWER
#pragma warning disable 0618 // Obsolete
        internal static void RenderPostProcessingCompat(CommandBuffer cmd, ref CameraData cameraData, RenderTextureDescriptor sourceDescriptor,
                                                        RenderTargetIdentifier source, RenderTargetIdentifier destination, bool opaqueOnly, bool flip)
        {
            var layer = cameraData.postProcessLayer;
            int effectsCount;

            if (opaqueOnly)
            {
                effectsCount = layer.sortedBundles[PostProcessEvent.BeforeTransparent].Count;
            }
            else
            {
                effectsCount = layer.sortedBundles[PostProcessEvent.BeforeStack].Count +
                               layer.sortedBundles[PostProcessEvent.AfterStack].Count;
            }

            var camera = cameraData.camera;
            var postProcessRenderContext = RenderingUtils.postProcessRenderContext;
            postProcessRenderContext.Reset();
            postProcessRenderContext.camera = camera;
            postProcessRenderContext.source = source;
            postProcessRenderContext.sourceFormat = sourceDescriptor.colorFormat;
            postProcessRenderContext.destination = destination;
            postProcessRenderContext.command = cmd;
            postProcessRenderContext.flip = flip;

            // If there's only one effect in the stack and soure is same as dest we	
            // create an intermediate blit rendertarget to handle it.	
            // Otherwise, PostProcessing system will create the intermediate blit targets itself.	
            if (effectsCount == 1 && source == destination)
            {
                var rtId = new RenderTargetIdentifier(m_PostProcessingTemporaryTargetId);
                var descriptor = sourceDescriptor;
                descriptor.msaaSamples = 1;
                descriptor.depthBufferBits = 0;

                postProcessRenderContext.destination = rtId;
                cmd.GetTemporaryRT(m_PostProcessingTemporaryTargetId, descriptor, FilterMode.Point);

                if (opaqueOnly)
                    cameraData.postProcessLayer.RenderOpaqueOnly(postProcessRenderContext);
                else
                    cameraData.postProcessLayer.Render(postProcessRenderContext);

                cmd.Blit(rtId, destination);
                cmd.ReleaseTemporaryRT(m_PostProcessingTemporaryTargetId);
            }
            else if (opaqueOnly)
            {
                cameraData.postProcessLayer.RenderOpaqueOnly(postProcessRenderContext);
            }
            else
            {
                cameraData.postProcessLayer.Render(postProcessRenderContext);
            }
        }
#pragma warning restore 0618
#endif

        // This is used to render materials that contain built-in shader passes not compatible with URP. 
        // It will render those legacy passes with error/pink shader.
        [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
        internal static void RenderObjectsWithError(ScriptableRenderContext context, ref CullingResults cullResults, Camera camera, FilteringSettings filterSettings, SortingCriteria sortFlags)
        {
            // TODO: When importing project, AssetPreviewUpdater::CreatePreviewForAsset will be called multiple times.
            // This might be in a point that some resources required for the pipeline are not finished importing yet.
            // Proper fix is to add a fence on asset import.
            if (errorMaterial == null)
                return;

            SortingSettings sortingSettings = new SortingSettings(camera) { criteria = sortFlags };
            DrawingSettings errorSettings = new DrawingSettings(m_LegacyShaderPassNames[0], sortingSettings)
            {
                perObjectData = PerObjectData.None,
                overrideMaterial = errorMaterial,
                overrideMaterialPassIndex = 0
            };
            for (int i = 1; i < m_LegacyShaderPassNames.Count; ++i)
                errorSettings.SetShaderPassName(i, m_LegacyShaderPassNames[i]);

            context.DrawRenderers(cullResults, ref errorSettings, ref filterSettings);
        }

        // Caches render texture format support. SystemInfo.SupportsRenderTextureFormat allocates memory due to boxing.
        static Dictionary<RenderTextureFormat, bool> m_RenderTextureFormatSupport = new Dictionary<RenderTextureFormat, bool>();

        internal static void ClearSystemInfoCache()
        {
            m_RenderTextureFormatSupport.Clear();
        }

        /// <summary>
        /// Checks if a render texture format is supported by the run-time system.
        /// Similar to <see cref="SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat)"/>, but doesn't allocate memory.
        /// </summary>
        /// <param name="format">The format to look up.</param>
        /// <returns>Returns true if the graphics card supports the given <c>RenderTextureFormat</c></returns>
        public static bool SupportsRenderTextureFormat(RenderTextureFormat format)
        {
            if (!m_RenderTextureFormatSupport.TryGetValue(format, out var support))
            {
                support = SystemInfo.SupportsRenderTextureFormat(format);
                m_RenderTextureFormatSupport.Add(format, support);
            }

            return support;
        }
    }
}
