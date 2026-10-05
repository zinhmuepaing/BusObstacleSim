# Active session state

Read this after a compaction or restart. Updated 2026-10-05.

## Task
BusObstacleSim milestones M8 to M11 (plan: C:\Users\Pi\.claude\plans\add-real-3d-models-woolly-popcorn.md).
Done: car, solid obstacles + hit reactions, CC0 models, dual carriageway + T-junction, traffic code, scenery builder
(`BusSim/Scenery/Create Scenery`, seeded; houses, flats, trees, street lights).

## In progress
- Verifying a full Hard drive with obstacles (SimulationHarness.RunDeferred, logs in BusObstacleSim/Logs/Harness/).
- Last result (check2): CAR_CUTIN collision logged at 830 m/s relative speed and the car flipped (min upright -0.10);
  ROADWORK_BARRIER hit 3 times. Added a velocity clamp (`maxScriptedSpeed`) in ObstacleBehaviour.PlaceAt; check3 tests it.
- The scene's ObstacleSpawner.vehicle and CarAutopilot.road references were null; rewired and saved.

## Decisions
- Never run git; propose commit messages. Edit docs only when asked.
- Docs needing the user's approval are listed in docs/KNOWN_ISSUES.md.
- CCGS (Donchitos/Claude-Code-Game-Studios) subset installed in .claude: 12 agents, 7 skills, 6 hooks, 3 rules.
  Use unity-specialist, ai-programmer, engine-programmer, qa-tester, etc. for reviews; /code-review, /soak-test, /perf-profile, /bug-report.

## Open
- KI-3 branch case, pool soak check (its seed-varying rigidbody count test is flawed), traffic verification in Play,
  character animation and cyclist pose check, package smoke test in a fresh project, docs updates.
