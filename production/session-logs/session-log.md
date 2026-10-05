## Archived Session State: 20261005_151710
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
---

## Session End: 20261005_151710
### Commits
a15b371 M8: physical car (WheelCollider), solid obstacles with hit reactions, autopilot overtaking
ce349aa M3-M7: obstacle planner, 14-type catalog, difficulty, zones, UPM package
2ccd3b2 M2 fixes: chase camera target, snappier bus handling, road shape, edge guard
c265831 M1: spline road with markings, kerbs, footpaths, sky, RoadSampler and tests
### Uncommitted Changes
BusObstacleSim/Assets/Scenes/SampleScene.unity
BusObstacleSim/Assets/_Project/ASSETS.md
BusObstacleSim/Assets/_Project/Data/Obstacles/BUSSTOP_BLOCK.asset
BusObstacleSim/Assets/_Project/Data/Obstacles/CAR_CUTIN.asset
BusObstacleSim/Assets/_Project/Data/Obstacles/DOUBLE_PARKED_VAN.asset
BusObstacleSim/Assets/_Project/Data/Obstacles/STALLED_CAR.asset
BusObstacleSim/Assets/_Project/Data/VehicleSettings.asset
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/BUSSTOP_BLOCK.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/BUSSTOP_RUSH.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/CAR_CUTIN.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/CONE_CLUSTER.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/CYCLIST_EDGE.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/DEBRIS_BRANCH.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/DEBRIS_CARGO.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/DOUBLE_PARKED_VAN.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/MOTORCYCLE_FILTER.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/PED_CHILD_RUN.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/PED_ELDERLY.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/PED_JAYWALK_ADULT.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/ROADWORK_BARRIER.prefab
BusObstacleSim/Assets/_Project/Prefabs/Obstacles/STALLED_CAR.prefab
BusObstacleSim/Assets/_Project/Prefabs/Vehicle/Car.prefab
BusObstacleSim/Assets/_Project/Tests/EditMode/RoadSamplerTests.cs
BusObstacleSim/Assets/_Project/Tests/EditMode/SpawnPlannerTests.cs
BusObstacleSim/Packages/com.bussim.obstacles/Editor/CarBuilder.cs
BusObstacleSim/Packages/com.bussim.obstacles/Editor/ObstacleContentBuilder.cs
BusObstacleSim/Packages/com.bussim.obstacles/Editor/SimulationHarness.cs
BusObstacleSim/Packages/com.bussim.obstacles/Editor/ZoneBuilder.cs
BusObstacleSim/Packages/com.bussim.obstacles/Runtime/Obstacles/ObstacleBehaviour.cs
BusObstacleSim/Packages/com.bussim.obstacles/Runtime/Obstacles/PassengerRush.cs
BusObstacleSim/Packages/com.bussim.obstacles/Runtime/Obstacles/PedestrianCrossing.cs
BusObstacleSim/Packages/com.bussim.obstacles/Runtime/Obstacles/WalkRig.cs
BusObstacleSim/Packages/com.bussim.obstacles/Runtime/Road/RoadMeshBuilder.cs
BusObstacleSim/Packages/com.bussim.obstacles/Runtime/Road/RoadSettings.cs
BusObstacleSim/Packages/com.bussim.obstacles/Runtime/Road/ZoneType.cs
BusObstacleSim/Packages/com.bussim.obstacles/Runtime/Spawning/ObjectPool.cs
BusObstacleSim/Packages/com.bussim.obstacles/Runtime/Spawning/ObstacleSpawner.cs
BusObstacleSim/Packages/com.bussim.obstacles/Runtime/Spawning/SpawnPlanner.cs
BusObstacleSim/Packages/com.bussim.obstacles/Runtime/Vehicle/CarAutopilot.cs
BusObstacleSim/ProjectSettings/EditorBuildSettings.asset
BusObstacleSim/ProjectSettings/ProjectSettings.asset
CLAUDE.md
---

