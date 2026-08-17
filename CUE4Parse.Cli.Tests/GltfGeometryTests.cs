using System.Numerics;
using Newtonsoft.Json.Linq;

namespace CUE4Parse.Cli.Tests;

public class GltfGeometryTests
{
    /// <summary>Reads a VEC3 float accessor out of a GLB's single binary chunk.</summary>
    public static Vector3[] ReadVec3Accessor(string glbPath, int accessorIndex)
    {
        var bytes = File.ReadAllBytes(glbPath);
        var json = GltfWriterTests.ReadGlbJson(glbPath);
        var binaryStart = 20 + BitConverter.ToInt32(bytes, 12) + 8; // json chunk, then BIN chunk header

        var accessor = json["accessors"]![accessorIndex]!;
        Assert.Equal("VEC3", accessor["type"]!.Value<string>());
        Assert.Equal(5126, accessor["componentType"]!.Value<int>()); // FLOAT

        var view = json["bufferViews"]![accessor["bufferView"]!.Value<int>()]!;
        var stride = view["byteStride"]?.Value<int>() ?? 12;
        var start = binaryStart + (view["byteOffset"]?.Value<int>() ?? 0) + (accessor["byteOffset"]?.Value<int>() ?? 0);
        var count = accessor["count"]!.Value<int>();

        var result = new Vector3[count];
        for (var i = 0; i < count; i++)
        {
            var at = start + i * stride;
            result[i] = new Vector3(
                BitConverter.ToSingle(bytes, at),
                BitConverter.ToSingle(bytes, at + 4),
                BitConverter.ToSingle(bytes, at + 8));
        }

        return result;
    }

    public static int AccessorIndex(string glbPath, string attribute)
    {
        var json = GltfWriterTests.ReadGlbJson(glbPath);
        return json["meshes"]![0]!["primitives"]![0]!["attributes"]![attribute]!.Value<int>();
    }

    [Theory]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SM_Fixture.uasset")]
    [InlineData("CUE4ParseFixtures/Content/Fixtures/Meshes/SK_Fixture.uasset")]
    public async Task EveryNormalIsAUnitVector(string assetPath)
    {
        var output = await GltfWriterTests.ExportAsync(assetPath);
        var glb = GltfWriterTests.SingleGlb(output);

        var normals = ReadVec3Accessor(glb, AccessorIndex(glb, "NORMAL"));

        Assert.NotEmpty(normals);
        Assert.All(normals, n => Assert.InRange(n.Length(), 1f - 1e-6f, 1f + 1e-6f));
    }

    /// <summary>
    /// A morph target that carries normals must expose NORMAL, not TANGENT, and its
    /// deltas must not be unit vectors: the renderer normalises base + sum(deltas).
    /// </summary>
    [Fact]
    public async Task MorphTargetDeltasAreNotNormalizedAndSitInTheNormalSlot()
    {
        var output = await GltfWriterTests.ExportAsync("CUE4ParseFixtures/Content/Fixtures/Meshes/SK_Fixture.uasset");
        var glb = GltfWriterTests.SingleGlb(output);

        var json = GltfWriterTests.ReadGlbJson(glb);

        if (json["meshes"]![0]!["primitives"]![0]!["targets"] is not JArray { Count: > 0 } targets)
        {
            Assert.Skip(
                "SK_Fixture has no morph targets, and the redistributable fixture set contains " +
                "no skeletal mesh that does. Recorded under Verification gaps in the output contract.");
            return;
        }

        var target = targets[0]!;
        Assert.NotNull(target["NORMAL"]);
        Assert.Null(target["TANGENT"]);

        var deltas = ReadVec3Accessor(glb, target["NORMAL"]!.Value<int>());
        Assert.Contains(deltas, d => d.Length() < 0.9f);
    }
}
