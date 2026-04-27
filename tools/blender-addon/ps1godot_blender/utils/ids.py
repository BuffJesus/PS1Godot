# Stable-ID helpers.
#
# Per docs/ps1godot_blender_addon_integration_plan.md § 5.1: "every
# exported object should have stable logical IDs". This module
# generates IDs on first export and never silently changes them on
# re-export.
#
# Two ID flavours:
#   asset_id  — globally unique handle (UUID hex, opaque). Used as the
#               cross-tool foreign key in resource manifests, save
#               files, dialogue refs. Generated when empty; otherwise
#               preserved verbatim.
#   mesh_id   — human-readable logical identifier (e.g. "town_crate_01").
#               Defaults to the Blender object's name (with the standard
#               name-to-id slugification) when empty; user can override.
#
# The exporter calls ensure_object_ids() once per object before
# writing the sidecar so the .blend file's PropertyGroup is the source
# of truth for next session.

import re
import uuid


# Allow ASCII letters, digits, dot, underscore, dash. Everything else
# becomes underscore. Matches the convention SplashEdit / PS1Godot
# already use for other string IDs (Lua script names, CLUT IDs, etc.).
_SLUG_RE = re.compile(r"[^A-Za-z0-9._-]+")


def slugify_name(name: str) -> str:
    """Turn a Blender object name into a wire-safe ID fragment.

    `name` is typically `bpy.types.Object.name`. We don't strip case —
    "Town_Crate_01" stays distinguishable from "town_crate_01".
    Trailing/leading underscores are removed for tidiness.
    """
    if not name:
        return "unnamed"
    s = _SLUG_RE.sub("_", name).strip("_")
    return s or "unnamed"


def new_asset_id() -> str:
    """Return a fresh opaque asset_id (UUID hex, 32 chars).

    No prefix or suffix — manifests prepend their own ("ps1g_" etc.)
    when relevant. The hex form is stable across JSON round-trips
    without quoting concerns.
    """
    return uuid.uuid4().hex


def ensure_object_ids(obj):
    """Fill in empty asset_id / mesh_id on a tagged Object.

    Mutates obj.ps1godot. Returns nothing — read the fields back from
    the PropertyGroup. Idempotent: existing IDs are preserved.
    """
    props = obj.ps1godot
    if not props.asset_id:
        props.asset_id = new_asset_id()
    if not props.mesh_id:
        props.mesh_id = slugify_name(obj.name)
