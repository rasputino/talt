# Transit & Logistics Tycoon (TALT) Constitution

TALT is an original transport management game in C# on .NET 10 for Windows, Linux and macOS, built
without a game engine on Silk.NET and WebGPU (wgpu-native). Development is mostly AI-assisted and
reviewed by a human C# developer.

## Core Principles

### I. Core and Client Separation (NON-NEGOTIABLE)

- `Talt.Core` MUST hold all game logic and MUST NOT reference graphics, windowing, audio or platform
  libraries.
- Everything in `Talt.Core` MUST be testable without a GPU or a window.
- `Talt.Client` (window, input, rendering, audio, UI) depends on `Talt.Core`; `Talt.Core` MUST NEVER
  depend on `Talt.Client`.
- Platform-specific code MUST live only in the client, behind small interfaces.

**Rationale:** keeps the simulation testable, deterministic and portable, and lets tools and
benchmarks run headless.

### II. Performance Is a Feature

- The global budgets in `docs/SPECKIT_GUIDE.md` (section 5) are acceptance criteria.
- Simulation and rendering hot paths MUST NOT allocate per tick or per frame.
- Every feature that touches simulation or rendering MUST add or update a BenchmarkDotNet benchmark
  and record its results in `docs/ARCHITECTURE.md`.
- A measured regression beyond 10% on a reference benchmark blocks the merge unless the plan
  justifies it.

**Rationale:** smooth play on very large maps is the game's main differentiator; it is lost
gradually unless measured continuously.

### III. Data-Oriented, Deterministic Simulation

- Simulation state MUST be stored in contiguous, cache-friendly structures (structure of arrays or
  an ECS).
- The simulation MUST advance in fixed ticks and MUST NOT read the wall clock or use unseeded
  randomness.
- The same seed and the same player commands MUST produce the same state, tick by tick.
- Parallel execution MUST produce results identical to sequential execution, verified by tests.
- Player actions MUST reach the simulation as commands.

**Rationale:** determinism enables reliable tests, saves, replays and future multiplayer; data
orientation enables parallelism at scale.

### IV. Tested Increments

- All `Talt.Core` logic MUST have xUnit tests written with or before the code.
- Every bug fix MUST include a regression test.
- Each user story MUST be independently testable.
- The full test suite MUST pass on every commit; tests MUST NOT depend on a GPU, a window or timing.

**Rationale:** AI-assisted development produces code quickly; tests are what keep it correct.

### V. Readable, Idiomatic C#

- Code MUST be reviewable and debuggable by a human: clear names, small focused types, XML
  documentation on public APIs, comments that explain why.
- Unsafe code and native interop MUST be confined to the rendering and platform layers.
- Simple designs are the default; any added complexity MUST be justified in the plan.

**Rationale:** the developer must be able to understand, verify and fix every change.

### VI. Original Content and Legal Compliance (NON-NEGOTIABLE)

- No code, graphics, sounds, music, text or level data from OpenTTD, Transport Tycoon, Voxel Tycoon
  or any other game.
- Existing games MAY be studied only to understand mechanics; implementations MUST be designed
  independently.
- The trademark "Transport Tycoon" MUST NOT be used.
- The project is source-available under PolyForm Strict 1.0.0, copyright Pablo León Manzano.
- Third-party libraries MUST have licenses compatible with distributing a closed commercial game.

**Rationale:** protects the author's full rights over the game and avoids copyright and trademark
claims.

### VII. Data-Driven Content

- Game content (cargo types, industries, vehicles, buildings, eras, balancing values) MUST be defined
  in versioned data files loaded at startup, not hard-coded.
- Data files MUST be validated at load with clear error messages.

**Rationale:** makes balancing easy and leaves the door open for modding.

### VIII. Living Documentation in English

- All documentation, code comments and identifiers MUST be in English.
- `docs/ARCHITECTURE.md` MUST be updated by every feature that changes structure, data flow or
  performance numbers.
- `docs/VISION.md` MUST record product decisions.
- Player-facing text MUST be localizable, with English and Spanish at minimum.

**Rationale:** shared, current context is what lets each AI-assisted session continue correctly.

## Technology Constraints

- .NET 10, C# latest, nullable enabled, warnings as errors, recommended analyzers, central package
  management (`Directory.Packages.props`).
- Window, input and GPU bindings: Silk.NET. GPU API: WebGPU via wgpu-native. No game engine.
- Tests: xUnit. Benchmarks: BenchmarkDotNet. CI: GitHub Actions on Windows, Linux and macOS.
- New dependencies MUST be justified in the plan (purpose, license, maintenance, size).

## Development Workflow

- One Spec Kit feature per branch; each feature MUST leave the game buildable and runnable.
- Implementation proceeds in small tasks; after each user story the app is run and verified.
- AI agents MUST NOT commit, push or merge unless the developer explicitly asks.
- A feature is done when tests pass, benchmarks meet budgets, the app runs and the feature has been
  demonstrated, and documentation is updated.

## Governance

- This constitution supersedes other practices.
- Every plan MUST include a Constitution Check; violations MUST be justified in the plan's
  complexity tracking, or the design changes.
- Amendments are proposed as changes to this file, approved by the developer, and versioned with
  semantic versioning: MAJOR for removed or redefined principles, MINOR for new principles or
  materially expanded guidance, PATCH for clarifications.
- Reviews of specs, plans and implementations MUST verify compliance with these principles.

**Version**: 1.0.0 | **Ratified**: 2026-11-22 | **Last Amended**: 2026-11-22
