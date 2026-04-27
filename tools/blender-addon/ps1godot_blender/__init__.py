# PS1Godot Blender add-on — Phase 1 skeleton.
#
# Goal: tag Blender objects with the same PS1 authoring metadata
# (MeshRole / DrawPhase / ShadingMode / AlphaMode / TexturePageId / …)
# that the PS1Godot exporter consumes, validate scenes against PS1
# constraints in-place, and ship JSON sidecars Godot can ingest without
# reinventing the FBX/GLB pipeline.
#
# Architecture (per docs/ps1godot_blender_addon_integration_plan.md):
#   PS1Godot remains the authoritative final exporter / runtime pipeline.
#   This add-on writes source/intermediate assets + metadata JSON; Godot
#   reads those alongside the .blend-derived mesh and produces the final
#   splashpack. Round-trip is the explicit design goal — every metadata
#   field that travels Blender → Godot must travel back unchanged.
#
# Phase 1 scope (this file + properties.py + two panels + one operator):
#   - bl_info + register/unregister plumbing
#   - PropertyGroups for Scene + Object metadata
#   - PS1Godot Project panel (3D view N-panel)
#   - PS1 Asset Metadata panel (per-object)
#   - PS1GODOT_OT_validate_scene operator stub
#
# Phases 2+ (not yet implemented; tracked in the integration plan):
#   - JSON sidecar export
#   - Vertex-color lighting tools
#   - Collision-helper authoring
#   - Animation metadata + event markers
#   - PS1Godot manifest import / round-trip
#
# Tested against Blender 4.x (matches the 4.7-dev Godot we author in).

bl_info = {
    "name": "PS1Godot",
    "author": "PS1Godot project",
    "version": (0, 1, 0),
    "blender": (4, 0, 0),
    "location": "View3D > Sidebar > PS1Godot",
    "description": "Tag, validate, and export Blender assets for PS1Godot / psxsplash.",
    "category": "Import-Export",
    "doc_url": "https://github.com/anthropics/claude-code",  # placeholder; swap for repo URL once published
    "tracker_url": "",
}

import bpy

from . import properties
from .panels import project_panel, metadata_panel
from .operators import validate_scene

# Order matters: properties must register before panels that draw them.
_modules = (
    properties,
    validate_scene,
    project_panel,
    metadata_panel,
)


def register():
    for m in _modules:
        m.register()
    properties.attach_pointers()


def unregister():
    properties.detach_pointers()
    # Reverse registration order so PropertyGroup teardown happens last.
    for m in reversed(_modules):
        m.unregister()


if __name__ == "__main__":
    register()
