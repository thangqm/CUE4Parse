using CUE4Parse_Conversion.Dto;
using CUE4Parse_Conversion.Writers.Gltf;
using CUE4Parse.UE4.Writers;

namespace CUE4Parse_Conversion.Formats.Meshes;

public sealed class GltfMeshFormat : IMeshExportFormat
{
    public string DisplayName => "glTF 2.0 (binary)";

    // ExporterBase.ResolveOutputPath builds the file name as
    // {ObjectName}{NameSuffix}.{Extension}, so ObjectName + lod._suffix is exactly the
    // file's stem. Passing it as the datablock name makes the naming rule hold with no
    // exceptions for any --mesh-quality x --nanite combination.
    private static string DatablockName(in MeshExportContext context, string? lodSuffix)
        => context.ObjectName + lodSuffix;

    public IReadOnlyList<ExportFile> BuildSkeletalMesh(in MeshExportContext context, SkeletalMeshDto dto)
    {
        var results = new List<ExportFile>();

        foreach (var lod in dto.LODs)
        {
            using var ar = new FArchiveWriter();
            new Gltf(DatablockName(context, lod._suffix), lod, context).Save(ar);

            results.Add(new ExportFile("glb", ar.GetBuffer(), lod._suffix));
        }

        return results;
    }

    public IReadOnlyList<ExportFile> BuildStaticMesh(in MeshExportContext context, StaticMeshDto dto)
    {
        var results = new List<ExportFile>();

        foreach (var lod in dto.LODs)
        {
            using var ar = new FArchiveWriter();
            new Gltf(DatablockName(context, lod._suffix), lod, context).Save(ar);

            results.Add(new ExportFile("glb", ar.GetBuffer(), lod._suffix));
        }

        return results;
    }

    public IReadOnlyList<ExportFile> BuildSkeleton(in MeshExportContext context, SkeletonDto dto)
        => throw new NotSupportedException(
            "glTF does not support skeleton-only exports. Please export a skeletal mesh to get a glTF file containing the skeleton.");
}
