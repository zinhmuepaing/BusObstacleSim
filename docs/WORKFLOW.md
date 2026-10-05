# Workflow, roadmap and acceptance tests

## 1. Session start checklist (user)
1. Unity is open on BusObstacleSim. Window > MCP for Unity shows a green session. If it says "No Session", click Connect.
2. Git is clean, or the last working state is committed.
3. In PowerShell: cd D:\ERP-Project, then run claude. In Unity, MCP for Unity > Client Configuration > Client Project Dir must be D:\ERP-Project, or the Unity server will not appear in /mcp.
4. In Claude Code run /mcp and confirm the Unity server is connected.
5. Paste the start prompt below.

## 2. Start prompt
Read CLAUDE.md and the four docs it lists. Summarise the plan in 8 lines or fewer. Then do Milestone <N> only. Stop after its acceptance test and report.

## 3. Per-step loop (Claude)
1. State the step in one line.
2. Make the change through MCP.
3. Read the Unity console. Fix errors until there are zero.
4. If relevant, enter Play mode, verify, then exit Play mode.
5. Save the scene.
6. Report: files changed, console state, how the user can check it, proposed commit message.

## 4. MCP gotchas
- Unity recompiles after script changes. Wait for the compile to finish before adding the new component. [Likely]
- Scene edits made during Play mode are lost.
- If MCP disconnects: Window > MCP for Unity > Connect, then /mcp in Claude Code.
- If a tool call fails twice, stop and report.

## 5. Git
- Commit after every working step. Message format: "M3: add ClearanceValidator and tests".
- Commit .meta files. Never commit Library/, Temp/, Logs/, UserSettings/.
- Git repo root is D:\ERP-Project, so these docs are versioned with the Unity project. The standard Unity .gitignore works there because its patterns match at any depth.
- Use the standard Unity .gitignore.

## 6. Roadmap and acceptance tests

| Milestone | Deliverable | Acceptance test |
|---|---|---|
| M0 Setup | MCP connected, git initialised | Claude creates a cube and reads the console through MCP. Repo has a first commit. |
| M1 Road | Spline road, 7.0 m wide, 1000 m, lane markings, kerbs, footpaths, sky | Road visible in Scene view. RoadSampler returns correct points. A debug line shows s and t at the cursor. |
| M2 Test bus | 2.5 m x 12 m box bus with WASD and arrows, follow camera | Drive start to end at up to 50 km/h without leaving the road. No console errors. |
| M3 Planner, validator, cones | SpawnPlanner, ClearanceValidator, ObjectPool, ObstacleSpawner, CONE_CLUSTER | EditMode tests pass. 1000 seeds never break the corridor rule. Same seed gives the same cones. Gizmos show corridor. |
| M4 Jaywalker | PED_JAYWALK_ADULT | Pedestrian triggers at 3.5 s time-to-arrival, crosses, clears within maxBlockSeconds. Bus can stop in time at 50 km/h. |
| M5 Catalog and difficulty | Remaining M5 types, Easy, Normal, Hard profiles | Event density matches the profile within 20 percent over 20 seeds. RunLogger prints counts per type. |
| M6 Zones and realism | Bus stop and school zones, M6 types, realistic models and materials | Zone obstacles only appear in zones. ASSETS.md lists every asset and licence. |
| M7 Plugin | UPM package with sample scene and README | A fresh Unity project installs the package and generates obstacles in under 10 minutes. |

Supervisor review points: after M2 (road and bus), after M4 (first dynamic obstacle), after M6 (realism), before M7.

## 7. Decision log
Append new decisions at the bottom.

| Date | Decision | Reason |
|---|---|---|
| 2026-10-05 | Use CoplayDev MCP for Unity v10.0.0 | Free, MIT, works with Claude Code. Unity's official MCP needs a Unity AI beta plan. |
| 2026-10-05 | Spline road with 2 same-direction lanes, left-hand | One path, width and direction in one place. |
| 2026-10-05 | 3.2 m minimum clear corridor, oncoming lane never used | Bus 2.5 m plus margin. Needs supervisor confirmation. |
| 2026-10-05 | Plan spawns from a seed first, activate at runtime | Guarantees passability and reproducible runs. |
| 2026-10-05 | Behavioural realism before visual realism | Placeholder shapes first, models swapped in later. |
