"""Import every .glb in a cue4 export into one Blender scene, named from the files.

    blender -b -P docs/examples/import_glb.py -- <export directory>

Written entirely against docs/cue4-output-contract.md — no CUE4Parse source consulted.
Writing it is the check on that document: every question it had to answer is answered
there. Not run in CI; tools/blender/check_import.py is the gate.
"""
import pathlib
import sys

import bpy

directory = pathlib.Path(sys.argv[sys.argv.index("--") + 1])

bpy.ops.wm.read_factory_settings(use_empty=True)

missing = []
imported = 0

# Contract §2: the first exported LOD carries no suffix, later ones are _LOD{n} or
# _Nanite. Importing only the unsuffixed files gives one mesh per asset.
for path in sorted(directory.rglob("*.glb")):
    stem = path.stem
    if "_LOD" in stem or stem.endswith("_Nanite"):
        continue

    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    imported += 1

    # Contract §5: meshes[].name, the root node name and scenes[].name all equal the
    # file name without its extension, so the datablock is addressable from the path.
    for obj in set(bpy.data.objects) - before:
        if obj.data is not None and getattr(obj.data, "name", None) == stem:
            obj.name = stem

# Contract §4: textures are referenced by relative URI, never embedded, so a missing
# sibling file shows up as an image that will not decode. Blender loads image buffers
# lazily — read .size to force the load before asking whether it worked.
for image in bpy.data.images:
    if image.name == "Render Result":
        continue
    width, height = tuple(image.size)
    if not image.has_data or width == 0 or height == 0:
        missing.append(f"{image.name} -> {image.filepath}")

print(f"imported {imported} file(s), {len(bpy.data.objects)} object(s), {len(bpy.data.images)} image(s)")
if missing:
    print("textures that did not resolve:")
    for entry in missing:
        print(f"  {entry}")
