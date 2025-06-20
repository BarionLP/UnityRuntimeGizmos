using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace RuntimeGizmos.Rendering
{
    public sealed class TransformGizmosRendererFeature : ScriptableRendererFeature
    {
        [SerializeField] private MaterialData materialData;
        private Pass pass;
        public override void Create()
        {
            pass = new Pass(FindFirstObjectByType<TransformGizmo>(), materialData)
            {
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents,
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (renderingData.cameraData.cameraType is UnityEngine.CameraType.Game)
            {
                renderer.EnqueuePass(pass);
            }
        }

#if UNITY_EDITOR
        void Reset()
        {
            materialData = new();


            // This gives you "Packages/com.mycompany.myPackage"
            var pkg = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(TransformGizmosRendererFeature).Assembly);
            Debug.Log(pkg);
            var assetPath = pkg == null ? "Assets/RuntimeGizmos/" : pkg.assetPath;

            materialData.XAxis = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(System.IO.Path.Combine(assetPath, "Runtime/Materials/AxisX.mat"));
            materialData.YAxis = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(System.IO.Path.Combine(assetPath, "Runtime/Materials/AxisY.mat"));
            materialData.ZAxis = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(System.IO.Path.Combine(assetPath, "Runtime/Materials/AxisZ.mat"));
            materialData.XPlane = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(System.IO.Path.Combine(assetPath, "Runtime/Materials/PlaneX.mat"));
            materialData.YPlane = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(System.IO.Path.Combine(assetPath, "Runtime/Materials/PlaneY.mat"));
            materialData.ZPlane = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(System.IO.Path.Combine(assetPath, "Runtime/Materials/PlaneZ.mat"));
            materialData.Selected = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(System.IO.Path.Combine(assetPath, "Runtime/Materials/Selected.mat"));
            materialData.Hovering = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(System.IO.Path.Combine(assetPath, "Runtime/Materials/Hover.mat"));
        }
#endif

        private sealed class Pass : ScriptableRenderPass
        {
            private readonly TransformGizmo gizmos;
            private readonly MaterialData materialData;
            private readonly Mesh mesh = new() { indexFormat = IndexFormat.UInt16 };

            public Pass(TransformGizmo gizmos, MaterialData materialData)
            {
                this.gizmos = gizmos;
                this.materialData = materialData;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                using var builder = renderGraph.AddRasterRenderPass("Transform Gizmos", out PassData data);
                var cameraData = frameData.Get<UniversalCameraData>();
                var resourceData = frameData.Get<UniversalResourceData>();

                data.gizmos = gizmos;
                data.materialData = materialData;
                data.ShaderPass = gizmos.lineMaterial.FindPass("Universal Forward"); // Shader Graph outputs multiple passes. we only need the the main pass

                switch (data.transformType)
                {
                    case TransformType.Move:
                        UpdateMeshMove(data.handleLines, data.gizmos.handleTriangles, data.gizmos.handlePlanes);
                        break;
                    case TransformType.Rotate:
                        UpdateMeshRotate(data.gizmos.circlesLines);
                        break;
                    case TransformType.Scale:
                        UpdateMeshScale(data.gizmos.handleLines, data.gizmos.handleSquares);
                        break;
                }

                data.enabled = data.transformType is not TransformType.All;
                data.hasPlanes = data.transformType is TransformType.Move;

                data.mesh = mesh;

                // builder.AllowPassCulling(false);
                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
                builder.SetRenderFunc<PassData>(ExecutePass);
            }

            static void ExecutePass(PassData data, RasterGraphContext context)
            {
                if (data.gizmos.highlightedRenderers.Count is 0 || !data.enabled) return;

                context.cmd.DrawMesh(data.mesh, Matrix4x4.identity, data.GetMaterialX(data.transformType), submeshIndex: 0, data.ShaderPass);
                context.cmd.DrawMesh(data.mesh, Matrix4x4.identity, data.GetMaterialY(data.transformType), submeshIndex: 1, data.ShaderPass);
                context.cmd.DrawMesh(data.mesh, Matrix4x4.identity, data.GetMaterialZ(data.transformType), submeshIndex: 2, data.ShaderPass);

                if (data.hasPlanes)
                {
                    context.cmd.DrawMesh(data.mesh, Matrix4x4.identity, data.GetMaterialXPlane(data.transformType), submeshIndex: 3, data.ShaderPass);
                    context.cmd.DrawMesh(data.mesh, Matrix4x4.identity, data.GetMaterialYPlane(data.transformType), submeshIndex: 4, data.ShaderPass);
                    context.cmd.DrawMesh(data.mesh, Matrix4x4.identity, data.GetMaterialZPlane(data.transformType), submeshIndex: 5, data.ShaderPass);
                }
            }

            void UpdateMeshMove(AxisVectors axisLines, AxisVectors arrows, AxisVectors planes)
            {
                var meshDataArray = Mesh.AllocateWritableMeshData(1);
                var meshData = meshDataArray[0];

                var vertexCountX = GetVertexCount(axisLines.x, arrows.x, planes.x);
                var vertexCountY = GetVertexCount(axisLines.y, arrows.y, planes.y);
                var vertexCountZ = GetVertexCount(axisLines.z, arrows.z, planes.z);

                var indexCountX = GetIndexCount(axisLines.x, arrows.x, planes.x);
                var indexCountY = GetIndexCount(axisLines.y, arrows.y, planes.y);
                var indexCountZ = GetIndexCount(axisLines.z, arrows.z, planes.z);

                int vertexCount = vertexCountX + vertexCountY + vertexCountZ;
                int indexCount = indexCountX + indexCountY + indexCountZ;

                meshData.SetVertexBufferParams(vertexCount,
                    new VertexAttributeDescriptor(VertexAttribute.Position, dimension: 3, stream: 0)
                );
                var vertices = meshData.GetVertexData<Vector3>(stream: 0);

                meshData.SetIndexBufferParams(indexCount, mesh.indexFormat);
                var indices = meshData.GetIndexData<ushort>();
                meshData.subMeshCount = 6;

                AddAxisHandle(axisLines.x, arrows.x, planes.x, vertexCountX, vertexStart: 0, indexStart: 0, subMesh: 0);
                AddAxisHandle(axisLines.y, arrows.y, planes.y, vertexCountY, vertexStart: vertexCountX, indexStart: indexCountX, subMesh: 1);
                AddAxisHandle(axisLines.z, arrows.z, planes.z, vertexCountZ, vertexStart: vertexCountX + vertexCountY, indexStart: indexCountX + indexCountY, subMesh: 2);

                Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);

                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                int GetVertexCount(List<Vector3> lineQuads, List<Vector3> arrowTriangles, List<Vector3> planeQuads) => lineQuads.Count + arrowTriangles.Count + planeQuads.Count;
                int GetIndexCount(List<Vector3> lineQuads, List<Vector3> arrowTriangles, List<Vector3> planeQuads) => lineQuads.Count * 6 / 4 + arrowTriangles.Count + planeQuads.Count * 6 / 4;

                void AddAxisHandle(List<Vector3> lineQuads, List<Vector3> arrowTriangles, List<Vector3> planeQuads, int vertexCount, int vertexStart, int indexStart, int subMesh)
                {
                    for (var i = 0; i < lineQuads.Count; i++)
                    {
                        vertices[vertexStart + i] = lineQuads[i];
                    }
                    for (var i = 0; i < arrowTriangles.Count; i++)
                    {
                        vertices[vertexStart + lineQuads.Count + i] = arrowTriangles[i];
                    }
                    for (var i = 0; i < planeQuads.Count; i++)
                    {
                        vertices[vertexStart + lineQuads.Count + arrowTriangles.Count + i] = planeQuads[i];
                    }

                    var vertexEnd = vertexStart + vertexCount;
                    var vertexStartArrow = vertexStart + lineQuads.Count;
                    var vertexStartPlane = vertexStartArrow + arrowTriangles.Count;
                    var j = indexStart;

                    // line indices
                    for (var i = vertexStart; i < vertexStartArrow; i += 4)
                    {
                        indices[j++] = (ushort)(i + 0);
                        indices[j++] = (ushort)(i + 1);
                        indices[j++] = (ushort)(i + 2);
                        indices[j++] = (ushort)(i + 2);
                        indices[j++] = (ushort)(i + 3);
                        indices[j++] = (ushort)(i + 0);
                    }

                    // arrow indices
                    for (ushort i = (ushort)vertexStartArrow; i < vertexStartPlane; i++)
                    {
                        indices[j++] = i;
                    }

                    meshData.SetSubMesh(subMesh, new SubMeshDescriptor(indexStart, j - indexStart));

                    int planeIndexStart = j;
                    // plane indices
                    for (var i = vertexStartPlane; i < vertexEnd; i += 4)
                    {
                        indices[j++] = (ushort)(i + 0);
                        indices[j++] = (ushort)(i + 1);
                        indices[j++] = (ushort)(i + 2);
                        indices[j++] = (ushort)(i + 2);
                        indices[j++] = (ushort)(i + 3);
                        indices[j++] = (ushort)(i + 0);
                    }

                    meshData.SetSubMesh(subMesh + 3, new SubMeshDescriptor(planeIndexStart, j - planeIndexStart));
                }
            }

            void UpdateMeshRotate(AxisVectors circles)
            {
                var meshDataArray = Mesh.AllocateWritableMeshData(1);
                var meshData = meshDataArray[0];

                int vertexCount = circles.x.Count + circles.y.Count + circles.z.Count;
                int indexCount = circles.x.Count + circles.y.Count + circles.z.Count;

                meshData.SetVertexBufferParams(vertexCount,
                    new VertexAttributeDescriptor(VertexAttribute.Position, dimension: 3, stream: 0)
                );
                var vertices = meshData.GetVertexData<Vector3>(stream: 0);

                meshData.SetIndexBufferParams(indexCount, mesh.indexFormat);
                var indices = meshData.GetIndexData<ushort>();
                meshData.subMeshCount = 3;

                AddAxisHandle(circles.x, start: 0, subMesh: 0);
                AddAxisHandle(circles.y, start: circles.x.Count, subMesh: 1);
                AddAxisHandle(circles.z, start: circles.x.Count + circles.y.Count, subMesh: 2);

                Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);

                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                void AddAxisHandle(List<Vector3> circleQuads, int start, int subMesh)
                {
                    for (var i = 0; i < circleQuads.Count; i++)
                    {
                        vertices[start + i] = circleQuads[i];
                    }

                    var vertexEnd = start + circleQuads.Count;
                    var j = start;
                    for (ushort i = (ushort)start; i < vertexEnd; i++)
                    {
                        indices[j++] = i;
                    }

                    meshData.SetSubMesh(subMesh, new SubMeshDescriptor(start, circleQuads.Count, MeshTopology.Quads));
                }
            }

            void UpdateMeshScale(AxisVectors axisLines, AxisVectors cubes)
            {
                var meshDataArray = Mesh.AllocateWritableMeshData(1);
                var meshData = meshDataArray[0];

                var vertexCountX = GetVertexCount(axisLines.x, cubes.x);
                var vertexCountY = GetVertexCount(axisLines.y, cubes.y);
                var vertexCountZ = GetVertexCount(axisLines.z, cubes.z);

                int vertexCount = vertexCountX + vertexCountY + vertexCountZ;
                int indexCount = vertexCount;

                meshData.SetVertexBufferParams(vertexCount,
                    new VertexAttributeDescriptor(VertexAttribute.Position, dimension: 3, stream: 0)
                );
                var vertices = meshData.GetVertexData<Vector3>(stream: 0);

                meshData.SetIndexBufferParams(indexCount, mesh.indexFormat);
                var indices = meshData.GetIndexData<ushort>();
                meshData.subMeshCount = 3;

                AddAxisHandle(axisLines.x, cubes.x, vertexCountX, start: 0, subMesh: 0);
                AddAxisHandle(axisLines.y, cubes.y, vertexCountY, start: vertexCountX, subMesh: 1);
                AddAxisHandle(axisLines.z, cubes.z, vertexCountZ, start: vertexCountX + vertexCountY, subMesh: 2);

                Mesh.ApplyAndDisposeWritableMeshData(meshDataArray, mesh);

                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                int GetVertexCount(List<Vector3> lineQuads, List<Vector3> cubeQuads) => lineQuads.Count + cubeQuads.Count;

                void AddAxisHandle(List<Vector3> lineQuads, List<Vector3> cubeQuads, int count, int start, int subMesh)
                {
                    for (var i = 0; i < lineQuads.Count; i++)
                    {
                        vertices[start + i] = lineQuads[i];
                    }
                    for (var i = 0; i < cubeQuads.Count; i++)
                    {
                        vertices[start + lineQuads.Count + i] = cubeQuads[i];
                    }

                    var end = start + count;

                    for (ushort i = (ushort)start; i < end; i++)
                    {
                        indices[i] = i;
                    }

                    meshData.SetSubMesh(subMesh, new SubMeshDescriptor(start, count, MeshTopology.Quads));
                }
            }
        }

        [Serializable]
        private sealed class MaterialData
        {
            [SerializeField] internal Material XAxis;
            [SerializeField] internal Material YAxis;
            [SerializeField] internal Material ZAxis;
            [SerializeField] internal Material XPlane;
            [SerializeField] internal Material YPlane;
            [SerializeField] internal Material ZPlane;
            [SerializeField] internal Material Selected;
            [SerializeField] internal Material Hovering;
        }

        private sealed class PassData
        {
            internal TransformGizmo gizmos;
            internal Mesh mesh;
            internal MaterialData materialData;
            internal int ShaderPass;
            internal bool enabled;
            internal bool hasPlanes;

            internal AxisVectors handleLines => gizmos.handleLines;
            internal TransformType transformType => gizmos.transformType;
            internal TransformType translatingType => gizmos.translatingType;
            internal bool IsTransforming => gizmos.IsTransforming;
            internal Axis nearAxis => gizmos.nearAxis;
            internal TransformType moveOrScaleType => (transformType is TransformType.Scale || (IsTransforming && translatingType is TransformType.Scale)) ? TransformType.Scale : TransformType.Move;

            internal Material GetMaterialX(TransformType type) => GetMaterial(type, materialData.XAxis, ((nearAxis is Axis.X) ? IsTransforming ? materialData.Selected : materialData.Hovering : materialData.XAxis), gizmos.HasTranslatingAxisPlane);
            internal Material GetMaterialY(TransformType type) => GetMaterial(type, materialData.YAxis, ((nearAxis is Axis.Y) ? IsTransforming ? materialData.Selected : materialData.Hovering : materialData.YAxis), gizmos.HasTranslatingAxisPlane);
            internal Material GetMaterialZ(TransformType type) => GetMaterial(type, materialData.ZAxis, ((nearAxis is Axis.Z) ? IsTransforming ? materialData.Selected : materialData.Hovering : materialData.ZAxis), gizmos.HasTranslatingAxisPlane);

            internal Material GetMaterialXPlane(TransformType type) => GetMaterial(type, materialData.XPlane, ((nearAxis is Axis.X) ? IsTransforming ? materialData.Selected : materialData.Hovering : materialData.XPlane), !gizmos.HasTranslatingAxisPlane);
            internal Material GetMaterialYPlane(TransformType type) => GetMaterial(type, materialData.YPlane, ((nearAxis is Axis.Y) ? IsTransforming ? materialData.Selected : materialData.Hovering : materialData.YPlane), !gizmos.HasTranslatingAxisPlane);
            internal Material GetMaterialZPlane(TransformType type) => GetMaterial(type, materialData.ZPlane, ((nearAxis is Axis.Z) ? IsTransforming ? materialData.Selected : materialData.Hovering : materialData.ZPlane), !gizmos.HasTranslatingAxisPlane);

            internal Material GetMaterial(TransformType type, Material @default, Material other, bool forceUseNormal = false)
                => !forceUseNormal && gizmos.TranslatingTypeContains(type, false) ? other : @default;
        }
    }
}
