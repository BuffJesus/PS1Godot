# JSON sidecar I/O.
#
# Writes the per-object metadata files PS1Godot ingests alongside its
# FBX/GLB import. Format is documented in
# docs/ps1godot_blender_addon_integration_plan.md § 7. Two design
# choices worth recording:
#
#   1. Per-object files (`<mesh_id>.ps1meshmeta.json`) — one file per
#      tagged Object. Editing one mesh's tags doesn't touch every
#      other file's git diff; the import loop matches mesh names 1:1
#      against `source_object_name`.
#
#   2. Schema is open + forward-compatible. Unknown keys round-trip
#      cleanly (Phase 8 importer can re-read what Phase 2 wrote even
#      if Phase 5 added new fields in between). The leading
#      `ps1godot_metadata_version` is the version gate.

import json
import os


SCHEMA_VERSION = 1
SIDECAR_SUFFIX = ".ps1meshmeta.json"


def write_pretty_json(path: str, payload: dict) -> None:
    """Write `payload` to `path` as pretty-printed JSON.

    Uses sort_keys=False so we control the field order (the metadata
    fields lead, materials trail). Creates parent dirs if absent.
    Trailing newline keeps git happy.
    """
    parent = os.path.dirname(path)
    if parent:
        os.makedirs(parent, exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(payload, f, indent=2, sort_keys=False, ensure_ascii=False)
        f.write("\n")


def sidecar_path_for(output_dir: str, mesh_id: str) -> str:
    """Compute the sidecar file path for a given mesh_id.

    Caller is expected to have already slugified mesh_id via
    utils.ids.slugify_name; we don't double-sanitize here so any
    caller-side malformed mesh_id surfaces as a write error rather
    than getting silently rewritten.
    """
    return os.path.join(output_dir, f"{mesh_id}{SIDECAR_SUFFIX}")
