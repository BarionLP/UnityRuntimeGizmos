using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace RuntimeGizmos.Rendering
{
    public sealed class GizmosOutlineRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private Material outline;
        private Pass pass;

        public override void Create()
        {
            pass = new(FindFirstObjectByType<TransformGizmo>(), outline)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType is CameraType.Game)
            {
                renderer.EnqueuePass(pass);
            }
        }

        private sealed class Pass : ScriptableRenderPass
        {
            private readonly TransformGizmo gizmoRef;
            private readonly Material outline;

            public Pass(TransformGizmo gizmoRef, Material outline)
            {
                this.gizmoRef = gizmoRef;
                this.outline = outline;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using var builder = renderGraph.AddRasterRenderPass("Outline", out PassData data);
                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                data.Outline = outline;
                data.OutlineShaderPass = outline.FindPass("Universal Forward"); // Shader Graph outputs multiple passes. we only need the the main pass
                data.Selected = gizmoRef.highlightedRenderers;

                builder.AllowPassCulling(false);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderFunc<PassData>(ExecutePass);
            }

            static void ExecutePass(PassData data, RasterGraphContext context)
            {
                foreach (var selected in data.Selected)
                {
                    context.cmd.DrawMesh(selected.GetComponent<MeshFilter>().mesh, selected.transform.localToWorldMatrix, data.Outline, submeshIndex: 0, data.OutlineShaderPass);
                }
            }
        }

        private sealed class PassData
        {
            internal Material Outline;
            internal int OutlineShaderPass;
            internal HashSet<Renderer> Selected = new();
        }
    }
}
