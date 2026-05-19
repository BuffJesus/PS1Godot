# Host-mode testing — design + patch

**Status (2026-05-16):** No implementation yet. Sized as ~5 commits
across psxsplash Makefile (HOST_BUILD=1 target), stub layer for
seven psyqo modules (GPU/SPU/GTE/TaskQueue/Kernel/PCdrv/CDROM),
test framework setup, initial test cases (splashpack parser, BVH
determinism, collision math, animation interp, music sequencer,
scene transitions), and a GitHub Actions CI job. Deferred to its
own session — pairs naturally with the debugging.md and
profiling.md follow-up sessions since all three reshape the
runtime's seam between hardware and logic.

Closes the design gap behind:

> ### 3. Host-mode / test build
> **Problem.** No way to unit-test psxsplash logic without an
> emulator or real hardware. The bar for "did I break
> something" is "launch PCSX-Redux, boot, observe" — which is
> slow and bad for CI.
>
> **Proposed direction.** A `HOST_BUILD=1` make target that:
> - Stubs `psyqo::GPU`, `psyqo::SPU`, `psyqo::GTE` with
>   host-side mocks that record calls rather than executing
>   them.
> - Compiles with the host's C++ compiler (MSVC/Clang/GCC),
>   not `mipsel-none-elf`.
> - Produces a binary that can boot, load a splashpack from
>   disk, step one frame, and assert invariants.
>
> **Evidence.** _(empty — most likely trigger is "splashpack
> v21 breaks silently and we didn't notice for a week")_
> — `docs/psxsplash-improvements.md`

The "we didn't notice for a week" prediction is the why. Every
splashpack format bump, every renderer tweak, every collision
fix is currently validated by running the demo and looking at
the screen. CI runs Godot-side C# tests but the runtime is
untested. This doc designs the host-mode build.

Drop this file at `docs/host-mode-testing.md`.

## Goal

```bash
cd psxsplash-main
make HOST_BUILD=1 -j$(nproc) test
```

Runs in <30 seconds on a laptop. Exercises:

- Splashpack header parsing (every version we still support).
- BVH cull determinism (same inputs → same triangle refs).
- Collision math (sphere-vs-triangle, ray-vs-AABB).
- Animation interpolation (keyframe lerps, looping behavior).
- Music sequencer (note dispatch, voice allocation).
- Scene manager state transitions (load / unload / additive
  chunks).

Fails the build if any test regresses. Hooks into CI so PRs
that break the runtime get caught before reaching main.

Non-goal: rendering / GTE / GPU behavior. Those are hardware-
specific and not unit-testable in any useful sense. Integration
testing for those happens in PCSX-Redux via the existing manual
flow.

## What's in place

- **`psxsplash-main/Makefile`** is a standard nugget Makefile.
  All build configuration goes through `make` flags
  (LOADER, NOPARSER, OT_SIZE, etc.). Adding `HOST_BUILD=1` fits
  the existing pattern.
- **The runtime is mostly portable C++.** Heavy use of stdint
  types, no inline assembly outside specific GTE/MIPS hot
  paths. `psyqo::FixedPoint` is a header-only template; works
  fine on host.
- **psyqo subdivides into clean modules** (`psyqo::GPU`,
  `psyqo::SPU`, `psyqo::GTE`, `psyqo::TaskQueue`,
  `psyqo::Kernel`). Stubbing them is well-bounded.
- **PCdrv handler** is already a runtime-vs-emulator switch via
  a function pointer; stubbing on host follows the same shape.
- **`grumpycoders/psyqo-testing`** in the pcsx-redux ecosystem
  shows this is feasible — Grumpycoders ships a host-test
  pattern for psyqo itself.

The work is in stubbing, isolating, and wiring a test
framework.

## Design

Three pieces: a stub layer that fakes the hardware-dependent
psyqo modules, a test runner that drives the runtime through
controlled inputs, and a CI integration that runs on every PR.

### Stub layer

A new directory `psxsplash-main/test/stubs/` contains host-only
implementations of the psyqo modules the runtime uses:

```
test/stubs/
  psyqo_gpu_stub.cpp        ← records draw calls, no rendering
  psyqo_spu_stub.cpp        ← records voice writes, no audio
  psyqo_gte_stub.cpp        ← does the math in plain C++ (no MIPS coproc)
  psyqo_task_queue_stub.cpp ← runs tasks immediately, synchronously
  psyqo_kernel_stub.cpp     ← assert prints + throws instead of halting
  pcdrv_stub.cpp            ← reads from real filesystem
  cdrom_stub.cpp            ← loads ISO files from real filesystem
```

The build system swaps the real psyqo for these when
`HOST_BUILD=1`:

```makefile
ifeq ($(HOST_BUILD),1)
  CXX = $(HOST_CXX)
  CPPFLAGS += -DHOST_BUILD -I test/stubs
  # Replace psyqo includes with stubs
  EXCLUDE_OBJS = $(PSYQO_OBJS)
  SRCS += $(wildcard test/stubs/*.cpp) $(wildcard test/cases/*.cpp)
endif
```

Each stub matches the corresponding psyqo header's public
interface but records calls instead of executing them. Tests
inspect the recordings:

```cpp
// In psyqo_gpu_stub.cpp:
namespace psyqo {

struct StubGPU::Recording {
    int frameCount = 0;
    int totalPrimitivesQueued = 0;
    std::vector<RecordedPrim> primitives;
    // ... etc
};

void StubGPU::chain(OrderingTableBase& ot) {
    m_recording.frameCount++;
    m_recording.totalPrimitivesQueued += ot.totalEntries();
}

}  // namespace psyqo
```

For modules with non-trivial math (GTE), the stub actually does
the math in portable C++. GTE's RTPS does perspective projection
plus screen-space output — straightforward fixed-point arithmetic.
Host and target produce bit-identical results.

### Test framework

Use **doctest**. Single header, ~10 KB. No external deps. Fits
the project's "don't over-abstract" principle.

```cpp
// test/cases/bvh_cull_test.cpp
#include "doctest.h"
#include "../../src/bvh.hh"

TEST_CASE("BVH frustum cull returns nothing for empty BVH") {
    psxsplash::BVHManager bvh;
    psxsplash::Frustum frustum = MakeIdentityFrustum();
    psxsplash::TriangleRef refs[64];
    int count = bvh.cullFrustum(frustum, refs, 64);
    CHECK(count == 0);
}

TEST_CASE("BVH cull is order-independent") {
    // ... seed two BVHs with same tris in different orders,
    // verify cullFrustum produces same set
}
```

Tests live in `test/cases/`, one file per system. Filenames
correspond to runtime source files (`bvh_cull_test.cpp` →
`src/bvh.cpp`).

The build target is straightforward:

```makefile
test: $(TEST_BINARY)
	$(TEST_BINARY) --reporters=console,xml

$(TEST_BINARY): $(TEST_OBJS) $(STUB_OBJS) $(SOURCE_OBJS_HOST)
	$(HOST_CXX) -o $@ $^ $(LDFLAGS)
```

`make test` builds and runs. Exit code 0 = green, non-zero =
failed. CI parses the XML reporter output for nice display.

### What's testable

**Format parsing.** Loader walks every splashpack version's
header layout. Test loads a known-good fixture splashpack of
each version, validates the parsed `SplashpackSceneSetup`.
Regression catches "v22 → v23 broke v22 loading."

**Splashpack writer round-trips.** Build a `SceneData` in C#,
write a splashpack, read it back through the runtime's loader,
diff. Cross-language validation that the format is consistent.
Already done at byte level via the existing "parity test"
suggestion — this lifts it into automation.

**BVH operations.** Build a BVH from synthetic triangles, run
`cullFrustum` with various frustums, validate the returned set.
Order independence, geometric correctness, performance
regression checks.

**Collision math.** Sphere-vs-triangle, sphere-vs-AABB,
ray-vs-AABB. Each has a clean input/output signature; test
with hand-computed expected results.

**Animation interpolation.** Build an `Animation` with known
keyframes, advance time, validate the interpolated value at
each tick. Catches "loop wrapping broke when I added the new
track type."

**Music sequencer logic.** Build a tiny PS1M blob (8 events:
note-on / note-off pairs), drive the sequencer through ticks,
validate which voices fire when. Note: voice allocation
(which physical SPU voice picks up the note) is testable
because the stub records the writes.

**Scene manager transitions.** Load scene A, load scene B
(wholesale swap), load chunk C on top of B (additive), unload
C, verify state at each step. Closes the design gap for the
chunk-streaming patch — its complex transition logic gets
tested instead of "hope the demo works."

**Lua API surface.** Bind the runtime's Lua VM with the stub
modules, run small Lua snippets, validate effects. Catches "I
renamed an API method and forgot the autocomplete generator."

### What's not testable

**Rendering output.** The stub records draw calls but doesn't
produce pixels. Visual regression is its own problem (image-
diffing against PSX captures) and is out of scope here.

**Real-hardware behavior.** GPU timing, GTE precision edge
cases, DMA priority quirks — all hardware-specific. The
existing PCSX-Redux flow continues to be the integration
test layer.

**SPU voice behavior.** The stub records writes; actual SPU
mixing doesn't happen. "Did the audio sound right" stays a
manual check.

**Memory allocator edge cases.** psyqo's allocator interacts
with the linker script + physical RAM layout. Host's allocator
behaves differently. Tests use mocked allocators with
controllable failure modes.

### Test fixtures

A `test/fixtures/` directory holds known-good binary inputs:

```
test/fixtures/
  splashpack/
    empty.splashpack         ← minimal valid scene, all sections zero
    v20.splashpack          ← v20-format scene, used for backcompat tests
    v21.splashpack          ← ...
    v22.splashpack
    v23.splashpack
  bvh/
    cube.bvh                 ← BVH of a unit cube at origin
    grid.bvh                 ← BVH of a 4×4 floor grid
  ps1m/
    one_note.ps1m            ← single note, simplest sequencer test
    chord.ps1m               ← three voices simultaneous
```

Fixtures are checked into git (small, binary, stable). New
fixture types added as new tests need them.

Fixtures are generated by C# code in
`godot-ps1/addons/ps1godot/tests/FixtureGenerator.cs`. Running
the C# test suite regenerates all fixtures and verifies they
parse on host. Cross-language consistency check.

### Integration with C# test suite

The Godot side already has `MidiSerializerTests`, `LuaDecimalRewriterTests`,
`LuaApiStubGeneratorTests`. Those run inside Godot.

Host-mode tests run in C++ from `make test`. The two suites
share the splashpack fixtures via the
`test/fixtures/splashpack/` directory — C# generates them, C++
reads them. Byte-identical layout enforced both ways.

A new wrapper script `scripts/run-all-tests.py` runs both
suites in sequence and produces a unified report. Goes into
CI.

### CI integration

Extend the GitHub Actions workflow from `linux-support.md` to
add a test job:

```yaml
jobs:
  build:
    # ... existing matrix build for GDExtension ...
  
  test-host:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
        with: { submodules: recursive }
      - run: |
          cd psxsplash-main
          make HOST_BUILD=1 test
      - uses: actions/upload-artifact@v4
        if: always()
        with:
          name: test-results
          path: psxsplash-main/test-results.xml
  
  test-csharp:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - run: dotnet test godot-ps1/PS1Godot.csproj
```

PRs that fail any test job stay in "checks failed" state until
fixed. The repository's `main` branch protection requires both
jobs to pass before merge.

## Implementation stages

Four stages. Stage 1 ships the foundation; later stages broaden
coverage.

### Stage 1 — Build infrastructure

The Makefile + stub skeletons. Until tests exist, this stage is
"infrastructure only" — no behavior tested.

- `HOST_BUILD=1` make target.
- Empty stub implementations for all psyqo modules used by the
  runtime.
- doctest header committed to `test/`.
- One smoke test that asserts the runtime can be linked with
  stubs (build succeeds with `make HOST_BUILD=1`).

### Stage 2 — Format + math coverage

The first useful round of tests.

- Splashpack parsing tests for v20–v23.
- BVH cull determinism + correctness.
- Collision math (sphere/triangle/AABB).
- Animation interpolation.

Coverage target: ~30 tests, ~1500 LOC. Catches most format-
related regressions.

### Stage 3 — Higher-level behavioral tests

- Scene manager transitions (load / unload / chunk additive).
- Music sequencer end-to-end (PS1M → voice writes).
- Lua API surface (a sample of methods, not exhaustive).

These exercise integration paths that would otherwise require
running the demo.

### Stage 4 — CI + fixture round-trips

- GitHub Actions workflow.
- C# fixture generator script.
- Cross-language byte-diff validation.
- Test result badges in README.

The "we'll never silently regress the format" stage.

## Open questions / tradeoffs

**Stub fidelity.** Stubs that diverge from psyqo's real behavior
let tests pass that real hardware would fail. Mitigation: stubs
are minimal — record calls, return success — rather than
emulating. Tests assert on call patterns, not on simulated
output. Where math matters (GTE), the stub implements the same
math psyqo does and we cross-check with a small set of
"reference value" tests against known PSX-captured outputs.

**Endianness.** PS1 is little-endian; modern desktops are too.
Tests run on little-endian hosts. ARM Macs default little-
endian. Cross-endian test runs are not in scope (would only
matter on PowerPC or big-endian SPARC, neither of which is a
realistic CI target).

**Float vs fixed-point.** psyqo uses `FixedPoint<12>` throughout
to match GTE's fp12 format. Host tests use the same template.
No host-specific float drift. Specifically tested: round-trip
through fp12 of the values that matter (positions, angles,
matrix entries).

**Maintenance cost.** Adding a test for every change is friction.
Mitigation: tests focus on regression risk, not coverage
chasing. Format parsing changes need tests; renaming a private
method doesn't. Code review enforces this — "what test caught
this?" for risky changes, but not as a blocker for low-risk
ones.

**Speed.** A 30-second test run is the target. Linker time
dominates for native builds (~10 s); actual test execution
should be <5 s for 50 tests. If suites grow to hundreds of
tests, parallelize via doctest's `--no-skip` mode.

**Coverage measurement.** Optional Stage 5: gcov / llvm-cov
integration that produces line coverage. Reveals untested
paths. Useful but adds CI complexity. Defer until the test
suite has substantive size.

**False sense of security.** Host tests don't replace
PCSX-Redux integration tests. The runtime can pass all host
tests and still hang on real hardware due to GTE precision,
DMA timing, or VRAM layout bugs. Document: host tests are
necessary, not sufficient. The existing "run the demo, check
the screen" workflow stays as the integration gate.

**Stub drift from psyqo updates.** When psyqo upstream adds
new API surface, the stubs need to add matching no-op
implementations. Mitigation: a `make HOST_BUILD=1` build
fails loudly when the runtime calls into psyqo with a method
the stub doesn't implement. CI catches this. Manual addition
of stub method is straightforward.

**Multi-platform fragility.** Tests run on Ubuntu in CI;
contributors on Windows / macOS might see different results.
Mitigation: same toolchain everywhere (G++ on Linux, Clang on
macOS, MSVC on Windows). Test results should match
modulo whitespace in error messages. CI runs all three
platforms.

**Fuzz testing.** Random splashpack data fed to the loader to
catch crashes. Cheap, valuable. Possibly a Stage 5 addition
once basic coverage is in place. Tooling: libFuzzer with the
loader as the entry point.

**Testing GDExtension code.** The C# GDExtension wrapper
(`scripting/`) builds for the host already and is testable
via `dotnet test`. Already covered by the C# test suite;
host-mode is specifically about the MIPS-side runtime.

## Suggested entries

### For `docs/psxsplash-improvements.md`

Update the existing #3 entry with the design pointer; add:

> ### N+M. Host-mode test build for psxsplash runtime
>
> **Problem.** Runtime regressions go undetected until the
> demo is run by hand. Format bumps and behavioral changes
> can break silently for days.
>
> **Proposed direction.** `HOST_BUILD=1` make target with
> psyqo modules stubbed, runtime compiled with the host's
> C++ compiler, doctest-based test suite. Coverage focuses on
> format parsing, BVH/collision/animation math, scene manager
> transitions, music sequencer. CI runs tests on every PR.
> Full design: `docs/host-mode-testing.md`.
>
> **Status.** Filed.

### For `ROADMAP.md`

> - [ ] **Host-mode test build for psxsplash runtime.**
>       `make HOST_BUILD=1 test` builds runtime with stub psyqo
>       and runs doctest cases on the dev machine. Format
>       parsing, BVH cull, collision math, animation
>       interpolation, music sequencer, scene manager
>       transitions. CI integration on every PR. Full design:
>       `docs/host-mode-testing.md`.

## Changelog

- `2026-05-11` — Document created. Twelfth patch doc in the
  series. Closes psxsplash-improvements #3. Pairs with the
  C# test suites already running in Godot for full coverage.
