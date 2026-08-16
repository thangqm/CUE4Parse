# Báo cáo đối chiếu `cue4` CLI ↔ FModel 4.4.4

**Ngày:** 2026-08-16 · **Sửa đổi lần 2:** 2026-08-16 (đóng khung lại — xem ngay dưới)
**Mục tiêu:** xác minh `cue4 export` có thay thế được FModel trong workflow tự động hay không, và liệt kê đúng những gì cần sửa / bổ sung để bỏ hẳn FModel.
**Phạm vi kiểm chứng:** Stellar Blade (Steam), 4 skeletal mesh mod + toàn bộ material/texture đi kèm — 54 file đối chiếu byte-for-byte, 583.517 vertex, 2.828.586 index.

> Trạng thái tổng: **40/54 file trùng byte tuyệt đối; 14 file khác.** Toàn bộ khác biệt nằm ở **CUE4Parse `master`**, không phải ở nhánh CLI (`git diff master...worktree-feat-cue4-cli` chỉ chạm đúng `CUE4Parse/Utils/CUE4Parse-Natives.cs`, +7/−3). Hình học, UV, tangent, skinning, index và material JSON khớp 100%.

---

## 0. Đóng khung lại: FModel **không** phải một cách hiện thực độc lập {#framing}

Bản đầu của báo cáo này ngầm coi FModel 4.4.4 là một "bên thứ hai" để đối chiếu, rồi
hỏi *bên nào đúng*. **Câu hỏi đó sai ngay từ đầu.** FModel không tự viết bộ đọc UE —
nó nhúng chính repo này làm submodule. Vậy nên:

> **FModel 4.4.4 = một bản CUE4Parse cũ đã ghim.** Mọi khác biệt đo được trong báo cáo
> này là **delta giữa hai phiên bản của cùng một repo**, không phải delta giữa hai cách
> hiện thực.

**Phiên bản đã ghim (đã tra, không đoán):**

| | |
|---|---|
| FModel | `4.4.4.0`, commit `8b95b403bbf16f935f6e20d326a508ec4655500d` |
| CUE4Parse submodule tại commit đó | **`d2f6ce6e618576dbbe7f6dd9ed3171f14513182a`** ("loading all virtual paths from uplugin files", **2025-12-18**) |

Tra lại bất cứ lúc nào:

```bash
curl -s "https://api.github.com/repos/4sval/FModel/contents/CUE4Parse?ref=<FModel-commit>" \
  | grep '"sha"'
```

### Hệ quả: phương pháp đúng thay cho bảng "bên nào đúng"

Với mỗi dòng còn chưa giải thích được, **không** so đoán hành vi — hãy diff hai phiên bản:

```bash
git diff d2f6ce6e..HEAD -- <đường dẫn liên quan>
```

Áp dụng ngay cho các dòng còn treo:

- **Texture ([D1](#d1))** — `d2f6ce6e` (2025-12-18) **có trước** `ea938ba8` (2026-08-04).
  Kiểm chứng: `git merge-base --is-ancestor ea938ba8 d2f6ce6e` → **sai**. Nghĩa là
  FModel 4.4.4 vẫn dùng `DXTDecoder` cũ, còn `master` đã chuyển sang `BCDecoder`.
  **Toàn bộ chênh lệch texture chính xác là delta của commit viết lại decoder** —
  không còn gì bí ẩn. Xem [`bc-interpolant-rounding.md`](bc-interpolant-rounding.md).
- **`COLOR_0` ([D3](#d3))** — cùng cách làm: `git diff d2f6ce6e..HEAD --
  CUE4Parse-Conversion/Writers/Gltf/` rồi tìm chỗ ép float→byte. Kết luận "cue4 đúng,
  FModel sai" ở D3 vẫn giữ nguyên (dấu vết `{0,1}` là bằng chứng đủ mạnh), nhưng cách
  phát biểu chuẩn hơn là: **bản cũ sai, bản mới đã sửa**, chứ không phải hai công cụ
  bất đồng.
- **Đếm archive 212 vs 167 + 7 loose, `unloadedVfs: 1` ([G8](#g8))** — nay đã có
  `cue4 info --verbose` liệt kê từng archive, nên đối chiếu 1:1 với log FModel được.
  Nhưng lưu ý cùng một lý do: cách *đếm* VFS cũng nằm trong khoảng diff
  `d2f6ce6e..HEAD` của `AbstractVfsFileProvider`, nên đừng giả định hai con số phải
  bằng nhau.

### Điều này **không** làm giảm giá trị phần đo đạc

Delta giữa hai phiên bản vẫn là thứ đáng biết — nó chính là cái mà người dùng chuyển
từ FModel sang cue4 sẽ nhìn thấy. Chỉ có điều nó nên được đọc như **ghi chú nâng cấp**,
chứ không phải như bảng điểm "ai đúng ai sai".

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

> **⚠️ ĐÍNH CHÍNH LẦN 2 (2026-08-16) — bỏ hẳn "đính chính lần 1".**
>
> Đính chính lần 1 nói rằng kết luận "hồi quy" là sai, và rằng **FModel** mới là bên
> không nhất quán (cắt xuống ở BC1 nhưng làm tròn ở BC3). **Chính đính chính đó mới
> sai**, và nó sai vì đọc code `master` rồi suy ra hành vi của FModel — trong khi
> FModel chạy một bản CUE4Parse **cũ hơn** (xem [§0](#framing)).
>
> Bằng chứng git, không phải suy luận:
>
> ```bash
> git show ea938ba8^:CUE4Parse-Conversion/Textures/DXT/DXTDecoder.cs
> #  DXT1      : (2*c0 + c1) / 3          <- cắt xuống
> #  DXT3/DXT5 : (2*c0 + c1 + 1) / 3      <- LÀM TRÒN
> git show ea938ba8 -- CUE4Parse-Conversion/Textures/TextureDecoder.cs
> #  - DXTDecoder.DXT1/DXT3/DXT5  ->  + BCDecoder.BC1/BC2/BC3
> ```
>
> FModel 4.4.4 ghim `d2f6ce6e` (2025-12-18) — **trước** `ea938ba8` (2026-08-04) — nên
> nó chạy đúng `DXTDecoder` ở trên. Bộ đôi "DXT1 khớp tuyệt đối + DXT5 lệch −1" là
> **chính xác điều mà code cũ dự đoán**, chứ không phải dấu hiệu FModel bất nhất.
>
> **Kết luận ban đầu đúng:** `ea938ba8` đã làm BC2/BC3 lặng lẽ đổi từ *làm tròn* sang
> *cắt xuống*, trong một commit mà mục đích công bố chỉ là tăng tốc.

Điểm không nhất quán trong code hiện tại là giữa **(BC1, BC2, BC3: cắt xuống)** và **(BC4, BC5: làm tròn qua số hạng `+3`/`+2`)**.

**Phát hiện thứ hai, tìm ra khi viết test vét cạn (mục 2 bên dưới):** cùng commit đó
còn thay `temp / 5` (chia nguyên, chính xác) bằng `(temp * 1636) >> 13`, và **đẳng thức
này sai**: `1636 × 5 = 8180 < 8192`. Nhánh 6-giá-trị của BC4/BC5 vì thế **thấp hơn
`floor(x/5)` đúng 1 đơn vị** ở 374/1278 giá trị — 79,7% cặp endpoint có ít nhất một
interpolant sai. Hệ số đúng là **1639**. Nhánh `/7` (`9365 >> 16`) thì chính xác trên
toàn dải, nên đây là nhầm lẫn chứ không phải đánh đổi tốc độ. **BC5 là format của
normal map**, nên nó chạm đúng những file lệch nhiều nhất trong bảng đo ở trên.

**Đề xuất cho agent — không đổi công thức (đã thực hiện):**
1. **Giữ nguyên decoder.** Sửa sẽ đổi byte của mọi texture BC cho *mọi* consumer của CUE4Parse; và câu hỏi "có cố ý bỏ làm tròn ở DXT3/DXT5 không" chỉ tác giả `ea938ba8` trả lời được.
2. **Khoá hành vi bằng test vét cạn** — đã có: [`CUE4Parse.Tests/BCDecoderTests.cs`](../../CUE4Parse.Tests/BCDecoderTests.cs), vét cạn toàn bộ không gian endpoint thật (1024 cặp đỏ, 4096 cặp lục, 1024 cặp lam, 65536 cặp alpha), ngưỡng **0** so với *hành vi hiện tại* — không phải "≤ 1" như đề xuất cũ, vì ngưỡng ≤ 1 là ngưỡng mà **cả** công thức cắt xuống lẫn công thức làm tròn đều qua, tức là một test không phân biệt được hai bên.
3. **Báo cáo lên upstream:** [`docs/reports/bc-interpolant-rounding.md`](bc-interpolant-rounding.md) — soạn xong, **chưa gửi**.
4. Ghi vào tài liệu rằng byte texture **không** ổn định giữa các phiên bản CUE4Parse — đã có trong [output contract](../cue4-output-contract.md) §8. Đừng bao giờ so hash texture giữa hai version; hãy so `--manifest`.

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

> **Cập nhật 2026-08-16:** phần lớn lộ trình này đã hoàn thành trong kế hoạch
> [`2026-08-16-cue4-blender-pipeline.md`](../superpowers/plans/2026-08-16-cue4-blender-pipeline.md)
> (Phase 0–3). Trạng thái dưới đây phản ánh thực tế đã kiểm chứng, không phải dự định.

**Bắt buộc trước khi bỏ:**

- [x] **G3** — native đã build có ACL; `cue4 info` báo `library`/`acl`/`oodle` và có test khoá tính trung thực của phần báo cáo đó. **Vẫn chưa kiểm chứng giải nén ACL thật** — bộ fixture không có animation ACL nào và không thể thêm. Xem "Verification gaps" trong output contract.
- [x] **G2** — `SoundExporter` đã có; `USoundWave`/`USoundNodeWave`/`UAkMediaAssetData` xuất ra byte thô, `USoundCue` bị bỏ qua có chủ đích. **cue4 trích xuất chứ không transcode** — `.wem`/`.binka`/`.rada`/`.opus` vẫn cần vgmstream. Không có fixture Wwise nên `.wem` chưa được CI phủ.
- [x] **D2** — `NORMAL` đã chuẩn hoá chính xác, có test `|n| − 1 < 1e-6` và có glTF-Validator trong CI.
- [x] **G1** — đã có `--compression-format`, `--no-morph-targets`, `--no-hdr`.

**Nên có để workflow tự động ổn định:**

- [x] **G5** — `--manifest` (đã sắp xếp, có sha256, hai lần chạy cho manifest trùng byte).
- [x] **G6** — đã có test tự động: glTF-Validator + import Blender headless (bản ghim), test decoder vét cạn, `|NORMAL|`, tên/thứ tự joint, và URI ảnh phải trỏ tới file có thật.
- [x] **D1** — test vét cạn đã khoá hành vi BC1/BC3/BC4/BC5, ngưỡng 0; **phát hiện thêm lỗi hệ số `1636`** (xem D1). Báo cáo upstream đã soạn, **chưa gửi**.
- [ ] **G4** — `--with-raw`: **chưa làm.** Vẫn phải chạy `unpack` riêng, tức mount lần thứ hai.
- [x] **G7** — default `--nanite no-nanite` được giữ, nhưng nay có **cảnh báo khi dữ liệu Nanite bị bỏ**, nên không còn im lặng.
- [x] **G8** — `cue4 info --verbose` liệt kê từng archive. Lưu ý [§0](#framing): cách đếm VFS cũng nằm trong diff `d2f6ce6e..HEAD`, đừng giả định hai con số phải bằng nhau.
- [x] **G9** — tài liệu đã cập nhật: [`docs/cue4-guide.md`](../cue4-guide.md) (đã đưa vào repo) + [`docs/cue4-output-contract.md`](../cue4-output-contract.md).

**Còn treo, cần biết:**

- CI (`.github/workflows/cli-tests.yml`) **chưa từng chạy** — `origin` vẫn trỏ thẳng upstream nên nhánh chưa có remote để đẩy. Lỗi separator đường dẫn chỉ biểu hiện **trên Linux**, nên một lần xanh trên Windows không chứng minh được gì.

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
