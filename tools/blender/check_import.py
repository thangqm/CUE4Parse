"""Import every .glb under argv[0] and assert its textures actually resolve.

Run as: blender -b -P tools/blender/check_import.py -- <directory>
Fails loudly: any raise leaves Blender with a non-zero exit code, which is the assertion.

This is the only check in the suite that runs the real consumer. glTF-Validator proves a
file is spec-legal; it does not resolve relative URIs the way Blender's importer does, does
not decode PNG payloads, and has no concept of a shader node. Those are exactly the three
ways to get a white mesh out of a file with zero validator errors.
"""
import pathlib
import sys

import bpy

directory = pathlib.Path(sys.argv[sys.argv.index("--") + 1])
files = sorted(directory.rglob("*.glb"))
if not files:
    raise SystemExit(f"no .glb found under {directory}")

for path in files:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(path))

    if not bpy.data.objects:
        raise SystemExit(f"{path.name}: imported no objects")

    stem = path.stem
    if not any(mesh.name == stem for mesh in bpy.data.meshes):
        names = ", ".join(mesh.name for mesh in bpy.data.meshes)
        raise SystemExit(f"{path.name}: no mesh datablock named {stem!r}; got: {names}")

    for image in bpy.data.images:
        if image.name == "Render Result":
            continue

        # Blender loads image buffers lazily, so has_data is False on a freshly imported
        # image that is perfectly fine. Reading .size forces the load; checking has_data
        # before that reports every texture as broken. Both are asserted, in this order.
        width, height = tuple(image.size)
        if not image.has_data or width == 0 or height == 0:
            raise SystemExit(
                f"{path.name}: image {image.name!r} ({image.filepath}) "
                f"did not decode (size {width}x{height}, has_data={image.has_data})")

    print(f"OK  {path.name}: {len(bpy.data.objects)} objects, {len(bpy.data.images)} images")
