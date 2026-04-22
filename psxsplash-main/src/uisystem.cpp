#include "uisystem.hh"

#include <psyqo/kernel.hh>
#include <psyqo/primitives/common.hh>
#include <psyqo/primitives/misc.hh>
#include <psyqo/primitives/rectangles.hh>
#include <psyqo/primitives/sprites.hh>
#include <psyqo/primitives/triangles.hh>
#include "streq.hh"

namespace psxsplash {

// ============================================================================
// Init
// ============================================================================

void UISystem::init(psyqo::Font<>& systemFont) {
    m_systemFont = &systemFont;
    m_canvasCount  = 0;
    m_elementCount = 0;
    m_pendingTextCount = 0;
    m_fontCount = 0;
}

// ============================================================================
// Pointer relocation after buffer shrink
// ============================================================================

void UISystem::relocate(intptr_t delta) {
    for (int ci = 0; ci < m_canvasCount; ci++) {
        if (m_canvases[ci].name && m_canvases[ci].name[0] != '\0')
            m_canvases[ci].name = reinterpret_cast<const char*>(reinterpret_cast<intptr_t>(m_canvases[ci].name) + delta);
    }
    for (int ei = 0; ei < m_elementCount; ei++) {
        if (m_elements[ei].name && m_elements[ei].name[0] != '\0')
            m_elements[ei].name = reinterpret_cast<const char*>(reinterpret_cast<intptr_t>(m_elements[ei].name) + delta);
    }
    for (int fi = 0; fi < m_fontCount; fi++) {
        m_fontDescs[fi].pixelData = nullptr;
    }
}

// ============================================================================
// Load from splashpack (zero-copy, pointer fixup)
// ============================================================================

void UISystem::loadFromSplashpack(uint8_t* data, uint16_t canvasCount,
                                   uint8_t fontCount, uint32_t tableOffset) {
    if (tableOffset == 0) return;

    uint8_t* ptr = data + tableOffset;

    // ── Parse font descriptors (112 bytes each, before canvas data) ──
    // Layout: glyphW(1) glyphH(1) vramX(2) vramY(2) textureH(2)
    //         dataOffset(4) dataSize(4) advanceWidths(96)
    if (fontCount > UI_MAX_FONTS - 1) fontCount = UI_MAX_FONTS - 1;
    m_fontCount = fontCount;
    for (int fi = 0; fi < fontCount; fi++) {
        UIFontDesc& fd = m_fontDescs[fi];
        fd.glyphW        = ptr[0];
        fd.glyphH        = ptr[1];
        fd.vramX         = *reinterpret_cast<uint16_t*>(ptr + 2);
        fd.vramY         = *reinterpret_cast<uint16_t*>(ptr + 4);
        fd.textureH      = *reinterpret_cast<uint16_t*>(ptr + 6);
        uint32_t dataOff = *reinterpret_cast<uint32_t*>(ptr + 8);
        fd.pixelDataSize = *reinterpret_cast<uint32_t*>(ptr + 12);
        fd.pixelData     = (dataOff != 0) ? (data + dataOff) : nullptr;
        // Read 96 advance width bytes
        for (int i = 0; i < 96; i++) {
            fd.advanceWidths[i] = ptr[16 + i];
        }
        ptr += 112;
    }

    // Canvas descriptors follow immediately after font descriptors.
    // Font pixel data is in the dead zone (at absolute offsets in the descriptors).

    // ── Parse canvas descriptors ──
    if (canvasCount == 0) return;
    if (canvasCount > UI_MAX_CANVASES) canvasCount = UI_MAX_CANVASES;

    // Canvas descriptor table: 12 bytes per entry
    // struct { uint32_t dataOffset; uint8_t nameLen; uint8_t sortOrder;
    //          uint8_t elementCount; uint8_t flags; uint32_t nameOffset; }
    uint8_t* tablePtr = ptr; // starts after font descriptors AND pixel data
    m_canvasCount = canvasCount;
    m_elementCount = 0;

    for (int ci = 0; ci < canvasCount; ci++) {
        uint32_t dataOffset    = *reinterpret_cast<uint32_t*>(tablePtr); tablePtr += 4;
        uint8_t  nameLen       = *tablePtr++;
        uint8_t  sortOrder     = *tablePtr++;
        uint8_t  elementCount  = *tablePtr++;
        uint8_t  flags         = *tablePtr++;
        uint32_t nameOffset    = *reinterpret_cast<uint32_t*>(tablePtr); tablePtr += 4;

        UICanvas& cv = m_canvases[ci];
        cv.name = (nameLen > 0 && nameOffset != 0)
                  ? reinterpret_cast<const char*>(data + nameOffset)
                  : "";
        cv.visible    = (flags & 0x01) != 0;
        cv.sortOrder  = sortOrder;
        cv.elements   = &m_elements[m_elementCount];

        // Cap element count against pool
        if (m_elementCount + elementCount > UI_MAX_ELEMENTS)
            elementCount = (uint8_t)(UI_MAX_ELEMENTS - m_elementCount);
        cv.elementCount = elementCount;

        // Parse element array (48 bytes per entry)
        uint8_t* elemPtr = data + dataOffset;
        for (int ei = 0; ei < elementCount; ei++) {
            UIElement& el = m_elements[m_elementCount++];

            // Identity (8 bytes)
            el.type    = static_cast<UIElementType>(*elemPtr++);
            uint8_t eFlags = *elemPtr++;
            el.visible = (eFlags & 0x01) != 0;
            uint8_t eNameLen = *elemPtr++;
            elemPtr++; // pad0
            uint32_t eNameOff = *reinterpret_cast<uint32_t*>(elemPtr); elemPtr += 4;
            el.name = (eNameLen > 0 && eNameOff != 0)
                      ? reinterpret_cast<const char*>(data + eNameOff)
                      : "";

            // Layout (8 bytes)
            el.x = *reinterpret_cast<int16_t*>(elemPtr); elemPtr += 2;
            el.y = *reinterpret_cast<int16_t*>(elemPtr); elemPtr += 2;
            el.w = *reinterpret_cast<int16_t*>(elemPtr); elemPtr += 2;
            el.h = *reinterpret_cast<int16_t*>(elemPtr); elemPtr += 2;

            // Anchors (4 bytes)
            el.anchorMinX = *elemPtr++;
            el.anchorMinY = *elemPtr++;
            el.anchorMaxX = *elemPtr++;
            el.anchorMaxY = *elemPtr++;

            // Primary color (4 bytes)
            el.colorR = *elemPtr++;
            el.colorG = *elemPtr++;
            el.colorB = *elemPtr++;
            elemPtr++; // pad1

            // Type-specific data (16 bytes)
            uint8_t* typeData = elemPtr;
            elemPtr += 16;

            // Initialize union to zero
            for (int i = 0; i < (int)sizeof(UIImageData); i++)
                reinterpret_cast<uint8_t*>(&el.image)[i] = 0;

            switch (el.type) {
            case UIElementType::Image:
                el.image.texpageX = typeData[0];
                el.image.texpageY = typeData[1];
                el.image.clutX    = *reinterpret_cast<uint16_t*>(&typeData[2]);
                el.image.clutY    = *reinterpret_cast<uint16_t*>(&typeData[4]);
                el.image.u0       = typeData[6];
                el.image.v0       = typeData[7];
                el.image.u1       = typeData[8];
                el.image.v1       = typeData[9];
                el.image.bitDepth = typeData[10];
                break;
            case UIElementType::Progress:
                el.progress.bgR   = typeData[0];
                el.progress.bgG   = typeData[1];
                el.progress.bgB   = typeData[2];
                el.progress.value = typeData[3];
                break;
            case UIElementType::Text:
                el.textData.fontIndex = typeData[0]; // 0=system, 1+=custom
                el.textData.hAlign    = typeData[1]; // 0=Left, 1=Center, 2=Right
                el.textData.vAlign    = typeData[2]; // 0=Top,  1=Middle, 2=Bottom
                break;
            default:
                break;
            }

            // Text content offset (8 bytes)
            uint32_t textOff = *reinterpret_cast<uint32_t*>(elemPtr); elemPtr += 4;
            elemPtr += 4; // pad2

            // Initialize text buffer
            el.textBuf[0] = '\0';
            if (el.type == UIElementType::Text && textOff != 0) {
                const char* src = reinterpret_cast<const char*>(data + textOff);
                int ti = 0;
                while (ti < UI_TEXT_BUF - 1 && src[ti] != '\0') {
                    el.textBuf[ti] = src[ti];
                    ti++;
                }
                el.textBuf[ti] = '\0';
            }
        }
    }

    // Insertion sort canvases by sortOrder (ascending = back-to-front)
    for (int i = 1; i < m_canvasCount; i++) {
        UICanvas tmp = m_canvases[i];
        int j = i - 1;
        while (j >= 0 && m_canvases[j].sortOrder > tmp.sortOrder) {
            m_canvases[j + 1] = m_canvases[j];
            j--;
        }
        m_canvases[j + 1] = tmp;
    }
}

// ============================================================================
// Layout resolution
// ============================================================================

void UISystem::resolveLayout(const UIElement& el,
                              int16_t& outX, int16_t& outY,
                              int16_t& outW, int16_t& outH) const {
    // Anchor gives the origin point in screen space (8.8 fixed -> pixel)
    int ax = ((int)el.anchorMinX * VRAM_RES_WIDTH)  >> 8;
    int ay = ((int)el.anchorMinY * VRAM_RES_HEIGHT) >> 8;
    outX = (int16_t)(ax + el.x);
    outY = (int16_t)(ay + el.y);

    // Stretch: anchorMax != anchorMin means width/height is determined by span + offset
    if (el.anchorMaxX != el.anchorMinX) {
        int bx = ((int)el.anchorMaxX * VRAM_RES_WIDTH) >> 8;
        outW = (int16_t)(bx - ax + el.w);
    } else {
        outW = el.w;
    }
    if (el.anchorMaxY != el.anchorMinY) {
        int by = ((int)el.anchorMaxY * VRAM_RES_HEIGHT) >> 8;
        outH = (int16_t)(by - ay + el.h);
    } else {
        outH = el.h;
    }

    // Clamp to screen bounds (never draw outside the framebuffer)
    if (outX < 0) { outW += outX; outX = 0; }
    if (outY < 0) { outH += outY; outY = 0; }
    if (outW <= 0) outW = 1;
    if (outH <= 0) outH = 1;
    if (outX + outW > VRAM_RES_WIDTH)  outW = (int16_t)(VRAM_RES_WIDTH  - outX);
    if (outY + outH > VRAM_RES_HEIGHT) outH = (int16_t)(VRAM_RES_HEIGHT - outY);
}

// ============================================================================
// TPage construction for UI images
// ============================================================================

psyqo::PrimPieces::TPageAttr UISystem::makeTPage(const UIImageData& img) {
    psyqo::PrimPieces::TPageAttr tpage;
    tpage.setPageX(img.texpageX);
    tpage.setPageY(img.texpageY);
    // Color mode from bitDepth: 0->Tex4Bits, 1->Tex8Bits, 2->Tex16Bits
    switch (img.bitDepth) {
    case 0:
        tpage.set(psyqo::Prim::TPageAttr::Tex4Bits);
        break;
    case 1:
        tpage.set(psyqo::Prim::TPageAttr::Tex8Bits);
        break;
    case 2:
    default:
        tpage.set(psyqo::Prim::TPageAttr::Tex16Bits);
        break;
    }
    tpage.setDithering(false); // UI doesn't need dithering
    return tpage;
}

// ============================================================================
// Render a single element into the OT
// ============================================================================

void UISystem::renderElement(UIElement& el,
                             psyqo::OrderingTable<Renderer::ORDERING_TABLE_SIZE>& ot,
                             psyqo::BumpAllocator<Renderer::BUMP_ALLOCATOR_SIZE>& balloc) {
    int16_t x, y, w, h;
    resolveLayout(el, x, y, w, h);

    switch (el.type) {
    case UIElementType::Box: {
        auto& frag = balloc.allocateFragment<psyqo::Prim::Rectangle>();
        frag.primitive.setColor(psyqo::Color{.r = el.colorR, .g = el.colorG, .b = el.colorB});
        frag.primitive.position = {.x = x, .y = y};
        frag.primitive.size = {.x = w, .y = h};
        frag.primitive.setOpaque();
        ot.insert(frag, 0);
        break;
    }

    case UIElementType::Progress: {
        // Background: full rect
        auto& bgFrag = balloc.allocateFragment<psyqo::Prim::Rectangle>();
        bgFrag.primitive.setColor(psyqo::Color{.r = el.progress.bgR, .g = el.progress.bgG, .b = el.progress.bgB});
        bgFrag.primitive.position = {.x = x, .y = y};
        bgFrag.primitive.size = {.x = w, .y = h};
        bgFrag.primitive.setOpaque();
        ot.insert(bgFrag, 1);

        // Fill: partial width
        int fillW = (int)el.progress.value * w / 100;
        if (fillW < 0) fillW = 0;
        if (fillW > w) fillW = w;
        if (fillW > 0) {
            auto& fillFrag = balloc.allocateFragment<psyqo::Prim::Rectangle>();
            fillFrag.primitive.setColor(psyqo::Color{.r = el.colorR, .g = el.colorG, .b = el.colorB});
            fillFrag.primitive.position = {.x = x, .y = y};
            fillFrag.primitive.size = {.x = (int16_t)fillW, .y = h};
            fillFrag.primitive.setOpaque();
            ot.insert(fillFrag, 0);
        }
        break;
    }

    case UIElementType::Image: {
        psyqo::PrimPieces::TPageAttr tpage = makeTPage(el.image);
        psyqo::PrimPieces::ClutIndex clut(el.image.clutX, el.image.clutY);
        psyqo::Color tint = {.r = el.colorR, .g = el.colorG, .b = el.colorB};

        // Triangle 0: top-left, top-right, bottom-left
        {
            auto& tri = balloc.allocateFragment<psyqo::Prim::GouraudTexturedTriangle>();
            tri.primitive.pointA.x = x;     tri.primitive.pointA.y = y;
            tri.primitive.pointB.x = x + w; tri.primitive.pointB.y = y;
            tri.primitive.pointC.x = x;     tri.primitive.pointC.y = y + h;
            tri.primitive.uvA.u = el.image.u0; tri.primitive.uvA.v = el.image.v0;
            tri.primitive.uvB.u = el.image.u1; tri.primitive.uvB.v = el.image.v0;
            tri.primitive.uvC.u = el.image.u0; tri.primitive.uvC.v = el.image.v1;
            tri.primitive.tpage = tpage;
            tri.primitive.clutIndex = clut;
            tri.primitive.setColorA(tint);
            tri.primitive.setColorB(tint);
            tri.primitive.setColorC(tint);
            tri.primitive.setOpaque();
            ot.insert(tri, 0);
        }
        // Triangle 1: top-right, bottom-right, bottom-left
        {
            auto& tri = balloc.allocateFragment<psyqo::Prim::GouraudTexturedTriangle>();
            tri.primitive.pointA.x = x + w; tri.primitive.pointA.y = y;
            tri.primitive.pointB.x = x + w; tri.primitive.pointB.y = y + h;
            tri.primitive.pointC.x = x;     tri.primitive.pointC.y = y + h;
            tri.primitive.uvA.u = el.image.u1; tri.primitive.uvA.v = el.image.v0;
            tri.primitive.uvB.u = el.image.u1; tri.primitive.uvB.v = el.image.v1;
            tri.primitive.uvC.u = el.image.u0; tri.primitive.uvC.v = el.image.v1;
            tri.primitive.tpage = tpage;
            tri.primitive.clutIndex = clut;
            tri.primitive.setColorA(tint);
            tri.primitive.setColorB(tint);
            tri.primitive.setColorC(tint);
            tri.primitive.setOpaque();
            ot.insert(tri, 0);
        }
        break;
    }

    case UIElementType::Text: {
        uint8_t fi = el.textData.fontIndex;
        uint8_t hAlign = el.textData.hAlign;
        uint8_t vAlign = el.textData.vAlign;
        if (fi > 0 && fi <= m_fontCount) {
            // Custom font: render proportionally into OT (handles '\n'
            // + alignment itself)
            renderProportionalText(fi - 1, x, y, w, h, hAlign, vAlign,
                                   el.colorR, el.colorG, el.colorB,
                                   el.textBuf, ot, balloc);
        } else {
            // System font: 8x8 glyph cell; advance 8px per char, 8px
            // per line. Split on '\n', measure each line, shift per
            // hAlign, shift whole block per vAlign.
            const int16_t systemLineHeight = 8;
            const int16_t systemGlyphW = 8;

            // Pass 1: count lines to size the vertical shift.
            int lineCount = 0;
            for (const char* p = el.textBuf; ; p++) {
                if (*p == '\0') { lineCount++; break; }
                if (*p == '\n') lineCount++;
            }
            int16_t totalH = (int16_t)(lineCount * systemLineHeight);
            int16_t yOffset = 0;
            if (vAlign == 1) yOffset = (int16_t)((h - totalH) / 2);
            else if (vAlign == 2) yOffset = (int16_t)(h - totalH);

            // Pass 2: queue each line with per-line x shift.
            const char* lineStart = el.textBuf;
            int16_t cursorY = y + yOffset;
            while (m_pendingTextCount < UI_MAX_ELEMENTS) {
                const char* p = lineStart;
                while (*p != '\0' && *p != '\n') p++;
                uint8_t segLen = (uint8_t)(p - lineStart);
                if (segLen > 0) {
                    int16_t lineW = (int16_t)(segLen * systemGlyphW);
                    int16_t lineX = x;
                    if (hAlign == 1) lineX = (int16_t)(x + (w - lineW) / 2);
                    else if (hAlign == 2) lineX = (int16_t)(x + (w - lineW));
                    m_pendingTexts[m_pendingTextCount++] = {
                        lineX, cursorY,
                        el.colorR, el.colorG, el.colorB,
                        lineStart, segLen
                    };
                }
                if (*p == '\0') break;
                lineStart = p + 1;
                cursorY += systemLineHeight;
            }
        }
        break;
    }
    }
}

// ============================================================================
// Render phases
// ============================================================================

void UISystem::renderOT(psyqo::GPU& gpu,
                        psyqo::OrderingTable<Renderer::ORDERING_TABLE_SIZE>& ot,
                        psyqo::BumpAllocator<Renderer::BUMP_ALLOCATOR_SIZE>& balloc) {
    m_pendingTextCount = 0;

    // Canvases are pre-sorted by sortOrder (ascending = back first).
    // Higher-sortOrder canvases insert at OT 0 later, appearing on top.
    for (int i = 0; i < m_canvasCount; i++) {
        UICanvas& cv = m_canvases[i];
        if (!cv.visible) continue;
        for (int j = 0; j < cv.elementCount; j++) {
            UIElement& el = cv.elements[j];
            if (!el.visible) continue;
            renderElement(el, ot, balloc);
        }
    }
}

void UISystem::renderText(psyqo::GPU& gpu) {
    for (int i = 0; i < m_pendingTextCount; i++) {
        auto& pt = m_pendingTexts[i];
        m_systemFont->chainprintf(gpu,
            {{.x = pt.x, .y = pt.y}},
            {{.r = pt.r, .g = pt.g, .b = pt.b}},
            "%.*s", (int)pt.len, pt.text);
    }
}

// ============================================================================
// Font support
// ============================================================================

psyqo::FontBase* UISystem::resolveFont(uint8_t fontIndex) {
    // Only used for system font now; custom fonts go through renderProportionalText
    return m_systemFont;
}

void UISystem::uploadFonts(psyqo::GPU& gpu) {
    for (int i = 0; i < m_fontCount; i++) {
        UIFontDesc& fd = m_fontDescs[i];
        if (!fd.pixelData || fd.pixelDataSize == 0) continue;

        // Upload 4bpp texture to VRAM
        // 4bpp 256px wide = 64 VRAM hwords wide
        Renderer::GetInstance().VramUpload(
            reinterpret_cast<const uint16_t*>(fd.pixelData),
            (int16_t)fd.vramX, (int16_t)fd.vramY,
            64, (int16_t)fd.textureH);

        // Upload white CLUT at font CLUT position (entry 0=transparent, entry 1=white).
        // Sprite color tinting will produce the desired text color.
        static const uint16_t whiteCLUT[2] = { 0x0000, 0x7FFF };
        Renderer::GetInstance().VramUpload(
            whiteCLUT,
            (int16_t)fd.vramX, (int16_t)fd.vramY,
            2, 1);
    }
}

// ============================================================================
// Proportional text rendering (custom fonts)
// ============================================================================

void UISystem::renderProportionalText(int fontIdx, int16_t x, int16_t y,
                                       int16_t w, int16_t h,
                                       uint8_t hAlign, uint8_t vAlign,
                                       uint8_t r, uint8_t g, uint8_t b,
                                       const char* text,
                                       psyqo::OrderingTable<Renderer::ORDERING_TABLE_SIZE>& ot,
                                       psyqo::BumpAllocator<Renderer::BUMP_ALLOCATOR_SIZE>& balloc) {
    UIFontDesc& fd = m_fontDescs[fontIdx];
    int glyphsPerRow = 256 / fd.glyphW;
    uint8_t baseV = fd.vramY & 0xFF;

    // TPage for this font's texture page
    psyqo::PrimPieces::TPageAttr tpageAttr;
    tpageAttr.setPageX(fd.vramX >> 6);
    tpageAttr.setPageY(fd.vramY >> 8);
    tpageAttr.set(psyqo::Prim::TPageAttr::Tex4Bits);
    tpageAttr.setDithering(false);

    // CLUT reference for this font
    psyqo::Vertex clutPos = {{.x = (int16_t)fd.vramX, .y = (int16_t)fd.vramY}};
    psyqo::PrimPieces::ClutIndex clutIdx(clutPos);

    psyqo::Color color = {.r = r, .g = g, .b = b};

    // ── Pass 1: measure line widths for hAlign + count lines for
    //            vAlign. Static buffer is plenty for a 64-byte text
    //            buffer (worst case is 64 empty \n lines, we cap at 16
    //            visible lines — the runtime render budget already
    //            balks long before that).
    constexpr int MAX_LINES = 16;
    int16_t lineWidths[MAX_LINES] = {0};
    int lineCount = 0;
    {
        int16_t cur = 0;
        for (const char* p = text; ; p++) {
            uint8_t c = (uint8_t)*p;
            if (c == '\0') {
                if (lineCount < MAX_LINES) lineWidths[lineCount] = cur;
                lineCount++;
                break;
            }
            if (c == '\n') {
                if (lineCount < MAX_LINES) lineWidths[lineCount] = cur;
                lineCount++;
                cur = 0;
                continue;
            }
            if (c < 32 || c > 127) c = '?';
            cur += fd.advanceWidths[c - 32];
        }
        if (lineCount > MAX_LINES) lineCount = MAX_LINES;
    }
    int16_t totalH = (int16_t)(lineCount * fd.glyphH);
    int16_t yOff = 0;
    if (vAlign == 1) yOff = (int16_t)((h - totalH) / 2);
    else if (vAlign == 2) yOff = (int16_t)(h - totalH);

    auto startX = [&](int lineIdx) -> int16_t {
        int16_t lw = (lineIdx < MAX_LINES) ? lineWidths[lineIdx] : (int16_t)0;
        if (hAlign == 1) return (int16_t)(x + (w - lw) / 2);
        if (hAlign == 2) return (int16_t)(x + (w - lw));
        return x;
    };

    // ── Pass 2: emit glyph sprites, shifting per line ──────────
    int lineIdx = 0;
    int16_t cursorX = startX(0);
    int16_t cursorY = (int16_t)(y + yOff);
    while (*text) {
        uint8_t c = (uint8_t)*text++;
        if (c == '\n') {
            lineIdx++;
            cursorX = startX(lineIdx);
            cursorY += fd.glyphH;
            continue;
        }
        if (c < 32 || c > 127) c = '?';
        uint8_t charIdx = c - 32;

        uint8_t advance = fd.advanceWidths[charIdx];

        if (c == ' ') {
            cursorX += advance;
            continue;
        }

        int charRow = charIdx / glyphsPerRow;
        int charCol = charIdx % glyphsPerRow;
        uint8_t u = (uint8_t)(charCol * fd.glyphW);
        uint8_t v = (uint8_t)(baseV + charRow * fd.glyphH);

        auto& frag = balloc.allocateFragment<psyqo::Prim::Sprite>();
        frag.primitive.position = {.x = cursorX, .y = cursorY};
        // Use advance as sprite width for proportional sizing.
        // The glyph is left-aligned in the cell, so showing advance-width
        // pixels captures the glyph content with correct spacing.
        int16_t spriteW = (advance > 0 && advance < fd.glyphW) ? (int16_t)advance : (int16_t)fd.glyphW;
        frag.primitive.size = {.x = spriteW, .y = (int16_t)fd.glyphH};
        frag.primitive.setColor(color);
        psyqo::PrimPieces::TexInfo texInfo;
        texInfo.u = u;
        texInfo.v = v;
        texInfo.clut = clutIdx;
        frag.primitive.texInfo = texInfo;
        ot.insert(frag, 0);

        cursorX += advance;
    }

    // Then: insert TPage AFTER sprites at the same depth.
    // OT uses head insertion (LIFO), so TPage ends up rendering BEFORE the sprites.
    // This ensures each text element's TPage is active for its own sprites only,
    // even when multiple fonts are on screen simultaneously.
    auto& tpFrag = balloc.allocateFragment<psyqo::Prim::TPage>();
    tpFrag.primitive.attr = tpageAttr;
    ot.insert(tpFrag, 0);
}

// ============================================================================
// Canvas API
// ============================================================================

int UISystem::findCanvas(const char* name) const {
    if (!name) return -1;
    for (int i = 0; i < m_canvasCount; i++) {
        if (m_canvases[i].name && streq(m_canvases[i].name, name))
            return i;
    }
    return -1;
}

void UISystem::setCanvasVisible(int idx, bool v) {
    if (idx >= 0 && idx < m_canvasCount)
        m_canvases[idx].visible = v;
}

bool UISystem::isCanvasVisible(int idx) const {
    if (idx >= 0 && idx < m_canvasCount)
        return m_canvases[idx].visible;
    return false;
}

// ============================================================================
// Element API
// ============================================================================

int UISystem::findElement(int canvasIdx, const char* name) const {
    if (canvasIdx < 0 || canvasIdx >= m_canvasCount || !name) return -1;
    const UICanvas& cv = m_canvases[canvasIdx];
    for (int i = 0; i < cv.elementCount; i++) {
        if (cv.elements[i].name && streq(cv.elements[i].name, name)) {
            // Return flat handle: index into m_elements
            int handle = (int)(cv.elements + i - m_elements);
            return handle;
        }
    }
    return -1;
}

void UISystem::setElementVisible(int handle, bool v) {
    if (handle >= 0 && handle < m_elementCount)
        m_elements[handle].visible = v;
}

bool UISystem::isElementVisible(int handle) const {
    if (handle >= 0 && handle < m_elementCount)
        return m_elements[handle].visible;
    return false;
}

void UISystem::setText(int handle, const char* text) {
    if (handle < 0 || handle >= m_elementCount) return;
    UIElement& el = m_elements[handle];
    if (el.type != UIElementType::Text) return;
    if (!text) { el.textBuf[0] = '\0'; return; }
    int i = 0;
    while (i < UI_TEXT_BUF - 1 && text[i] != '\0') {
        el.textBuf[i] = text[i];
        i++;
    }
    el.textBuf[i] = '\0';
}

const char* UISystem::getText(int handle) const {
    if (handle < 0 || handle >= m_elementCount) return "";
    const UIElement& el = m_elements[handle];
    if (el.type != UIElementType::Text) return "";
    return el.textBuf;
}

void UISystem::setProgress(int handle, uint8_t value) {
    if (handle < 0 || handle >= m_elementCount) return;
    UIElement& el = m_elements[handle];
    if (el.type != UIElementType::Progress) return;
    if (value > 100) value = 100;
    el.progress.value = value;
}

void UISystem::setColor(int handle, uint8_t r, uint8_t g, uint8_t b) {
    if (handle < 0 || handle >= m_elementCount) return;
    m_elements[handle].colorR = r;
    m_elements[handle].colorG = g;
    m_elements[handle].colorB = b;
}

void UISystem::getColor(int handle, uint8_t& r, uint8_t& g, uint8_t& b) const {
    if (handle < 0 || handle >= m_elementCount) { r = g = b = 0; return; }
    r = m_elements[handle].colorR;
    g = m_elements[handle].colorG;
    b = m_elements[handle].colorB;
}

void UISystem::setPosition(int handle, int16_t x, int16_t y) {
    if (handle < 0 || handle >= m_elementCount) return;
    UIElement& el = m_elements[handle];
    el.x = x;
    el.y = y;
    // Zero out anchors to make position absolute
    el.anchorMinX = 0;
    el.anchorMinY = 0;
    el.anchorMaxX = 0;
    el.anchorMaxY = 0;
}

void UISystem::getPosition(int handle, int16_t& x, int16_t& y) const {
    if (handle < 0 || handle >= m_elementCount) { x = y = 0; return; }
    // Resolve full layout to return actual screen position
    int16_t rx, ry, rw, rh;
    resolveLayout(m_elements[handle], rx, ry, rw, rh);
    x = rx;
    y = ry;
}

void UISystem::setSize(int handle, int16_t w, int16_t h) {
    if (handle < 0 || handle >= m_elementCount) return;
    m_elements[handle].w = w;
    m_elements[handle].h = h;
    // Clear stretch anchors so size is explicit
    m_elements[handle].anchorMaxX = m_elements[handle].anchorMinX;
    m_elements[handle].anchorMaxY = m_elements[handle].anchorMinY;
}

void UISystem::getSize(int handle, int16_t& w, int16_t& h) const {
    if (handle < 0 || handle >= m_elementCount) { w = h = 0; return; }
    int16_t rx, ry, rw, rh;
    resolveLayout(m_elements[handle], rx, ry, rw, rh);
    w = rw;
    h = rh;
}

void UISystem::setProgressColors(int handle, uint8_t bgR, uint8_t bgG, uint8_t bgB,
                                  uint8_t fillR, uint8_t fillG, uint8_t fillB) {
    if (handle < 0 || handle >= m_elementCount) return;
    UIElement& el = m_elements[handle];
    if (el.type != UIElementType::Progress) return;
    el.progress.bgR = bgR;
    el.progress.bgG = bgG;
    el.progress.bgB = bgB;
    el.colorR = fillR;
    el.colorG = fillG;
    el.colorB = fillB;
}

uint8_t UISystem::getProgress(int handle) const {
    if (handle < 0 || handle >= m_elementCount) return 0;
    const UIElement& el = m_elements[handle];
    if (el.type != UIElementType::Progress) return 0;
    return el.progress.value;
}

UIElementType UISystem::getElementType(int handle) const {
    if (handle < 0 || handle >= m_elementCount) return UIElementType::Box;
    return m_elements[handle].type;
}

int UISystem::getCanvasElementCount(int canvasIdx) const {
    if (canvasIdx < 0 || canvasIdx >= m_canvasCount) return 0;
    return m_canvases[canvasIdx].elementCount;
}

int UISystem::getCanvasElementHandle(int canvasIdx, int elementIndex) const {
    if (canvasIdx < 0 || canvasIdx >= m_canvasCount) return -1;
    const UICanvas& cv = m_canvases[canvasIdx];
    if (elementIndex < 0 || elementIndex >= cv.elementCount) return -1;
    return (int)(cv.elements + elementIndex - m_elements);
}

void UISystem::getProgressBgColor(int handle, uint8_t& r, uint8_t& g, uint8_t& b) const {
    if (handle < 0 || handle >= m_elementCount) { r = g = b = 0; return; }
    const UIElement& el = m_elements[handle];
    if (el.type != UIElementType::Progress) { r = g = b = 0; return; }
    r = el.progress.bgR;
    g = el.progress.bgG;
    b = el.progress.bgB;
}

int UISystem::getCanvasCount() const {
    return m_canvasCount;
}

const UIImageData* UISystem::getImageData(int handle) const {
    if (handle < 0 || handle >= m_elementCount) return nullptr;
    const UIElement& el = m_elements[handle];
    if (el.type != UIElementType::Image) return nullptr;
    return &el.image;
}

const UIFontDesc* UISystem::getFontDesc(int fontIdx) const {
    if (fontIdx < 0 || fontIdx >= m_fontCount) return nullptr;
    return &m_fontDescs[fontIdx];
}

uint8_t UISystem::getTextFontIndex(int handle) const {
    if (handle < 0 || handle >= m_elementCount) return 0;
    const UIElement& el = m_elements[handle];
    if (el.type != UIElementType::Text) return 0;
    return el.textData.fontIndex;
}

} // namespace psxsplash
