#pragma once

#include <psyqo/gte-registers.hh>
#include <psyqo/primitives/common.hh>

namespace psxsplash {

  // Sentinel value for untextured (vertex-color-only) triangles
  static constexpr uint16_t UNTEXTURED_TPAGE = 0xFFFF;

  // Working triangle for the renderer. The renderer's processTriangle
  // function takes one of these by reference. For STATIC meshes (v31+),
  // they're constructed on the stack per-iteration via expandTri() from
  // a pooled (Vertex[], Face[]) representation. For SKINNED meshes, the
  // legacy expanded Tri[] layout is preserved (skin's per-tri-vertex bone
  // indices need vertices not to be deduplicated).
  class Tri final {
    public:
      psyqo::GTE::PackedVec3 v0, v1, v2;
      psyqo::GTE::PackedVec3 normal;

      psyqo::Color colorA, colorB, colorC;

      psyqo::PrimPieces::UVCoords uvA, uvB;
      psyqo::PrimPieces::UVCoordsPadded uvC;

      psyqo::PrimPieces::TPageAttr tpage;
      uint16_t clutX;
      uint16_t clutY;
      uint16_t padding;

      /// Returns true if this triangle has no texture (vertex-color only).
      bool isUntextured() const {
          return *reinterpret_cast<const uint16_t*>(&tpage) == UNTEXTURED_TPAGE;
      }
  };
  static_assert(sizeof(Tri) == 52, "Tri is not 52 bytes");

  // Pooled vertex (v31+ static-mesh format). Position + per-vertex UV
  // + per-vertex color. Face normal lives on Face, not here, because
  // PSX rasterisation uses one normal per triangle (flat shading), not
  // per-vertex. Field order matters: psyqo::Color is union-with-u32 →
  // 4-byte alignment. Putting it last (offset 8) avoids the struct-pad
  // the compiler would otherwise insert before it. uv (1-byte aligned,
  // 2 B) fits into the 6-7 byte slot after pos with no waste.
  struct Vertex {
      psyqo::GTE::PackedVec3      pos;   // 6 B  (PSX-coord int16×3, fp12)
      psyqo::PrimPieces::UVCoords uv;    // 2 B
      psyqo::Color                color; // 4 B  (rgba; alpha is GTE-padding)
  };
  static_assert(sizeof(Vertex) == 12, "Vertex must be 12 bytes");

  // Triangle face record (v31+). 3 × u16 vertex-pool indices + per-face
  // attributes (normal, texture page, CLUT). The renderer expands each
  // Face into a Tri on the stack via expandTri() before submitting.
  struct Face {
      uint16_t i0, i1, i2;                       // 6 B  vertex indices
      psyqo::GTE::PackedVec3 normal;             // 6 B  face normal (fp12)
      psyqo::PrimPieces::TPageAttr tpage;        // 2 B
      uint16_t clutX;                            // 2 B
      uint16_t clutY;                            // 2 B
      uint16_t pad;                              // 2 B  alignment / future use
  };
  static_assert(sizeof(Face) == 20, "Face must be 20 bytes");

  // Per-mesh blob header (v31+). Followed by Vertex[vertexCount] then
  // Face[triCount] in the splashpack data buffer. obj->polygons (still
  // typed Tri* for layout-compat) actually points at this header for
  // static meshes. Skinned meshes keep the legacy expanded Tri[] layout.
  struct MeshBlob {
      uint16_t vertexCount;
      uint16_t triCount;
  };
  static_assert(sizeof(MeshBlob) == 4, "MeshBlob must be 4 bytes");

  /// Get the vertex array for a static-mesh blob.
  inline const Vertex* meshVertices(const void* meshDataPtr) {
      return reinterpret_cast<const Vertex*>(
          reinterpret_cast<const uint8_t*>(meshDataPtr) + sizeof(MeshBlob));
  }

  /// Get the face array for a static-mesh blob.
  inline const Face* meshFaces(const void* meshDataPtr) {
      auto blob = reinterpret_cast<const MeshBlob*>(meshDataPtr);
      return reinterpret_cast<const Face*>(
          reinterpret_cast<const uint8_t*>(meshDataPtr)
          + sizeof(MeshBlob)
          + sizeof(Vertex) * blob->vertexCount);
  }

  /// Triangle count for a static-mesh blob (matches obj->polyCount; both are
  /// kept in sync by the writer for diagnostic clarity).
  inline uint16_t meshTriCount(const void* meshDataPtr) {
      return reinterpret_cast<const MeshBlob*>(meshDataPtr)->triCount;
  }

  /// Build a Tri on the stack from a pooled (Vertex[], Face) pair. Inlined
  /// so the per-tri call site has identical hot-path shape to the legacy
  /// "Tri tri = obj->polygons[i]" load — three Vertex dereferences plus
  /// the face copy, no extra branching.
  inline Tri expandTri(const Vertex* verts, const Face& face) {
      Tri t;
      const Vertex& a = verts[face.i0];
      const Vertex& b = verts[face.i1];
      const Vertex& c = verts[face.i2];
      t.v0 = a.pos; t.v1 = b.pos; t.v2 = c.pos;
      t.normal = face.normal;
      t.colorA = a.color; t.colorB = b.color; t.colorC = c.color;
      t.uvA = a.uv; t.uvB = b.uv;
      // uvC promoted to UVCoordsPadded (extra two bytes hold tpage data on
      // PSX primitives; we copy through u/v and the rest stays zero — the
      // renderer's setup writes the actual prim-side fields itself).
      t.uvC = {};
      t.uvC.u = c.uv.u;
      t.uvC.v = c.uv.v;
      t.tpage = face.tpage;
      t.clutX = face.clutX;
      t.clutY = face.clutY;
      t.padding = 0;
      return t;
  }

} // namespace psxsplash
