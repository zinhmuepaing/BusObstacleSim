# BusObstacleSim: instructions for Claude Code

## What this project is
A Unity add-on for a Singapore bus driver training simulator. It places random, realistic obstacles on a road while a simple test bus drives along it.
The obstacles are the deliverable. The bus is only a test rig. This is a student research project (Experiential Research Programme) reviewed by a supervisor.

## Read these first, in order
1. docs/PROJECT_SPEC.md (what to build, fixed numbers, requirements)
2. docs/ARCHITECTURE.md (scripts, data, algorithms)
3. docs/OBSTACLE_CATALOG.md (obstacle types and behaviours)
4. docs/WORKFLOW.md (how to work in Unity through MCP, roadmap, acceptance tests)

Paths are relative to the folder that contains this file. If a doc and the user's message disagree, the user's message wins. Then tell the user which doc needs updating.

## Environment
- Unity 6.6 (6000.6.4f1), Universal Render Pipeline (URP), Windows.
- Claude Code is launched from D:\ERP-Project (the folder that holds this file). The Unity project root is the subfolder BusObstacleSim/. Every Assets/... path in the docs is relative to BusObstacleSim/.
- Edit Unity files only inside BusObstacleSim/. Edit the .md files only when the user asks.
- MCP for Unity by CoplayDev, pinned to v10.0.0. Do not upgrade it.
- Required package: com.unity.splines (add via Package Manager if missing). Also Unity Test Framework.

## Hard rules
1. One small step at a time. Finish and verify it before starting the next.
2. After every script create or edit: read the Unity console, fix all errors, repeat until zero errors. Then report any warnings.
3. Use Unity 6 APIs. Example: Rigidbody.linearVelocity, not Rigidbody.velocity.
4. Input: before writing input code, check Project Settings > Player > Active Input Handling. Default to the Input System package (UnityEngine.InputSystem). Do not use UnityEngine.Input unless the setting allows it.
5. Code lives in Assets/_Project/Scripts/. Namespace BusSim.*. One class per file, file name equals class name.
6. All spawn randomness uses a seeded System.Random. Never use UnityEngine.Random for spawn decisions. Log the seed on every run.
7. The road is flat. Ignore elevation. Do all placement math in XZ. Set Y only from the road surface.
8. Traffic is left-hand (Singapore).
9. No magic numbers in code. Put tunables in ScriptableObjects or Inspector fields.
10. No per-frame allocations in Update. Pool obstacles.
11. Do not edit the scene while Play mode is on (edits are lost). Leave Play mode off when you finish a step.
12. Never touch Library/, Temp/, ProjectSettings/, or Packages/manifest.json without asking first. Ask before importing any asset pack. Record every imported asset and its licence in Assets/_Project/ASSETS.md.
13. If a tool call fails twice, stop and report what you tried. Do not loop.
14. Phase 1 code must be reusable as a Unity package later (Phase 2): no hard references to scene object names, no dependency on the test bus, runtime code separate from editor code.

## How to reply
Short. For each step report: what changed, console status, how the user can check it, and a proposed git commit message. Challenge the user's plan if you see a flaw. Tag claims you are unsure about as [Likely] or [Guessing].
