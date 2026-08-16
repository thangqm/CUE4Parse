using System.Collections.Generic;
using CUE4Parse_Conversion.Dto;

namespace CUE4Parse_Conversion.Formats.Meshes;

public interface IMeshExportFormat : IExportFormat
{
    public IReadOnlyList<ExportFile> BuildSkeletalMesh(in MeshExportContext context, SkeletalMeshDto dto);

    public IReadOnlyList<ExportFile> BuildStaticMesh(in MeshExportContext context, StaticMeshDto dto);

    public IReadOnlyList<ExportFile> BuildSkeleton(in MeshExportContext context, SkeletonDto dto);
}
