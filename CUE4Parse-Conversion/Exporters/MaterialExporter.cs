using CUE4Parse_Conversion.Formats.Materials;
using CUE4Parse_Conversion.Options;
using CUE4Parse_Conversion.Writers.Gltf;
using CUE4Parse.UE4.Assets.Exports.Material;

namespace CUE4Parse_Conversion.Exporters;

public sealed class MaterialExporter(UMaterialInterface material) : ExporterBase(material)
{
    protected override IReadOnlyList<ExportFile> BuildExportFiles(CancellationToken ct = default)
    {
        Log.Debug("Extracting material parameters (depth: {Depth})", Session.Options.MaterialDepth);

        var parameters = new CMaterialParams2();
        material.GetParams(parameters, Session.Options.MaterialDepth);

        var files = new List<ExportFile> { new JsonMaterialFormat().Build(ObjectName, parameters) };
        if (Session.Options.MeshFormat == EMeshFormat.USD)
        {
            files.Add(new UsdMaterialFormat().Build(ObjectName, parameters, SaveDirectory));
        }

        foreach (var texture in parameters.Textures.Values)
        {
            ct.ThrowIfCancellationRequested();
            Session.Add(texture);
        }

        // Enqueued here rather than in the binder: this is the place that already has
        // the session, and it keeps the binder pure. The classification itself belongs to
        // the binder, so the URI it emits and the file enqueued here cannot disagree.
        if (Session.Options.MeshFormat == EMeshFormat.Gltf2 &&
            GltfMaterialBinder.TryGetOrmSource(parameters, out var specular))
        {
            Session.Add(new OrmTextureExporter(specular));
        }

        return files;
    }
}
