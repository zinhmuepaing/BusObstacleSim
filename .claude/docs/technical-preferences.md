# Technical Preferences

Project facts for the CCGS skills and agents. CLAUDE.md and docs/ at the repo root remain the authority; if they disagree with this file, they win.

## Engine & Language

- **Engine**: Unity 6.6 (6000.6.4f1)
- **Language**: C# (namespaces `BusSim.*`, one class per file, file name equals class name)
- **Rendering**: Universal Render Pipeline (URP)
- **Physics**: PhysX, WheelCollider car, dynamic Rigidbody obstacles. Use Unity 6 APIs (`Rigidbody.linearVelocity`).

## Input & Platform

- **Target Platforms**: Windows PC (Phase 1 is an editor add-on, Phase 2 a Unity package inside a bus driver training simulator)
- **Input Methods**: Keyboard (Input System package)
- **Primary Input**: Keyboard
- **Gamepad Support**: None
- **Touch Support**: None
- **Platform Notes**: Do not use `UnityEngine.Input`. Left-hand traffic (Singapore).

## Naming Conventions

- **Classes**: PascalCase, file name equals class name
- **Variables**: camelCase private fields, PascalCase properties
- **Signals/Events**: C# events, PascalCase (`ObstacleActivated`)
- **Files**: one class per file
- **Scenes/Prefabs**: PascalCase, obstacle prefabs in `Assets/_Project/Prefabs/Obstacles`
- **Constants**: PascalCase `const` or `static readonly`; no magic numbers, tunables go in ScriptableObjects or Inspector fields

## Performance Budgets

- **Target Framerate**: 60 FPS
- **Frame Budget**: 16.6 ms
- **Draw Calls**: not set
- **Memory Ceiling**: not set
- **Hot-path rule**: no per-frame allocations in Update; obstacles and traffic are pooled

## Testing

- **Framework**: NUnit (Unity Test Framework). EditMode tests in `Assets/_Project/Tests/EditMode`.
- **Minimum Coverage**: planner, validator, road sampler and catalog logic covered by EditMode tests
- **Required Tests**: seeded determinism (same seed gives same plan), clearance corridor, no obstacle in a junction zone
- **Simulation checks**: `SimulationHarness` (Play mode, manual physics stepping), logs in `BusObstacleSim/Logs/Harness/`

## Forbidden Patterns

- `UnityEngine.Random` for spawn decisions (use a seeded `System.Random`)
- Elevation maths (the road is flat; placement in XZ)
- Hard references to scene object names or to the test car in package runtime code
- Editing the scene while Play mode is on
- Editing `Library/`, `Temp/`, `ProjectSettings/` or `Packages/manifest.json` without asking
- Git commands run by the assistant (the user commits; propose a message)

## Allowed Libraries / Addons

- com.unity.splines, Unity Test Framework, Input System
- Free CC0 art only (Kenney, Quaternius), each recorded in `Assets/_Project/ASSETS.md`

## Architecture Decisions Log

- No ADRs yet. Key decisions so far are in docs/ARCHITECTURE.md and docs/KNOWN_ISSUES.md.

## Engine Specialists

- **Primary**: unity-specialist
- **Language/Code Specialist**: unity-specialist
- **Shader Specialist**: unity-shader-specialist
- **UI Specialist**: not installed (no UI in scope)
- **Additional Specialists**: gameplay-programmer (obstacle behaviours), ai-programmer (traffic, autopilot), engine-programmer (physics, vehicle), technical-artist (models, materials, scenery), performance-analyst, qa-lead, qa-tester, lead-programmer, technical-director, tools-programmer (editor builders)
- **Routing Notes**: obstacle behaviour changes go to gameplay-programmer, traffic and autopilot to ai-programmer, WheelCollider and Rigidbody tuning to engine-programmer, editor builders to tools-programmer.

### File Extension Routing

| File Extension / Type | Specialist to Spawn |
|-----------------------|---------------------|
| Game code (C#) | unity-specialist |
| Shader / material files | unity-shader-specialist |
| UI / screen files | unity-specialist |
| Scene / prefab / level files | technical-artist |
| Editor tooling (`Editor/*.cs`) | tools-programmer |
| General architecture review | technical-director |
