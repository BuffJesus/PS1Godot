#pragma once

#include <psyqo/fixed-point.hh>
#include <psyqo/matrix.hh>
#include <psyqo/trigonometry.hh>

#include "bvh.hh"

namespace psxsplash {

class Camera {
  public:
    Camera();

    void MoveX(psyqo::FixedPoint<12> x);
    void MoveY(psyqo::FixedPoint<12> y);
    void MoveZ(psyqo::FixedPoint<12> z);

    void SetPosition(psyqo::FixedPoint<12> x, psyqo::FixedPoint<12> y, psyqo::FixedPoint<12> z);
    psyqo::Vec3& GetPosition() { return m_position; }

    void SetRotation(psyqo::Angle x, psyqo::Angle y, psyqo::Angle z);
    psyqo::Matrix33& GetRotation();

    void SetProjectionH(int32_t h) { m_projH = h; }
    int32_t GetProjectionH() const { return m_projH; }

    void ExtractFrustum(Frustum& frustum) const;

    int16_t GetAngleX() const { return m_angleX; }
    int16_t GetAngleY() const { return m_angleY; }
    int16_t GetAngleZ() const { return m_angleZ; }

    // Begin a screen-shake. `intensity` is the max per-axis offset in world
    // units (FP12), applied as random noise. Decays linearly over `frames`
    // frames. Multiple Shake calls overwrite (don't stack) — gameplay code
    // should pick max(currentRemaining, newRemaining) if it wants stacking.
    void Shake(psyqo::FixedPoint<12> intensity, int frames);

    // Advances shake state by one frame and updates `m_shakeOffset`. Does NOT
    // touch `m_position` — shake is applied by SceneManager as a wrap around
    // render (add before render, subtract after). Keeping it separate means
    // game logic (including camera-follow that calls SetPosition every frame)
    // never clobbers the offset. Call once per frame, even when paused.
    void AdvanceShake();

    // Current frame's shake offset (zero when no shake is active). Read by
    // SceneManager::GameTick to wrap the render block.
    const psyqo::Vec3& GetShakeOffset() const { return m_shakeOffset; }

    bool IsShaking() const { return m_shakeFramesRemaining > 0; }

  private:
    psyqo::Matrix33 m_rotationMatrix;
    psyqo::Trig<> m_trig;
    psyqo::Vec3 m_position;
    int16_t m_angleX = 0, m_angleY = 0, m_angleZ = 0;
    int32_t m_projH = 120;

    // Shake state (see Shake / AdvanceShake). m_shakeOffset is the current
    // frame's render-only offset; never folded into m_position.
    psyqo::Vec3 m_shakeOffset;
    psyqo::FixedPoint<12> m_shakeIntensity;
    int m_shakeFramesRemaining = 0;
    int m_shakeFramesTotal = 0;
};
}  // namespace psxsplash
