using System.Numerics;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;

namespace CUE4Parse.Cli.Tests;

/// <summary>A one-triangle GLB, used to prove the validator harness itself works.</summary>
public static class MinimalGlb
{
    public static byte[] Bytes()
    {
        var mesh = new MeshBuilder<VertexPosition, VertexEmpty, VertexEmpty>("Minimal");
        var prim = mesh.UsePrimitive(new MaterialBuilder("Mat").WithMetallicRoughnessShader());
        prim.AddTriangle(
            new VertexBuilder<VertexPosition, VertexEmpty, VertexEmpty>(new VertexPosition(0, 0, 0)),
            new VertexBuilder<VertexPosition, VertexEmpty, VertexEmpty>(new VertexPosition(1, 0, 0)),
            new VertexBuilder<VertexPosition, VertexEmpty, VertexEmpty>(new VertexPosition(0, 0, 1)));

        var scene = new SceneBuilder("Minimal");
        scene.AddRigidMesh(mesh, Matrix4x4.Identity);
        return scene.ToGltf2().WriteGLB().ToArray();
    }
}
