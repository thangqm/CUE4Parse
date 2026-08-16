# Báo cáo đối chiếu `cue4` CLI ↔ FModel 4.4.4

**Ngày:** 2026-08-16
**Mục tiêu:** xác minh `cue4 export` có thay thế được FModel trong workflow tự động hay không, và liệt kê đúng những gì cần sửa / bổ sung để bỏ hẳn FModel.
**Phạm vi kiểm chứng:** Stellar Blade (Steam), 4 skeletal mesh mod + toàn bộ material/texture đi kèm — 54 file đối chiếu byte-for-byte, 583.517 vertex, 2.828.586 index.

> Trạng thái tổng: **40/54 file trùng byte tuyệt đối; 14 file khác.** Toàn bộ khác biệt nằm ở **CUE4Parse `master`**, không phải ở nhánh CLI (`git diff master...worktree-feat-cue4-cli` chỉ chạm đúng `CUE4Parse/Utils/CUE4Parse-Natives.cs`, +7/−3). Hình học, UV, tangent, skinning, index và material JSON khớp 100%.

---

## 1. Cách tái lập

### 1.1 Tham số FModel thật (không đoán)

| Nguồn | Giá trị |
|---|---|
| `C:\tools\Output\Logs\FModel-Log-2026-08-07.log` | `GAME_UE4_LATEST (DesktopMobile) \| Archives: x167 \| AES: x0 \| Loose Files: x7` |
| `%APPDATA%\FModel\AppSettings.json` → `UeVersion` | `68943872` = `0x041C0000` = `GAME_UE4_28` = `GAME_UE4_LATEST` |
| `GameDirectory` | `C:\Program Files (x86)\Steam\steamapps\common\StellarBlade\SB` |
| FModel version | `4.4.4.0 (8b95b403bbf16f935f6e20d326a508ec4655500d)`, .NET 8 |

**Lưu ý quan trọng:** FModel **không** dùng `GAME_StellarBlade`. Đã chạy đối chứng cả hai — `GAME_UE4_LATEST` và `GAME_StellarBlade` cho ra **90/90 file giống hệt nhau**, nên với bộ asset này lựa chọn version không ảnh hưởng. Nhưng để tái lập đúng thì dùng `GAME_UE4_LATEST`.

### 1.2 Bảng ánh xạ AppSettings.json → cờ CLI

| FModel setting | Giá trị | Enum | Cờ `cue4` |
|---|---|---|---|
| `MeshExportFormat` | 1 | `EMeshFormat.Gltf2` | `--mesh-format gltf2` |
| `TextureExportFormat` | 0 | `ETextureFormat.Png` | `--texture-format png` |
| `TexturePlatform` | 0 | `ETexturePlatform.DesktopMobile` | `--texture-platform desktop` |
| `LodExportFormat` | 0 | `EMeshQuality.Highest` | `--mesh-quality highest` |
| `SocketExportFormat` | 1 | `ESocketFormat.Bone` | `--socket-format bone` |
| `MaterialExportFormat` | 0 | `EMaterialDepth.TopLayerOnly` | `--material-depth top-layer-only` |
| `NaniteMeshExportFormat` | 0 | `ENaniteMeshFormat.NaniteOnly` | ⚠️ **không khớp default CLI** (`no-nanite`) — xem [G7](#g7) |
| `CompressionFormat` | 2 | `EFileCompressionFormat.ZSTD` | ❌ **CLI không có cờ** — xem [G1](#g1) |
| `SaveMorphTargets` | true | `exportMorphTargets` | ❌ không có cờ (default `true`, tình cờ khớp) |
| `SaveHdrTexturesAsHdr` | true | `exportHdrTexturesAsHdr` | ❌ không có cờ (default `true`, tình cờ khớp) |
| `SaveEmbeddedMaterials` | true | `exportMaterials` | mặc định bật (`--no-materials` để tắt) |
| `KeepDirectoryStructure` | true | — | hành vi mặc định của `export` |

### 1.3 Lệnh đã chạy

```powershell
$P = "SB/Content/Art/Character/PC/CH_P_EVE_09/Lemi21_Mods"
cue4 export `
  "$P/15_SexyCop/MESH_SexyCop_Gold.uasset" `
  "$P/12_Cyber/Mesh_Cyber_Gold.uasset" `
  "$P/3_SkimpyNurse/SkimpyNurse_Mesh_Gold.uasset" `
  "$P/8_Secretary/WSec_Mesh_Gold.uasset" `
  --paks "C:\Program Files (x86)\Steam\steamapps\common\StellarBlade\SB" `
  --game GAME_UE4_LATEST -o <out> `
  --mesh-format gltf2 --texture-format png --texture-platform desktop `
  --mesh-quality highest --socket-format bone --material-depth top-layer-only
```

Exit 0, `72 ok / 2 skipped` (`ClothConfigNv`, `SkimpyNurse_Mesh_Clothing_0` — FModel cũng không xuất, không phải lỗi).

### 1.4 ⚠️ Cảnh báo về dữ liệu chuẩn (ground truth)

Trong `C:\tools\Output\Exports\...\Lemi21_Mods\`, các thư mục **`Common/`, `14_ShadowOps/`, `6_GamerGirl/`** có timestamp **2026-08-16 13:45:08–13:45:13** (cả loạt trong 5 giây, không có `.uasset` thô đi kèm) — **không phải** sản phẩm của phiên FModel ngày 07–08/08. Bằng chứng phụ: `L21_Body_N.png` (BC5) trong `Common/` trùng byte với cue4, trái với quy luật BC5 luôn lệch ở mọi file FModel thật.

→ Chỉ **`12_Cyber`, `15_SexyCop`, `3_SkimpyNurse`, `8_Secretary`** (+ `Common/Materials/L21_Heels_White.json` và `Common/Materials/BodyTextures/L21_Heels_Color_2.png` đề ngày 08/08) được dùng làm chuẩn. **Nếu ghi đè thư mục Exports thì phải giữ lại bản FModel gốc để còn đối chiếu về sau.**

---

## 2. Kết quả tổng hợp

### 2.1 Khớp tuyệt đối (40/54)

- **Toàn bộ 9 material `.json`** — byte-for-byte.
- **Toàn bộ texture DXT1/BC1** (11 file, tới 4096×4096) — byte-for-byte.
- **BC5 kích thước nhỏ/phẳng** (`CyberGlasses_N` 512×512) — byte-for-byte.
- Trong cả 4 glb: `POSITION`, `TEXCOORD_0`, `TANGENT`, `JOINTS_0`, `WEIGHTS_0`, `INDICES` — **0,00% sai khác**.
- `skins[0].joints`: thứ tự và toàn bộ 201 tên xương **giống hệt**.
- Số primitive mỗi mesh giống hệt (30 / 26 / 29 / 24).

### 2.2 Khác biệt (14/54)

| File | Loại | Nhóm nguyên nhân |
|---|---|---|
| `Cyber_N.png`, `CyberArm_N.png`, `CyberBoots_N.png`, `SexyCop_N.png`, `L21_SN_Normal.png`, `WSec1_N.png`, `WSec2_N.png`, `WSec3_N.png` | BC5 | [D1](#d1) |
| `WSec2_C.png`, `WSec3_C.png` | DXT5/BC3 | [D1](#d1) |
| `MESH_SexyCop_Gold.glb`, `Mesh_Cyber_Gold.glb`, `SkimpyNurse_Mesh_Gold.glb`, `WSec_Mesh_Gold.glb` | glTF | [D2](#d2) + [D3](#d3) + [D4](#d4) |

---

## 3. Chi tiết khác biệt

### D1 — Decoder BC5 / BC3 lệch làm tròn 1 đơn vị {#d1}

**Mức độ:** thấp (khác biệt ±1/255, mắt thường không thấy) — nhưng **phá vỡ mọi so sánh hash/CI**.

**Bằng chứng đo được** (histogram lệch dấu, `cue4 − fmodel`, mẫu `CyberBoots_N` 2048² và `WSec2_C` 2048²):

```
BC5  (CyberBoots_N):  R: +1 ×605.914 (100% lệch đều)   G: +1 ×579.009 (100% lệch đều)
                      B: ±1…±6, max 16 (kênh Z tái dựng từ R,G nên khuếch đại)   A: giống hệt
DXT5 (WSec2_C):       R: −1 ×396.735   G: −1 ×652.288   B: −1 ×416.768   A: giống hệt
DXT1 (mọi file):      giống hệt
```

R/G lệch **đúng +1 ở 100% pixel khác nhau, không bao giờ −1** → dấu hiệu chắc chắn của thay đổi làm tròn, không phải lỗi giải nén dữ liệu.

**Nguyên nhân gốc:** hai commit upstream đã có trên `master`:

- `266d435c` (2026-08-01) *Refactor BC4 and BC5 decoders*
- `ea938ba8` (2026-08-04) *Optimize BC1–BC5 decoders for 3–5× speedup*

thay phép chia nguyên bằng fixed-point **có số hạng làm tròn**. Xem [`CUE4Parse-Conversion/Textures/BC/BCDecoder.cs`](../../CUE4Parse-Conversion/Textures/BC/BCDecoder.cs):

- `DecodeBCColors` (dùng cho **BC4, BC5, và kênh alpha của BC3**): `var temp0 = 6 * c0 + c1 + 3;` rồi `(temp0 * 9365) >> 16` — cộng `+3` (và `+2` ở nhánh 6-giá-trị) = làm tròn tới gần nhất. Bản cũ chia nguyên (cắt xuống).
- `ReadColorsBC3` (phần màu của DXT5): `((2 * r0 + r1) * 683) >> 11` — cắt xuống, trong khi bản FModel làm tròn → sinh lệch **−1**.
- `ReadColorsBC1` (DXT1): khớp hoàn toàn, không cần đụng.

> **⚠️ ĐÍNH CHÍNH (bổ sung sau khi đọc kỹ code).** Phiên bản đầu của mục này kết luận rằng chênh lệch −1 ở DXT5 là **hồi quy** của commit tối ưu decoder. **Kết luận đó sai.** `ReadColorsBC1` và `ReadColorsBC3` trong code hiện tại dùng **cùng một biểu thức** `((2*r0 + r1) * 683) >> 11` (cắt xuống). Vậy mà DXT1 khớp FModel còn DXT5 lệch −1 → **FModel mới là bên không nhất quán**: nó cắt xuống ở BC1 nhưng làm tròn ở BC3. Code hiện tại tự nhất quán giữa BC1 và BC3.

Điểm không nhất quán *thật sự còn lại* trong code hiện tại là giữa **(BC1, BC3: cắt xuống)** và **(BC4, BC5: làm tròn qua số hạng `+3`/`+2`)**.

**Đề xuất cho agent — không đổi công thức:**
1. **Giữ nguyên decoder.** Sai lệch tối đa ±1/255 ở cả hai cách, không cách nào gây lỗi. Đổi BC1 sang làm tròn sẽ làm mọi texture DXT1 đã xuất trước đây đổi byte — churn thật, lợi ích không nhìn thấy.
2. **Khoá hành vi bằng test vét cạn**: mọi tổ hợp endpoint/index cho BC1/BC3/BC4/BC5, so với số học chính xác, assert sai lệch ≤ 1. Chỉ sửa công thức nếu test lộ ra sai lệch > 1 — tức là sửa khi có bằng chứng.
3. Ghi vào tài liệu rằng byte texture **không** ổn định giữa các phiên bản CUE4Parse, nên pipeline đừng bao giờ so hash texture giữa hai version khác nhau.

---

### D2 — glTF `NORMAL` không phải vector đơn vị (vi phạm spec) {#d2}

**Mức độ:** trung bình — đây là **lỗi thật**, nên ưu tiên sửa.

**Bằng chứng:**

```
cue4    |NORMAL|      min=0.998307  max=0.998629     |TANGENT.xyz| = 1.000000
fmodel  |NORMAL|      min=1.000000  max=1.000000     |TANGENT.xyz| = 1.000000
```

Lệch tối đa mỗi thành phần: `0.00169` (≈0,1°) — đủ nhỏ để không ai nhìn thấy, nhưng **spec glTF 2.0 bắt buộc `NORMAL` phải chuẩn hoá**, và glTF-Validator sẽ báo `ACCESSOR_VECTOR3_NON_UNIT`.

**Nguyên nhân gốc — đã truy đến dòng:**

[`CUE4Parse-Conversion/Writers/Gltf/Gltf.cs:236-241`](../../CUE4Parse-Conversion/Writers/Gltf/Gltf.cs)

```csharp
public static FVector SwapYZAndNormalize(FVector vec)   // dùng cho NORMAL
{
    var res = SwapYZ(vec);
    res.Normalize();          // -> FVector.Normalize() -> MathUtils.InvSqrt()
    return res;
}

public static Vector4 SwapYZAndNormalize(Vector4 vec)   // dùng cho TANGENT
{
    return Vector4.Normalize(new Vector4(vec.X, vec.Z, vec.Y, vec.W));  // System.Numerics, chính xác
}
```

[`CUE4Parse/Utils/MathUtils.cs:30`](../../CUE4Parse/Utils/MathUtils.cs) là **fast inverse square root kiểu Quake III**:

```csharp
i = 0x5f3759df - (i >> 1);
x = x * (1.5f - xhalf * x * x);   // chỉ 1 vòng Newton -> sai số tương đối tối đa ~0,175%
```

0,175% khớp chính xác với `|n| ≈ 0,9983` đo được. Nhánh `Vector4` dùng `System.Numerics` nên tangent đúng — đó là lý do TANGENT trùng 100% còn NORMAL thì không.

**Đề xuất cho agent:**
1. Sửa `Gltf.SwapYZAndNormalize(FVector)` dùng chuẩn hoá chính xác, ví dụ `Vector3.Normalize(new Vector3(vec.X, vec.Z, vec.Y))`, thay vì `FVector.Normalize()`.
2. **Không** đổi `MathUtils.InvSqrt` — nó cố tình mô phỏng `FMath::InvSqrt` của UE, đổi đi sẽ ảnh hưởng chỗ khác. Chỉ đổi call site trong exporter.
3. Rà các exporter khác dùng `FVector.Normalize()` trên đường ghi file (`UEFormat`, `USD`, `ActorX`) — nhiều khả năng cùng bệnh.
4. Thêm test: sau khi export glTF, assert `abs(|normal| − 1) < 1e-6` cho toàn bộ vertex.
5. *(Ghi chú, chưa gây lỗi quan sát được — chỉ để rà)*: nhánh `Vector4` chuẩn hoá cả thành phần `W` (handedness ±1) chứ không chỉ `xyz`. Output vẫn khớp FModel nên chưa phải sửa gấp, nhưng đáng xem lại ý đồ.

---

### D3 — glTF `COLOR_0`: **cue4 đúng, FModel sai** ✅ {#d3}

**Mức độ:** đây là **ưu điểm** của CLI, ghi lại để không ai "sửa ngược" cho giống FModel.

Phân bố giá trị thực đo trên `WSec_Mesh_Gold.glb` (189.378 vertex):

```
cue4   : (0,254,0,255) ×184.651   (254,254,254,255) ×4.654   (0,0,0,255) ×47   (0,254,0,254) ×26
fmodel : (0,0,0,1)     ×189.352   (0,0,0,0) ×26
```

FModel chỉ ra được đúng 2 giá trị trong tập `{0, 1}` — dấu vết đặc trưng của **float 0..1 bị ép thẳng sang byte không nhân 255** (`0.996 → 0`, `1.0 → 1`, và đúng 26 vertex có alpha `0.996 → 0`). Vertex color trong FModel 4.4.4 **hỏng**; cue4 ghi đúng giá trị đã chuẩn hoá.

**Hành động:** không sửa. Ghi vào tài liệu migration để người dùng biết vertex color sau khi chuyển sang CLI sẽ **khác và tốt hơn** — mọi pipeline DCC đang lệ thuộc vertex color của FModel cần được kiểm tra lại.

---

### D4 — Khác biệt hình thức trong glTF {#d4}

**Mức độ:** thấp; cần quyết định giữ hay đổi cho nhất quán.

| Mục | cue4 | FModel 4.4.4 | Ghi chú |
|---|---|---|---|
| `asset.generator` | `SharpGLTF 1.0.0+5153478a` | `SharpGLTF 1.0.0-alpha0023` | khác phiên bản thư viện |
| `meshes[0].name` | `LOD0` | `MESH_SexyCop_Gold` | `Gltf.cs:32,49` đặt tên `$"LOD{lod.SourceLodIndex}"` |
| `nodes[0].name` | `...ao_LOD0` | `...ao` | thêm hậu tố `_LOD0` |
| `scenes[0].name` | có | không có | |
| `accessors` `TANGENT`/`WEIGHTS_0` | không có `min`/`max` | có | spec chỉ bắt buộc cho `POSITION` — **hợp lệ cả hai** |
| Thứ tự mảng `nodes` | khác | khác | **`skins[0].joints` giống hệt**, mọi tên xương khớp → không ảnh hưởng rig |
| Một số quaternion | dấu ngược | | tương đương toán học |

**Đề xuất:** khoá tên mesh/node thành một quy ước dứt khoát và ghi vào tài liệu — vì tên node là thứ script Blender/Maya hay bám vào. Nếu `--mesh-quality all` thì hậu tố `_LOD{n}` là cần thiết; cân nhắc chỉ thêm hậu tố khi xuất nhiều LOD.

---

## 4. Ưu điểm của `cue4` CLI so với FModel

1. **stdout thuần JSON / NDJSON, log ra stderr** — script hoá không cần parse text người đọc.
2. **Exit code phân loại rõ** (0 ok, 2 usage, 3 config, 4 mount, 5 AES, 6 mappings, 7 not found, 8 partial export) — CI phân nhánh được mà không đọc message.
3. **Mount một lần cho nhiều path** — mount là phần chậm nhất; 4 mesh + toàn bộ material trong một tiến trình.
4. **Kết quả tất định** — chạy lại với `--game` khác nhau vẫn cho 90/90 file giống hệt; không có timestamp/uuid nhúng trong output → hash được, cache được.
5. **Chính xác hơn FModel ở vertex color** ([D3](#d3)).
6. **Hình học tuyệt đối tin cậy** — position/UV/tangent/skinning/index khớp 100% trên 583k vertex.
7. **Material JSON và mọi texture DXT1 trùng byte** với FModel.
8. **Guard 1000 asset** + `--limit`/`--force` — chặn lỡ tay quét cả game.
9. **Config profile** (`cue4.json`, `%APPDATA%\cue4\cue4.json`) — bỏ được cờ lặp lại.
10. **Headless, self-contained, không cần .NET runtime** — chạy được trong container/CI.
11. `list` không deserialize → khảo sát pattern rất nhanh trước khi làm việc thật.

---

## 5. Nhược điểm / thiếu sót cần bổ sung

### G1 — ExportOptions chưa expose hết qua CLI {#g1}

[`CUE4Parse.Cli/Services/ExportOptionsMapper.cs:69-79`](../../CUE4Parse.Cli/Services/ExportOptionsMapper.cs) bỏ trống 3 tham số của [`ExportOptions`](../../CUE4Parse-Conversion/Options/ExportOptions.cs):

| Tham số | Default hiện tại | FModel | Hậu quả |
|---|---|---|---|
| `compressionFormat` | `None` | `ZSTD` (=2) | ⚠️ **Khi dùng `--mesh-format ueformat`, cue4 ghi file không nén còn FModel ghi ZSTD** → file to hơn nhiều và khác hoàn toàn. Đây là mismatch thật, chỉ chưa lộ vì test này dùng gltf2. |
| `exportMorphTargets` | `true` | `true` | tình cờ khớp, nhưng không tắt được |
| `exportHdrTexturesAsHdr` | `true` | `true` | tình cờ khớp, nhưng không tắt được |

**Việc cần làm:** thêm `--compression-format none|gzip|zstd`, `--no-morph-targets`, `--no-hdr` vào `ExportFlags` + `Program.cs` + `ExportOptionsMapper`, theo đúng khuôn `AcceptOnlyFromAmong` đang dùng.

### G2 — Không có exporter cho audio

[`CUE4Parse-Conversion/ExportSession.cs:44-62`](../../CUE4Parse-Conversion/ExportSession.cs) không có nhánh nào cho `USoundWave` / Wwise. Mọi asset âm thanh sẽ trả `{"status":"skipped","reason":"no exporter for this type"}`. FModel làm được (bnk/wem → wav/ogg, có `CompressedAudioMode`, `WwiseMaxBnkPrefetch`).

**Việc cần làm:** thêm `SoundExporter` vào `ExportSession.Add`, hoặc một verb riêng `cue4 audio`. **Đây là rào cản lớn nhất để bỏ hẳn FModel** nếu workflow có đụng âm thanh.

### G3 — Animation ACL không hoạt động trong build hiện tại

`cue4_guide.txt` §10 ghi rõ: native library build **thiếu submodule ACL**, nên hầu hết animation của UE đời mới sẽ fail. Chưa kiểm chứng trong đợt này vì bộ test không có animation.

**Việc cần làm:** `git submodule update --init --recursive` rồi build lại native, dựng lại `artifacts/cue4.exe`, và **thêm một test animation ACL vào bộ kiểm chứng** trước khi tuyên bố thay được FModel.

### G4 — `export` không kèm được `.uasset` thô

FModel lưu cả `.glb` **và** `.uasset` (thao tác "Save raw data"). `cue4 export` chỉ ra file đã convert. Tương đương là `cue4 unpack`, nhưng phải chạy lệnh thứ hai → mount lại lần nữa (đi ngược "quy tắc 2" của guide).

**Việc cần làm:** thêm cờ `--with-raw` cho `export` để gộp cả hai trong một lần mount, hoặc ghi rõ trong tài liệu là phải chạy `unpack` riêng.

### G5 — Output chưa đủ để CI verify

NDJSON có `files` (đường dẫn) nhưng **không có kích thước, không có hash**. Muốn kiểm tra "chạy lại có ra đúng như lần trước không" thì phải tự đi quét thư mục.

**Việc cần làm:** thêm `--manifest <file.json>` ghi `{path, objectPath, bytes, sha256}` cho mỗi file xuất ra. Đây là mảnh còn thiếu để dựng regression test tự động.

### G6 — Chưa có bộ parity test tự động

Toàn bộ báo cáo này làm bằng tay. Không có gì ngăn lần sửa decoder tiếp theo lại phá output mà không ai biết.

**Việc cần làm:** thêm vào `CUE4Parse.Tests` các golden-file test cho: BC1/BC3/BC5/BC7 decode, `|NORMAL| == 1`, số lượng và tên `joints`, và cấu trúc thư mục output.

### G7 — Default `--nanite` lệch với FModel {#g7}

CLI mặc định `no-nanite`; FModel (theo `AppSettings.json`) là `NaniteOnly`. Bộ test này là UE4.26 nên không có Nanite → không lộ. **Với game UE5 thì đây là khác biệt output thật sự.**

**Việc cần làm:** quyết định default rồi ghi vào tài liệu; hoặc bắt buộc chỉ định `--nanite` khi mesh có dữ liệu Nanite.

### G8 — Số archive mount khác nhau, chưa giải thích

FModel log: `Archives: x167 | Loose Files: x7`. `cue4 info`: `mountedVfs: 212, unloadedVfs: 1, fileCount: 234340`.
Có thể chỉ là cách đếm khác (pak và utoc tính riêng), **chưa xác minh**. Không ảnh hưởng kết quả đợt này vì asset cần thiết nằm trong `SexyCop_P.utoc` mà cả hai đều mount được.

**Việc cần làm:** thêm `cue4 info --verbose` liệt kê từng archive để đối chiếu 1:1 với log FModel, và làm rõ `unloadedVfs: 1` là archive nào.

### G9 — Tài liệu đã lạc hậu

`C:\tools\cue4_guide.txt` §10 ghi *"Not yet exercised against a full retail game install"* — nay đã có bằng chứng ngược lại (Stellar Blade retail, 212 archive, 234.340 file). Cần cập nhật §10 bằng kết quả trong báo cáo này.

---

## 6. Lộ trình bỏ FModel

**Bắt buộc trước khi bỏ:**

- [ ] **G3** — build lại native có ACL và kiểm chứng animation.
- [ ] **G2** — audio exporter (chỉ khi workflow có dùng âm thanh; nếu không, ghi rõ là "ngoài phạm vi").
- [ ] **D2** — sửa chuẩn hoá `NORMAL`, kèm test.
- [ ] **G1** — expose `--compression-format` (chặn cạm bẫy khi chuyển sang `ueformat`).

**Nên có để workflow tự động ổn định:**

- [ ] **G5** — `--manifest` có hash.
- [ ] **G6** — golden-file test cho decoder + exporter.
- [ ] **D1** — test vét cạn khoá hành vi decoder BC1/BC3/BC4/BC5 (giữ nguyên công thức, xem đính chính ở D1).
- [ ] **G4** — `--with-raw`.
- [ ] **G7**, **G8**, **G9** — chốt default, làm rõ mount, cập nhật tài liệu.

**Đã đạt, không cần làm gì:**

- [x] Hình học, UV, tangent, skinning, index — chính xác tuyệt đối.
- [x] Material JSON — trùng byte.
- [x] Texture DXT1/BC1 — trùng byte.
- [x] Vertex color — chính xác hơn FModel ([D3](#d3)).
- [x] Exit code, JSON output, mount một lần, tính tất định — đủ dùng cho CI.

---

## 7. Phụ lục — script tái lập

Các script dùng để đo nằm ở thư mục scratchpad của phiên:
`%LOCALAPPDATA%\Temp\claude\c--Workspace-CUE4Parse\<session>\scratchpad\`

- `PixDiff.ps1 <a.png> <b.png> <tên>` — histogram lệch có dấu theo từng kênh B/G/R/A.
- `glbcmp.py <a.glb> <b.glb>` — so `POSITION`/`NORMAL`/`TANGENT`/`TEXCOORD_0`/`COLOR_0`/`JOINTS_0`/`WEIGHTS_0`/`INDICES` theo từng primitive, có remap joint theo tên.
- `GlbInfo.ps1 <glb> [json_out]` — tách chunk JSON/BIN, in độ dài, xuất glTF JSON.
- `BinDiff.ps1 <rel>` — định vị các đoạn byte lệch trong chunk BIN.

**Nên đưa các script này vào repo** (ví dụ `tools/parity/`) thay vì để trong scratchpad, để đợt kiểm chứng sau chạy lại được ngay.
