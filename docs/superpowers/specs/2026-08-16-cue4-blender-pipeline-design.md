# Spec — `cue4` CLI thay thế FModel, output dùng được ngay trong Blender

**Ngày:** 2026-08-16
**Bản:** 2 — sửa sau buổi rà soát đối chiếu với mã nguồn
**Nhánh:** `worktree-feat-cue4-cli`
**Tiền đề:** [Báo cáo đối chiếu cue4 ↔ FModel 4.4.4](../../reports/2026-08-16-cue4-cli-vs-fmodel-parity.md)

> **Bản 2 sửa gì so với bản 1.** Bốn kết luận của bản 1 bị lật sau khi đối chiếu với
> mã nguồn: ánh xạ kênh ORM (§5.2), quy tắc đuôi file texture (§5.4), phần "đính
> chính" về decoder BC (§2, §6.2), và nhận định "audio chỉ thiếu dây nối" (§3, §7.4).
> Ngoài ra phát hiện hai lỗi chặn: export ghi sai tên file trên Linux (§9), và morph
> target bị chuẩn hoá sai (§6.1). Chi tiết từng chỗ ghi ngay tại mục tương ứng.

---

## 1. Mục tiêu

Đưa `cue4` từ "công cụ trích xuất chạy được" thành **công cụ duy nhất** trong quy trình, thay hẳn FModel, với đầu ra mà một script Blender headless (`blender --background --python`) tiêu thụ được mà không cần can thiệp tay.

Ba điều kiện để coi là đạt:

1. Một `.glb` do `cue4` xuất ra, import vào Blender, hiện đúng texture — không phải mesh trắng trơn.
2. Script bpy dựa được vào một hợp đồng đầu ra thành văn, ổn định qua các lần chạy.
3. Mesh, texture, material, **animation** và **audio** đều xuất được, không phải quay lại FModel cho bất kỳ loại nào.

### 1.1 Ngoài phạm vi

- **World/Map export.** `WorldExporter` có sẵn nhưng không được kiểm chứng, không được tài liệu hoá trong đợt này.
- **Script phía Blender.** Repo không chứa `.py` nào cho Blender; cam kết của dự án dừng ở hợp đồng đầu ra.
- **Blender trong CI.** Kiểm chứng bằng glTF-Validator và assert cấu trúc, không chạy Blender thật.
- **Giải mã audio Wwise/Bink.** Xem §7.4 — `cue4` xuất byte thô đúng định dạng; việc chuyển `.wem`/`.binka` thành file phát được thuộc về vgmstream ở bước sau, không nằm trong đợt này.
- **Tương thích byte với FModel.** Chủ động từ bỏ — xem §2.
- `--with-raw`, `--material-map`, `--no-audio-decompress`: đã cân nhắc và loại, xem §7.7.

---

## 2. Vì sao từ bỏ mục tiêu "khớp FModel"

**FModel dùng CUE4Parse làm thư viện.** Nên "FModel 4.4.4" không phải một implementation
độc lập để đối chiếu — nó là **một bản CUE4Parse cũ được ghim**. Mọi chênh lệch đo
được giữa `cue4` (build từ master) và FModel 4.4.4 là **delta giữa hai phiên bản của
chính repo này**, không phải bất đồng giữa hai cách hiểu.

Điều đó làm mục tiêu "khớp FModel" trở nên vô nghĩa theo một cách khác với bản 1 đã
mô tả: khớp FModel là **khớp với quá khứ của chính mình**, không phải khớp với đúng
đắn. Chuẩn của dự án vì thế là **spec glTF 2.0, xác thực bằng glTF-Validator chính
thức** — một chuẩn ở ngoài repo, không trôi theo lịch sử commit.

Hệ quả về phương pháp: những chênh lệch chưa giải thích được — `COLOR_0`, và con số
212 vs 167 + 7 archive kèm `unloadedVfs: 1` ở §7.6 — phải tra bằng
`git diff <phiên bản CUE4Parse mà FModel 4.4.4 ghim>..HEAD` chứ **không** bằng diff
pixel hộp đen. Việc đầu tiên là tra xem FModel 4.4.4 ghim version nào.

> **Bản 1 sai ở đâu.** Bản 1 có một bảng "bên nào đúng" và một ô "Đính chính" khẳng
> định mục D1 của báo cáo (chênh lệch −1 ở DXT5 là hồi quy của commit tối ưu decoder)
> là suy đoán sai. Khẳng định đó **không hợp lệ về mặt logic** — nó đọc mã nguồn *hiện
> tại* để bác bỏ một mệnh đề về mã nguồn *cũ*.
>
> Tra thực tế: commit `ea938ba8` *"Optimize BC1–BC5 decoders for 3–5× speedup"*
> (04/08/2026) thay `DXTDecoder` bằng `BCDecoder`. Code **trước** commit đó —
> `DXTDecoder.cs` — dùng `(2*c0 + c1) / 3` cho DXT1 (cắt xuống) nhưng
> `(2*c0 + c1 + 1) / 3` cho DXT3 và DXT5 (làm tròn). Code **sau** dùng chung một biểu
> thức cắt xuống `((2*r0 + r1) * 683) >> 11` cho cả BC1 lẫn BC3. Tức `ea938ba8` **đã
> đổi BC3 từ làm tròn sang cắt xuống**, còn BC1 giữ nguyên — tái tạo chính xác hiện
> tượng đo được.
>
> **Mục D1 của báo cáo đúng và phải giữ nguyên.** FModel không tự mâu thuẫn; sự không
> nhất quán (DXT1 cắt, DXT3/5 tròn) đã sống trong repo này nhiều năm và FModel chỉ chở
> nó đi. Bảng "bên nào đúng" của bản 1 bị gỡ.

---

## 3. Bối cảnh kỹ thuật đã xác minh

Đường dữ liệu hiện có, đã đọc và xác nhận trong code:

```
MeshExporter → Session.Add(material) → MaterialExporter
                                        ├── material.GetParams(CMaterialParams2, MaterialDepth)
                                        ├── JsonMaterialFormat().Build(...)       → M_SexyCop.json
                                        └── foreach texture: Session.Add(texture) → TextureExporter → PNG
```

| Sự kiện | Vị trí | Ý nghĩa với thiết kế |
|---|---|---|
| `MaterialExporter` đã dựng `CMaterialParams2` và đã enqueue đúng tập texture | `Exporters/MaterialExporter.cs` | Binder không cần phát minh gì, chỉ dùng lại |
| `CMaterialParams2` có bảng tra cứu `Diffuse`/`Normals`/`SpecularMasks`/`Emissive`/`DiffuseColors`/`EmissiveColors` + regex dự phòng, truy cập qua `TryGetTexture2d(out, params names)` | `UE4/Assets/Exports/Material/CMaterialParams2.cs` | Không tự chế heuristic |
| **`UsdMaterialFormat` đọc `SpecularMasks` là G = Metallic, B = Roughness** | `Formats/Materials/UsdMaterialFormat.cs:76-77` | Quy ước của repo — **ngược** với `metallicRoughnessTexture` của glTF. Xem §5.2 |
| `CMaterialParams2.BlendMode`, `UMaterial.TwoSided`, `UMaterial.OpacityMaskClipValue` (mặc định 0,333) | như trên, `UMaterial.cs` | Đủ dữ liệu cho `alphaMode`/`doubleSided` |
| `ExporterBase.SavePath` / `SaveDirectory` tất định từ package path; đã có sẵn `ExporterBase.Resolve(obj, fromDirectory, ext)` tính đường dẫn tương đối | `Exporters/ExporterBase.cs` | Tính URI được mà không cần chờ texture ghi xong — **nhưng chỉ thư mục**, xem §5.4 |
| **`TextureEncoder.Encode` trả `ext = "hdr"` cho texture HDR bất kể `options.TextureFormat`** | `Textures/TextureEncoder.cs:16-20` | Đuôi file không suy ra được từ `TextureFormat`. Xem §5.4 |
| **`TextureExporter` thêm hậu tố `_MIP{n}` (all-mips) và `_LAYER{i}` (`UTexture2DArray`)** | `Exporters/TextureExporter.cs` | Tên file không chỉ là `<ObjectName>.<ext>`. Xem §5.4 |
| `ExportSession.Add` khử trùng lặp theo `ObjectPath`, first-wins | `ExportSession.cs:68` | Cùng một texture chỉ ghi một lần — nhưng "ai thắng" phụ thuộc thứ tự nếu hai exporter khác nhau cùng `ObjectPath`. Xem §5.6 |
| **`ExportSession.ResolveOutputPath` trả đường dẫn đã `.Replace('/', '\\')` vô điều kiện** | `ExportSession.cs:165` | Export **hỏng câm trên Linux**. Chặn toàn bộ §9. Xem §6.3 |
| `ExportResult` không mang tên class | `ExportResult.cs:7` | Manifest cần `ClassName`. Xem §7.5 |
| `MeshLodDto._suffix = i == 0 ? null : IsNanite ? "_Nanite" : $"_LOD{SourceLodIndex}"` | `Dto/MeshDto.cs:162` | LOD đầu **không** có hậu tố; Nanite dùng `_Nanite`. Xem §8.2 |
| `MeshMaterialDto.Material` là `FPackageIndex?` | `Dto/MeshMaterialDto.cs` | Binder lấy `UMaterialInterface` từ DTO sẵn có |
| `MeshExporter.cs:22` — `if (!Session.Options.ExportMaterials) return null;` | `Exporters/MeshExporter.cs` | `--no-materials` tự động vô hiệu binder |
| **`SoundDecoder.Decompress` không giải nén gì** — chỉ đặt lại nhãn đuôi và trả `null` cho định dạng lạ | `Sounds/SoundDecoder.cs:111-152` | WEM/BinkA ra byte thô, không phát được. Xem §7.4 |
| `Gltf.SwapYZAndNormalize(FVector)` → `FVector.Normalize()` → `MathUtils.InvSqrt` (fast inverse sqrt kiểu Quake, sai số ~0,175%) | `Writers/Gltf/Gltf.cs`, `Utils/MathUtils.cs` | Nguyên nhân gốc của normal không chuẩn hoá |
| `Gltf.SwapYZAndNormalize(Vector4)` → `Vector4.Normalize()` (chính xác) | `Writers/Gltf/Gltf.cs` | Lý do TANGENT đúng mà NORMAL sai |
| **`UEModel` và `ActorXMesh` chuẩn hoá normal *chính xác* (`normal /= MathF.Sqrt(normal \| normal)`)** | `Writers/UEFormat/UEModel.cs:133`, `Writers/ActorX/ActorXMesh.cs:238` | Chỉ glTF bị bệnh normal; nhưng **TANGENTS của UEFormat** thì dùng `Normalize()`. Xem §6.1 |
| `CUE4ParseNatives.IsFeatureAvailable("ACL\0"u8)` có sẵn, dựa trên `WITH_ACL` do CMake bật theo sự tồn tại của submodule | `Utils/CUE4Parse-Natives.cs`, `CUE4Parse-Natives/Features.cpp`, `CMakeLists.txt:7` | §7.6 gần như miễn phí |
| `OodleHelper.InitializeAsync` có nhánh **tải oo2core từ mạng** khi native lib không có Oodle | `Compression/OodleHelper.cs:39-52` | `oodle` không thể là boolean. Xem §7.6 |

Vấn đề trung tâm nằm ở `Writers/Gltf/Gltf.cs`, trong `ExportMeshSections`:

```csharp
var mat = new MaterialBuilder().WithBaseColor(Vector4.One);
mat.Name = lod.Owner.GetMaterial(section)?.SlotName ?? $"MaterialSlot_{i}";
```

Toàn bộ thông tin material bị vứt bỏ, chỉ giữ lại cái tên. Kết quả đo được trên `MESH_SexyCop_Gold.glb`: `"materials": [{"name":"L21_BodyMat","pbrMetallicRoughness":{}}, ...]`, không có mảng `images`, không có mảng `textures`.

---

## 4. Kiến trúc

### 4.1 Hướng đã chọn

Nối material **ngay trong `CUE4Parse-Conversion/Writers/Gltf/`**, không hậu xử lý ở tầng CLI và không gác sau feature flag.

Lý do loại hai hướng kia:
- *Hậu xử lý trong CLI*: phải đọc-ghi lại file 20–40MB mỗi mesh, và phải dựng lại ánh xạ slot→material mà `Gltf.cs` vốn đã có.
- *Feature flag*: material rỗng không phải hành vi ai cố ý muốn, nó là khiếm khuyết. Thêm cờ chỉ để bảo tồn khiếm khuyết là nợ kỹ thuật, và default lệch nhau giữa CLI với thư viện dễ gây nhầm.

Đổi trực tiếp trong tầng conversion còn cho phép đẩy ngược phần glTF lên upstream.

### 4.2 File mới

| File | Trách nhiệm |
|---|---|
| `CUE4Parse-Conversion/Writers/Gltf/GltfMaterialBinder.cs` | `CMaterialParams2` → `MaterialBuilder`, kèm URI tương đối. Hàm thuần. |
| `CUE4Parse-Conversion/Exporters/OrmTextureExporter.cs` | Repack `SpecularMasks` → ORM hợp quy ước glTF. Xem §5.6 |
| `CUE4Parse-Conversion/Exporters/TextureFileNamer.cs` | Một nguồn sự thật cho `(ext, suffix)` của file texture. Dùng chung bởi `TextureExporter` và binder. Xem §5.4 |
| `CUE4Parse-Conversion/Formats/Meshes/MeshExportContext.cs` | Record gộp tham số của `IMeshExportFormat`. Xem §4.5 |
| `CUE4Parse-Conversion/Exporters/SoundExporter.cs` | Bọc `SoundDecoder.Decode` |
| `CUE4Parse.Cli/Output/ExportManifest.cs` | Gom `ExportResult` → manifest JSON có hash, sắp xếp tất định |
| `.github/workflows/cli-tests.yml` | CI cho `CUE4Parse.Cli.Tests` + glTF-Validator. File **mới** để bề mặt xung đột với upstream bằng không |
| `docs/cue4-output-contract.md` | Hợp đồng đầu ra |
| `docs/cue4-guide.md` | Chuyển `C:\tools\cue4_guide.txt` vào repo, cập nhật |
| `tools/parity/` | Bộ script đối chiếu |

### 4.3 File sửa

`Writers/Gltf/Gltf.cs` · `Writers/UEFormat/UEModel.cs` (§6.1) · `Formats/Meshes/IMeshExportFormat.cs` và bốn implementor (§4.5) · `Exporters/ExportSession.cs` (§6.3 và §5.6) · `Exporters/ExporterBase.cs` (ctor `protected` nhận hậu tố tên, §5.6) · `Exporters/TextureExporter.cs` · `Exporters/MaterialExporter.cs` · `ExportResult.cs` (thêm `ClassName`, §7.5) · `Textures/BC/BCDecoder.cs` (§6.2) · `Options/ExportOptions.cs` · `CUE4Parse.Cli/Services/ExportOptionsMapper.cs` · `CUE4Parse.Cli/Program.cs` · `CUE4Parse.Cli/Commands/{ExportCommand,InfoCommand}.cs` · `.gitmodules`/native build cho ACL · `CLAUDE.md`.

### 4.4 Hai ranh giới

**Binder chỉ được tham chiếu texture mà session thật sự ghi ra.** Bảo đảm bằng cấu trúc chứ không bằng kiểm tra chéo: binder gọi đúng `material.GetParams(params, Session.Options.MaterialDepth)` mà `MaterialExporter` gọi, nên hai tập trùng nhau theo định nghĩa. Khi `ExportMaterials == false` thì không có slot nào để bind. Việc enqueue `OrmTextureExporter` cũng đặt ở `MaterialExporter` chứ không ở binder, để binder giữ được tính thuần (§5.6).

**Manifest thuần CLI.** Tầng `CUE4Parse-Conversion` không biết gì về manifest; nó chỉ trả `ExportResult`. Thêm `ClassName` vào `ExportResult` **không** phá ranh giới này — thư viện chỉ báo cáo đầy đủ hơn về việc mình đã làm, nó vẫn không biết dữ liệu ấy sẽ được dùng để làm gì.

### 4.5 `MeshExportContext`

`Gltf` không có `ExportOptions` lẫn `SaveDirectory`, mà binder cần cả hai. `IMeshExportFormat` có `options` nhưng không có `saveDirectory`, và `saveDirectory` **không** suy ra được từ `objectPath` nếu không chép lại logic của `ExporterBase` — chép lại là tạo ra hai nguồn sự thật cho đường dẫn.

Vì đằng nào cũng phải đổi chữ ký, gộp luôn thay vì thêm tham số thứ sáu:

```csharp
public readonly record struct MeshExportContext(
    string ObjectName, string ObjectPath, string SaveDirectory,
    ExportOptions Options, IReadOnlyDictionary<string, string>? MaterialPaths = null);
```

Đây là breaking change với bốn implementor của `IMeshExportFormat`, tất cả đều nằm trong repo. Đổi một lần, thay vì mỗi lần cần thêm một mẩu ngữ cảnh lại đổi tiếp.

Lý lẽ chống lại việc để binder tự `TryLoad<UMaterialInterface>()` trong writer không đứng vững: `Gltf.cs:58` đã gọi `morphTargets[j].Load<UMorphTarget>()` — tiền lệ có sẵn.

---

## 5. `GltfMaterialBinder`

### 5.1 Chữ ký

```csharp
MaterialBuilder Bind(UMaterialInterface material, string slotName,
                     ExportOptions options, string meshSaveDirectory)
```

`meshSaveDirectory` là `ExporterBase.SaveDirectory` của mesh, chuyền xuống qua `MeshExportContext`; mọi URI tính tương đối từ đó. Không tham chiếu `ExportSession` nên test được bằng một `UMaterialInterface` dựng sẵn.

### 5.2 Ánh xạ kênh

| glTF | Nguồn `CMaterialParams2` |
|---|---|
| `pbrMetallicRoughness.baseColorTexture` | `Diffuse[0]` |
| `pbrMetallicRoughness.baseColorFactor` | `DiffuseColors[0]`, bỏ qua nếu không có |
| `pbrMetallicRoughness.metallicRoughnessTexture` | **texture ORM sinh ra từ `SpecularMasks[0]`**, xem §5.6 |
| `normalTexture` | `Normals[0]` |
| `emissiveTexture`, `emissiveFactor` | `Emissive[0]`, `EmissiveColors[0]` |
| `occlusionTexture` | **không ghi** |

> **Bản 1 sai ở đâu.** Bản 1 viết *"Quy ước ORM của UE (R = AO, G = Roughness,
> B = Metallic) trùng đúng `metallicRoughnessTexture` của glTF, nên không cần hoán vị
> kênh."* Sai. Quy ước của repo là **G = Metallic, B = Roughness** — đọc thẳng từ
> `UsdMaterialFormat.cs:76-77`, khớp packing "SRM" của Fortnite (R = Specular,
> G = Metallic, B = Roughness). glTF thì bắt buộc G = Roughness, B = Metallic. Hai cái
> **hoán vị đúng nhau**, mà glTF 2.0 không có cơ chế swizzle kênh — nên phải sinh
> texture repack (§5.6).
>
> Bản 1 cũng ánh xạ `occlusionTexture` ← `SpecularMasks[0]` với lý do "glTF đọc kênh
> R". Nhưng R của `SpecularMasks` là **specular**, không phải AO. Nhét vào là bịa dữ
> liệu, vi phạm chính nguyên tắc §5.5. Bỏ hẳn.

### 5.3 Alpha và mặt

| glTF | Nguồn | Quy tắc |
|---|---|---|
| `alphaMode` | `CMaterialParams2.BlendMode` | `BLEND_Opaque` → `OPAQUE`; `BLEND_Masked` → `MASK`; `BLEND_Translucent`, `BLEND_Additive`, `BLEND_Modulate` → `BLEND` |
| `alphaCutoff` | `UMaterial.OpacityMaskClipValue` | chỉ ghi khi `alphaMode == MASK` |
| `doubleSided` | `UMaterial.TwoSided` | |

Thiếu ba trường này thì tất, lưới đánh cá và kính trong bộ mod nhân vật sẽ thành khối đục trong Blender — đúng tình trạng hiện nay và FModel cũng không giải quyết.

### 5.4 Quy tắc URI

- Luôn dùng `/`, kể cả trên Windows.
- Percent-encode theo RFC 3986 (tên asset UE có thể chứa khoảng trắng và ký tự Unicode).
- Tương đối từ `meshSaveDirectory` tới `texture.SavePath`; texture dùng chung sẽ ra dạng `../Common/Materials/BodyTextures/L21_Body_Color.png`. Dùng lại `ExporterBase.Resolve(obj, fromDirectory, ext)` đã có.
- Cùng một texture xuất hiện nhiều lần → tái dùng một mục `images`/`textures`, không nhân bản.
- Texture **không** nhúng vào `.glb`. Bộ `Lemi21_Mods` có 6 mesh dùng chung khoảng 60MB body texture; nhúng sẽ nhân bản chỗ đó sáu lần.

**Tên file không suy ra được từ `options.TextureFormat`.** Ba luật chồng lên nhau
quyết định tên thật:

1. `TextureEncoder.Encode` trả `ext = "hdr"` cho texture HDR, bất kể `TextureFormat`.
2. `--all-mips` thêm hậu tố `_MIP{index}`.
3. `UTexture2DArray` thêm hậu tố `_LAYER{i}`.

Nên tên có thể là `T_Foo.png`, `T_Foo.hdr`, `T_Foo_MIP0.png` hoặc `T_Foo_LAYER0.png`.

→ **`TextureFileNamer` tĩnh, dùng chung bởi `TextureExporter` và binder**, nhận
`UTexture` + `ExportOptions`, trả `(ext, suffix)`. Cả ba luật đều quyết định được
**không cần decode** (`PlatformData.PixelFormat` cho HDR-ness, type check cho array,
`GetFirstMipIndex()` cho mip), nên binder vẫn thuần và vẫn không phải chờ texture ghi
xong. Đây là cách duy nhất giữ cam kết §8.3 mà không nhân đôi luật đặt tên rồi để
chúng trôi khỏi nhau.

> **Bản 1 sai ở đâu.** Bản 1 viết *"Đuôi file lấy từ `options.TextureFormat`, không
> hard-code `.png`"* và, ở §5.5, *"`--all-mips` → trỏ vào `texture.GetFirstMipIndex()`,
> khớp file mà nhánh mặc định sinh ra"*. Cả hai đều sai: ở chế độ all-mips **không tồn
> tại** file không hậu tố.

**Định dạng texture hợp lệ trong glTF.** glTF 2.0 lõi chỉ chấp nhận `image/png` và
`image/jpeg`. WebP cần `EXT_texture_webp`; TGA và Radiance HDR **không có extension
nào**. Vì vậy:

- `--texture-format tga` hoặc `webp` cùng `--mesh-format gltf2` → **lỗi sử dụng, exit
  2**, báo ngay lúc parse tham số. Người dùng đã gõ cờ đó; im lặng đưa cho họ thứ khác
  là không trung thực.
- Khi `MeshFormat == Gltf2`, **ép `ExportHdrTexturesAsHdr = false`** (cùng chỗ, cùng
  kiểu với dòng ép PNG cho USD đã có trong `ExportOptions`), kèm `Log.Warning` cho mỗi
  texture bị hạ. Ở đây ngược lại với trên: người dùng không gõ gì cả, và xung đột chỉ
  lộ ra giữa chừng ở từng texture — không thể fail cả lệnh vì một texture. Hệ quả:
  `--no-hdr` **vô nghĩa với `gltf2`**, phải ghi rõ trong bảng cờ §7.1.
- Việc USD ép PNG (đã tồn tại, chưa từng được tài liệu hoá) phải được ghi vào
  `docs/cue4-output-contract.md`.

### 5.5 Trường hợp biên

| Tình huống | Xử lý |
|---|---|
| `--all-mips` | URI trỏ tới `<Tên>_MIP{GetFirstMipIndex()}.<ext>` theo `TextureFileNamer` |
| `UTexture2DArray` | URI trỏ tới `<Tên>_LAYER0.<ext>` |
| `UTextureCube` | **Bỏ trống kênh** + `Log.Debug`. Panorama tương đương không dùng làm base color theo UV được; trỏ vào là bịa |
| Không phân loại được kênh nào đó | Bỏ trống kênh, **không bịa**, ghi `Log.Debug`. Material vẫn có tên và vẫn khớp slot |
| `--no-materials` | Binder không chạy; quay về `MaterialBuilder` trần như hiện nay |
| `--material-depth top-layer-only` | Binder dùng đúng depth ấy nên không bao giờ trỏ tới texture chưa xuất |
| `MeshMaterialDto.Material` là null | Material trần mang tên `slotName` |

### 5.6 `OrmTextureExporter`

glTF không hoán vị được kênh, nên `metallicRoughnessTexture` phải trỏ vào một ảnh đã
repack: **R = 1, G ← B nguồn (roughness), B ← G nguồn (metallic)**.

Chỗ đặt nó quyết định tính tất định của cả đầu ra:

- *Gắn cờ cho `TextureExporter` để nó ghi thêm `_ORM`* — **loại**. `ExportSession.Add`
  khử trùng lặp theo `ObjectPath` với luật first-wins, nên một texture vừa được
  material X enqueue có cờ (X phân loại nó là `SpecularMasks`) vừa được material Y
  enqueue không cờ (Y phân loại nó là `Diffuse`) sẽ ra kết quả tuỳ thứ tự thắng cuộc
  của `Parallel.ForEachAsync`. Tiêu chí nghiệm thu 5 sẽ fail không ổn định. Bộ
  `Lemi21_Mods` — 6 mesh dùng chung một tập texture — là ví dụ có thật.
- *`MaterialExporter` tự ghi `<TênMaterial>_ORM.png`* — **loại**. Tất định, nhưng N
  material dùng chung một spec mask thì ra N bản sao. Mâu thuẫn với chính lý do §5.4
  chấp nhận độ phức tạp của URI vượt cấp.
- **`OrmTextureExporter(texture)` với `ObjectPath` riêng — chọn.** Session dedupe theo
  `ObjectPath` nên nó ghi đúng một lần dù bao nhiêu material cùng trỏ tới; file nằm
  cạnh texture gốc; mọi lần enqueue đều tương đương nên thứ tự không còn ảnh hưởng.

Cần thêm một ctor `protected` ở `ExporterBase` nhận hậu tố tên, vì ctor riêng hiện là
`private` và `ObjectName` chốt cứng bằng `export.Name`. Đổi lại, `SavePath` /
`SaveDirectory` tự tính ra đường dẫn `<Tên>_ORM` mà không phải viết tay lần thứ hai.

Enqueue đặt ở `MaterialExporter` (nơi đã phân loại `CMaterialParams2` và có `Session`),
có guard `MeshFormat == Gltf2` để không sinh file thừa cho UEFormat/ActorX/USD. Binder
chỉ cần **biết tên file**, mà tên đó tất định — nên binder vẫn thuần.

Với `--all-mips`, ORM **chỉ sinh cho mip đầu**: nó tồn tại để glTF trỏ vào, mà glTF chỉ
trỏ được một ảnh.

---

## 6. Sửa tính đúng đắn

### 6.1 `NORMAL` phải là vector đơn vị, và morph delta thì **không**

`Gltf.SwapYZAndNormalize(FVector)` đổi sang chuẩn hoá chính xác bằng `System.Numerics`.

**Không sửa `MathUtils.InvSqrt`** — nó cố ý mô phỏng `FMath::InvSqrt` của UE và được dùng ở nhiều nơi ngoài đường ghi file. Chỉ đổi call site trong exporter.

**Morph target đang bị phá dữ liệu.** `Gltf.cs:76`:

```csharp
new VertexGeometryDelta(SwapYZ(delta.PositionDelta * UnitScale),
                        Vector3.Zero,
                        SwapYZAndNormalize(delta.TangentZDelta))
```

`FMorphTargetDelta.TangentZDelta` là `FVector`, nên nó rơi vào đúng overload trên. Hai
lỗi chồng nhau:

1. **Chuẩn hoá một vector hiệu là sai về bản chất.** Delta normal điển hình dài 0,02;
   chuẩn hoá biến nó thành 1,0 — sai 50 lần, không phải 0,175%. Theo spec glTF, delta
   của morph target không cần đơn vị; renderer cộng `base + Σ(weight × delta)` rồi mới
   chuẩn hoá.
2. **Đặt nhầm khe.** Trong UE, `TangentZ` *chính là* normal. Ở đây `Vector3.Zero` vào
   khe thứ hai còn `TangentZDelta` vào khe thứ ba.

Sửa:

```csharp
new VertexGeometryDelta(SwapYZ(delta.PositionDelta * UnitScale), SwapYZ(delta.TangentZDelta), Vector3.Zero)
```

không nhân `UnitScale` cho delta normal (nó là hướng). **Phải xác nhận lại thứ tự tham
số của `VertexGeometryDelta` trong SharpGLTF 1.0.6 trước khi sửa.** Morph target bật
mặc định, nên lỗi này đang có hiệu lực ở mọi lần export skeletal mesh.

**Rà soát các format khác.** Bản 1 dự đoán UEFormat/ActorX/USD "nhiều khả năng cùng
bệnh". Sai: cả hai đã chuẩn hoá chính xác bằng `normal /= MathF.Sqrt(normal | normal)`.
glTF là chỗ **duy nhất** dùng `FVector.Normalize()` cho normal. Nhưng rà soát vẫn lộ ra
một chỗ khác: **`UEModel.cs:139-143`, TANGENTS của UEFormat**, dùng `tangent.Normalize()`.
Sửa cùng lô (một dòng), nhưng nó **không** nằm trong cam kết §8.3 nên không kéo theo
nghĩa vụ kiểm chứng mới.

### 6.2 BC decoder — chuyển sang làm tròn

BC4 và BC5 **đang làm tròn** (số hạng `+3`/`+2` trong `DecodeBCColors`), còn BC1/BC2/BC3
cắt xuống. "Cắt xuống" không phải quy ước của repo — nó là kết quả không chủ ý của
`ea938ba8`, một commit tối ưu tốc độ (§2).

→ **Đổi BC1, BC2, BC3 sang làm tròn** (thêm số hạng `+1` trước phép chia ba, giữ thủ
thuật nhân-dịch để không mất tốc độ). Kết quả: cả năm decoder nhất quán, sai số trung
bình về 0 thay vì −1/3 LSB, và BC3 quay lại đúng đầu ra lịch sử của nó.

Cần kiểm tra riêng: hằng số `683`/`>>11` (và `>>19` cho kênh green đã dịch sẵn 8 bit)
có còn cho ra `floor(x/3)` đúng trên toàn dải sau khi cộng thêm 1 hay không. Test vét
cạn (§9) chính là chỗ trả lời.

> **Bản 1 sai ở đâu.** Bản 1 kết luận "giữ nguyên công thức" với lý do *"đổi BC1 sang
> làm tròn sẽ làm mọi texture DXT1 đã xuất trước đây đổi byte — churn thật, lợi ích
> không nhìn thấy."* Lập luận không còn đứng: churn **đã xảy ra rồi**, âm thầm, cho mọi
> texture DXT3/DXT5, trong `ea938ba8`. Và chính §8.4 đã không cam kết byte texture ổn
> định giữa các phiên bản, nên churn không phải chi phí.
>
> Test vét cạn cũng phải đổi ngưỡng: assert lệch **= 0** so với số học chính xác, chứ
> không phải "≤ 1" — ngưỡng mà cả hai công thức đều lọt qua, tức là một test không phân
> biệt được đúng với sai.

### 6.3 Dấu phân cách đường dẫn

`ExportSession.ResolveOutputPath` kết thúc bằng `return fullPath.Replace('/', '\\');`,
vô điều kiện. Trên Linux, `\` là ký tự tên file hợp lệ, nên
`/tmp/out/Game/Content/Foo/Bar.glb` biến thành chuỗi `\tmp\out\Game\Content\Foo\Bar.glb`
và `File.WriteAllBytesAsync` tạo **một file duy nhất mang cái tên đó trong thư mục làm
việc hiện tại** — trong khi `Directory.CreateDirectory` đã lỡ tạo đúng cây thư mục rỗng
bên cạnh. Không ném lỗi, không cảnh báo.

→ **Đổi thành `Path.DirectorySeparatorChar`.** Xoá hẳn dòng `Replace` thì không đủ: trên
Windows `Path.Combine("C:\out", "Game/Content/Foo")` cho ra dấu trộn lẫn, vốn là lý do
dòng đó tồn tại.

Đây là **điều kiện tiên quyết** của toàn bộ §9 và của tiêu chí nghiệm thu 1, 2, 3 — CI
chạy trên Linux. Cũng là tiền đề của manifest: §7.5 hứa `path` tương đối, mà muốn tính
tương đối thì đường dẫn tuyệt đối phải đúng trước đã.

Hệ quả phụ: `ExportResult.DiskFilePaths` sẽ trả dấu phân cách gốc của hệ điều hành thay
vì luôn là `\`. Trong repo không có consumer nào dựa vào điều đó; ngoài repo thì chấp
nhận rủi ro.

---

## 7. Bề mặt CLI

### 7.1 Cờ mới

| Cờ | Ánh xạ | Hành vi khi **không** truyền cờ |
|---|---|---|
| `--compression-format none\|gzip\|zstd` | `ExportOptions.compressionFormat` | `none` — không nén |
| `--no-morph-targets` | `exportMorphTargets = false` | **có** xuất morph target |
| `--no-hdr` | `exportHdrTexturesAsHdr = false` | **có** giữ texture HDR ở dạng HDR — **trừ khi `--mesh-format gltf2`**, khi đó cờ này vô nghĩa vì HDR luôn bị ép xuống (§5.4) |
| `--flip-normal-y` | xem §7.2 | **không** đảo kênh xanh |
| `--manifest <file>` | xem §7.5 | không ghi manifest |

`ExportOptions` đã tự bỏ qua `compressionFormat` khi format khác UEFormat, nên không cần bảo vệ thêm ở tầng CLI.

### 7.2 `--flip-normal-y`

Đặt ở `ExportOptions`, tác động lúc `TextureExporter` ghi file chứ không phải trong binder — glTF không có khái niệm "đảo kênh xanh" nên buộc phải đảo byte PNG.

Điều kiện chọn texture để đảo: `texture.CompressionSettings == TC_Normalmap` (đã xác nhận có trên asset Stellar Blade), **không** dựa vào phân loại `Normals` của `CMaterialParams2` — bám thuộc tính của chính texture thì chắc hơn bám heuristic tên.

Mặc định tắt: xuất y nguyên byte như trong game. Người dùng mở thử trong Blender, nếu ánh sáng lõm-lồi ngược thì bật cờ. Không đoán mò thay họ.

### 7.3 Cảnh báo Nanite

Giữ mặc định `--nanite no-nanite`, **thêm cảnh báo** khi mesh có dữ liệu Nanite mà đang bị bỏ qua. Đổi mặc định là đánh cược mù; cảnh báo thì luôn đúng. Với game UE5 chỉ có Nanite LOD, hành vi hiện tại cho ra mesh rỗng mà không ai biết tại sao.

### 7.4 Audio

`SoundExporter(UObject)` bọc `SoundDecoder.Decode(...)`, cắm vào `ExportSession.Add` cho `USoundWave`, `USoundNodeWave`, `UAkMediaAssetData`. Đuôi file lấy từ tham số `out audioFormat` của decoder.

**`cue4` xuất byte thô, không giải mã.** `SoundDecoder.Decompress` không giải nén gì —
nó chỉ đặt lại nhãn đuôi (PCM → `.wav`) và trả `null` cho định dạng lạ. Wwise ra `.wem`
thô, Fortnite ra `.binka` thô; cả hai không phát được bằng trình phát thông thường.
FModel làm được là vì nó **bundle vgmstream**; CUE4Parse không có. Cái thiếu không phải
dây nối — cái thiếu là **codec**.

Chấp nhận phạm vi này vì mục tiêu §1 là quy trình Blender, mà Blender không tiêu thụ
audio. Audio có mặt chỉ để thoả điều kiện "không phải quay lại FModel" — và điều kiện
đó **đạt**: byte `cue4` trích ra là cùng byte FModel trích ra, chỉ khác là FModel decode
hộ ở bước sau. Nhánh `OGG` thì phát được ngay, vì Ogg Vorbis đã là định dạng cuối.

`docs/cue4-output-contract.md` phải ghi rõ WEM/BinkA cần vgmstream ở bước sau.

**Không** dispatch `USoundCue` — nó là đồ thị nút, không phải dữ liệu audio; để nó tiếp tục rơi vào `dump`.

> **Bản 1 sai ở đâu.** Bản 1 ghi *"Audio chỉ thiếu dây nối, không thiếu hạ tầng"* và
> đặt tiêu chí nghiệm thu *"một asset audio Wwise xuất ra file phát được"*. Tiêu chí đó
> không đạt được bằng dây nối; đã hạ phạm vi và viết lại §12.7.

### 7.5 Manifest

`--manifest <file.json>` trên `export`. Ngoài yêu cầu của script bpy, nó giải quyết một vấn đề quan sát được: **thứ tự dòng NDJSON đổi giữa các lần chạy** vì `ExportSession` chạy song song, nên output hiện tại không diff được.

```json
{
  "version": 1,
  "tool": "cue4 x.y.z",
  "options": {
    "meshFormat": "gltf2", "textureFormat": "png", "texturePlatform": "desktop",
    "meshQuality": "highest", "nanite": "no-nanite", "socketFormat": "bone",
    "materialDepth": "top-layer-only", "textureQuality": 100,
    "exportMaterials": true, "allMips": false, "flipNormalY": false,
    "compressionFormat": "none", "exportMorphTargets": true, "exportHdrTexturesAsHdr": false
  },
  "entries": [
    {
      "objectPath": "SB/Content/.../MESH_SexyCop_Gold.MESH_SexyCop_Gold",
      "class": "SkeletalMesh",
      "status": "ok",
      "files": [
        { "path": "SB/Content/.../MESH_SexyCop_Gold.glb", "bytes": 21708676, "sha256": "..." }
      ]
    }
  ]
}
```

`entries` **sắp xếp theo `objectPath`** để diff được. `path` tương đối so với `-o`, dùng `/`, để manifest di chuyển giữa các máy.

**`ExportResult` phải mang thêm `ClassName`.** Hiện nó chỉ có
`(Success, ObjectPath, DiskFilePaths, Error)`. CLI không tự suy ra được `class`: nó chỉ
`session.Add(export)` cho các asset gốc, còn material, texture, ORM, DNA đều do thư viện
enqueue truyền ứng — CLI không bao giờ nhìn thấy các `UObject` đó, nhưng chúng vẫn nằm
trong `results`. `ExporterBase.ClassName` đã có sẵn, chỉ là không được chuyển ra ngoài.

Manifest **chưa** mô tả LOD nguồn (`sourceLodIndex`, `isNanite`) — việc đó cần metadata
theo từng `ExportFile` chứ không phải từng `ExportResult`, hoãn sang đợt sau. Ghi vào
§8.4 để không ai xây script lên trên một giả định.

### 7.6 `info` mở rộng

Báo cáo tính năng native. Hạ tầng đã có: `CUE4ParseNatives.IsFeatureAvailable("ACL\0"u8)`,
dựa trên `WITH_ACL` mà CMake bật theo sự tồn tại của submodule — đúng câu hỏi cần hỏi.

```json
"native": {
  "library": true,
  "acl": false,
  "oodle": "downloaded"
}
```

- `library` = `CUE4ParseNatives.IsInitialized`. Tách được "không có native lib nào cả"
  khỏi "có lib nhưng build thiếu ACL" — hai tình huống có hai cách sửa khác nhau (chạy
  CMake, so với `git submodule update --init --recursive`).
- `oodle` là **enum ba trạng thái** `"native" | "downloaded" | "unavailable"`, không
  phải boolean: `OodleHelper.InitializeAsync` có nhánh tải oo2core từ mạng, nên
  `IsFeatureAvailable("Oodle")` trả `false` không có nghĩa là oodle không dùng được.
  Khác biệt có thật — bản tải cần mạng, nên máy CI offline hỏng ở chỗ không ai ngờ. Spec
  §1 nói người tiêu thụ là agent parse stdout; một boolean mang ba nghĩa là đúng thứ
  khiến agent suy luận sai mà không có cách nào biết.

**Khối `native` in trước khi dựng provider.** `InfoCommand.Execute` hiện mở đầu bằng
`ProviderFactory.Create(...)`, nên nếu `paksDir` sai thì nó thoát exit 4 và người dùng
không bao giờ biết `acl: false` — dù đó là thông tin duy nhất không phụ thuộc provider.
Một lệnh chẩn đoán mà hỏng khi có sự cố thì ngược đời. Khi dựng provider thất bại: vẫn
in `native` kèm khối `error`, **giữ nguyên exit 4**. Đây là ngoại lệ hợp lý duy nhất cho
quy tắc "lỗi thì chỉ in JSON lỗi" — với `info`, chẩn đoán *chính là* sản phẩm.

`--verbose` liệt kê từng archive đã mount, để làm rõ chênh lệch 212 (cue4) vs 167 + 7 loose files (FModel) và `unloadedVfs: 1` chưa giải thích được — nhưng xem §2: cách tra đúng là diff phiên bản, không phải quan sát hộp đen.

### 7.7 Đã cân nhắc và loại

- **`--with-raw`**: quy trình là Blender, `.uasset` thô không dùng tới. Ai cần đã có `cue4 unpack`.
- **`--material-map`**: `CMaterialParams2` đã có bảng lớn cộng regex. Thêm cơ chế ghi đè khi chưa biết nó hỏng ở đâu là thừa. Nếu lộ ra tên tham số lạ, cách đúng là bổ sung vào `CMaterialParams2` để mọi consumer cùng hưởng.
- **`--no-audio-decompress`**: khác biệt duy nhất giữa `shouldDecompress` `true` và
  `false` là — với `true`, PCM được đổi nhãn `.wav` và định dạng lạ trả `null`; với
  `false`, luôn nhận byte thô kèm nhãn gốc. Tức "decompress" thực chất nghĩa là "kiểm
  tra header và từ chối cái không nhận ra". Một cờ mà tên nói một đằng, tác dụng một
  nẻo, và tác dụng thật thì gần bằng không, là thứ script bpy sẽ hiểu nhầm đúng một lần
  rồi mất buổi chiều. Giữ `shouldDecompress: true` cố định.

---

## 8. Hợp đồng đầu ra

Nội dung `docs/cue4-output-contract.md`.

### 8.1 Cấu trúc thư mục

Theo quy tắc `ExporterBase.SavePath` hiện có: `<out>/<package path>/<ObjectName>.<ext>`; khi lá của package path trùng `ObjectName` thì không lồng thêm một cấp.

Hậu tố tên file theo `MeshLodDto._suffix`: LOD **đầu tiên được xuất** không có hậu tố;
các LOD sau là `_LOD{SourceLodIndex}`; LOD Nanite là `_Nanite`. Với
`--mesh-quality highest`, file `MESH_X.glb` có thể chứa LOD nguồn số 3.

### 8.2 Quy ước tên — có một thay đổi

Hiện tại `meshes[0].name` là `"LOD0"`, không mang tên asset. Khi script bpy import nhiều `.glb` vào cùng một scene, các datablock mesh đều tên `LOD0` nên Blender tự đổi thành `LOD0.001`, `LOD0.002`… và script mất khả năng định danh.

**Đổi thành: tên datablock mesh và tên node gốc = tên file `.glb` không đuôi**, tức
`ObjectName + _suffix`:

| File | `meshes[].name` |
|---|---|
| `MESH_X.glb` | `MESH_X` |
| `MESH_X_LOD1.glb` | `MESH_X_LOD1` |
| `MESH_X_Nanite.glb` | `MESH_X_Nanite` |

Luật này **không có ngoại lệ nào**: script đọc tên file là biết tên datablock, đúng cho
mọi tổ hợp `--mesh-quality` × `--nanite`, và duy nhất trên toàn scene vì `ObjectName`
đã duy nhất theo asset.

Thông tin bị mất — "file `MESH_X.glb` này thực ra là LOD nguồn số 3" — thuộc về manifest
(§7.5), không thuộc về tên. Tên dùng để định danh; metadata thuộc về manifest.

> **Bản 1 sai ở đâu.** Bản 1 đề xuất `<ObjectName>_LOD{n}`, "luôn có hậu tố kể cả khi
> chỉ một LOD". Quy tắc đó **không chặn được chính vụ va chạm nó sinh ra để chặn**: một
> mesh vừa có LOD thường vừa có Nanite — điều mà `--nanite nanite-first`/`nanite-last`
> tạo ra — cho hai file `MESH_X.glb` và `MESH_X_Nanite.glb` mà cả hai đều chứa
> `MESH_X_LOD0`, vì cả hai đều là LOD nguồn 0. Ngoài ra `<ObjectName>_LOD{n}` không
> khớp tên file, nên script phải mang một luật chuyển đổi có ngoại lệ — và ngoại lệ là
> thứ script sẽ làm sai.

### 8.3 Cam kết

- glTF không lỗi theo glTF-Validator chính thức, ở phiên bản đã ghim (§9).
- `|NORMAL| = 1` với mọi vertex.
- Material có texture khi phân loại được, và URI luôn trỏ tới file mà chính lần chạy đó ghi ra.
- `alphaMode` / `alphaCutoff` / `doubleSided` phản ánh đúng material UE.
- `COLOR_0` là vertex color thật, đã chuẩn hoá về 0..255.
- Tên datablock mesh = tên file không đuôi (§8.2).
- Manifest có `version`; đổi schema thì tăng số.
- Exit code theo bảng hiện có (0, 1, 2, 3, 4, 5, 6, 7, 8).

### 8.4 Không cam kết

Ghi rõ để không ai xây script lên trên:

- Byte của texture **không** ổn định giữa các phiên bản CUE4Parse.
- Thứ tự dòng NDJSON **không** xác định — dùng manifest nếu cần thứ tự.
- Thứ tự mảng `nodes` trong glTF **không** cam kết. `skins[].joints` thì có.
- Manifest **không** cho biết LOD nguồn của từng file (§7.5).
- File `.wem` / `.binka` là byte thô, **không** phát được trực tiếp (§7.4).
- Delta của morph target **không** phải vector đơn vị — đó là đúng spec glTF (§6.1).

---

## 9. Kiểm chứng

**Điều kiện tiên quyết.** Trước bất kỳ test nào ở bảng dưới:

1. Sửa `ExportSession.ResolveOutputPath` (§6.3) — không có nó, export trên Linux ghi
   sai tên file và mọi assert về cấu trúc đầu ra đều vô nghĩa.
2. Nối `CUE4Parse.Cli.Tests` vào CI. Hiện `.github/workflows/tests.yml` chỉ chạy
   `CUE4Parse.Tests`, và `CUE4Parse.Cli.Tests` có trong `.slnx` nhưng **không** trong
   CI. Thêm bằng **file workflow mới** (`cli-tests.yml`), không sửa `tests.yml` — file
   đó do upstream giữ, sửa nó là mở rộng bề mặt xung đột `git pull --rebase` vượt quá
   hai file đã cam kết giới hạn.
3. Lấy glTF-Validator bằng **binary standalone chính thức từ GitHub releases của
   KhronosGroup/glTF-Validator, ghim đúng một phiên bản**. Gói npm `gltf-validator`
   phơi ra API JavaScript chứ không phải lệnh CLI, nên "một bước npm" thực tế là npm
   cộng một driver JS tự viết — đúng thứ cần tránh. Ghim phiên bản là bắt buộc: lời hứa
   "không lỗi theo validator" chỉ có nghĩa khi validator không tự đổi dưới chân mình.

| Loại | Nội dung |
|---|---|
| Unit | `GltfMaterialBinder`: ánh xạ từng kênh; URI vượt cấp (`../Common/...`); percent-encode tên có khoảng trắng; kênh thiếu thì bỏ trống chứ không bịa; `alphaMode`/`alphaCutoff`/`doubleSided` theo từng `EBlendMode` |
| Unit | `TextureFileNamer`: HDR → `.hdr`; all-mips → `_MIP{n}`; `UTexture2DArray` → `_LAYER{i}`; và **cùng kết quả với `TextureExporter` trên mọi tổ hợp** |
| Unit | `OrmTextureExporter`: G/B hoán vị đúng; `ObjectPath` khác `TextureExporter` nên cả hai cùng qua được dedupe |
| Vét cạn | BC1/BC3/BC4/BC5 trên mọi tổ hợp endpoint/index, so số học chính xác, **lệch = 0** |
| Cấu trúc glTF | Trên fixture UE5_8: `\|NORMAL\| = 1` toàn bộ vertex; có `images`/`textures`; mọi URI phân giải được tới file thật; `alphaMode` đúng với material masked/translucent; tên datablock = tên file |
| Chuẩn | glTF-Validator ghim phiên bản, trong CI. "Đúng chuẩn" là lời hứa cốt lõi nên không tự viết validator rút gọn |
| Audio | Assert bằng **magic bytes** — `.wem` bắt đầu bằng `RIFF`, `.ogg` bằng `OggS`, `.wav` bằng `RIFF`+`WAVE`. Bắt được lớp lỗi thật sự có khả năng xảy ra (lấy nhầm chunk, lệch offset streaming, nhãn định dạng sai) |
| Fixture mới | Một animation nén ACL, một audio Wwise — không có thì không thể tuyên bố thay được FModel |
| Hồi quy | `tools/parity/` + một manifest baseline commit vào repo cho fixture UE5_8 |

Test tích hợp phải export vào **thư mục tạm mới mỗi lần chạy** — nếu không, file còn sót
từ lần chạy trước sẽ làm assert "mọi URI phân giải được tới file thật" xanh giả.

Dùng fixture UE5_8 sẵn có, **không** dùng asset Stellar Blade: asset game không phân phối lại được, và CI chạy trên Linux.

Bộ script đối chiếu chuyển từ scratchpad vào `tools/parity/`: `PixDiff.ps1` (histogram lệch có dấu theo kênh), `glbcmp.py` (so từng thuộc tính vertex theo primitive, có remap joint theo tên), `GlbInfo.ps1` (tách chunk JSON/BIN), `BinDiff.ps1` (định vị đoạn byte lệch).

---

## 10. Tài liệu

| File | Việc |
|---|---|
| `docs/cue4-guide.md` | Chuyển từ `C:\tools\cue4_guide.txt`, sửa §10 đã lạc hậu ("chưa thử trên bản game retail" — nay đã có bằng chứng ngược lại), bổ sung cờ mới |
| `docs/cue4-output-contract.md` | Mới, nội dung §8; kèm ghi chú USD/glTF ép PNG (§5.4) và WEM/BinkA cần vgmstream (§7.4) |
| `docs/reports/2026-08-16-cue4-cli-vs-fmodel-parity.md` | **Giữ nguyên mục D1** — nó đúng. Viết lại phần khung theo §2: FModel là ảnh chụp cũ của cùng thư viện, nên mọi chênh lệch phải tra bằng diff phiên bản. Hàng `COLOR_0` cần suy ra lại theo cách đó |
| `CLAUDE.md` | Bổ sung `CUE4Parse.Cli`, `tools/parity/`, và hợp đồng đầu ra |

---

## 11. Chia pha

| Pha | Nội dung | Kết quả dùng được |
|---|---|---|
| **0** | §6.3 dấu phân cách đường dẫn · `cli-tests.yml` · ghim glTF-Validator | **Từ đây mới kiểm chứng được bất cứ thứ gì.** Không có Pha 0 thì tiêu chí 1, 2, 3 không chạy được trong CI |
| **1** | §6.1 chuẩn hoá NORMAL + sửa morph delta · §5 `GltfMaterialBinder` · §5.6 `OrmTextureExporter` · §4.5 `MeshExportContext` · §5.4 `TextureFileNamer` + luật format · §8.2 quy ước tên · §8 hợp đồng · test unit + cấu trúc glTF + validator | **`.glb` vào Blender có texture** — gỡ nút thắt chính |
| **2** | §7.1 cờ còn thiếu · §7.2 `--flip-normal-y` · §7.3 cảnh báo Nanite · §7.5 manifest (+ `ExportResult.ClassName`) · §7.6 `info` mở rộng | Script bpy chạy tự động được |
| **3** | §7.4 audio · ACL + fixture animation · §6.2 BC làm tròn + test vét cạn · §10 tài liệu hoàn chỉnh | Thay hẳn FModel |

Mỗi pha kết thúc ở một trạng thái dùng được, không phải nửa vời.

---

## 12. Tiêu chí nghiệm thu

1. `cue4 export` một skeletal mesh có material nhiều lớp → `.glb` chứa `images` và `textures`; mọi URI phân giải được tới file có thật trên đĩa.
2. glTF-Validator (phiên bản đã ghim) chạy trên output không báo lỗi nào.
3. `|NORMAL| = 1` (sai số < 1e-6) trên toàn bộ vertex của mọi fixture.
4. Material có `BLEND_Masked` cho ra `alphaMode: "MASK"` kèm `alphaCutoff` bằng `OpacityMaskClipValue` của material đó.
5. `metallicRoughnessTexture` trỏ tới ảnh ORM đã repack, và kênh G của ảnh đó bằng kênh B của `SpecularMasks` gốc (§5.6).
6. Hai lần chạy `export` liên tiếp cùng tham số cho ra manifest **giống hệt nhau từng byte**.
7. Export chạy đúng trên Linux: cây thư mục đầu ra khớp `<package path>/<ObjectName>.<ext>`, không có file nào mang `\` trong tên.
8. `cue4 info` báo `acl: true` sau khi dựng lại native; một animation nén ACL xuất thành công. Khi `paksDir` sai, `info` vẫn in được khối `native` và trả exit 4.
9. Một asset audio Wwise xuất ra file `.wem` có magic `RIFF` và kích thước khớp chunk nguồn.
10. Test vét cạn BC1/BC3/BC4/BC5 xanh với ngưỡng lệch = 0.
11. Morph target xuất ra có delta **không** chuẩn hoá, và nằm ở khe normal chứ không phải khe tangent.
12. `docs/cue4-output-contract.md` mô tả đủ để viết script bpy mà không cần đọc mã nguồn.
