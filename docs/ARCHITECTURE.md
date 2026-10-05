# Architecture

## 1. Folder layout (inside BusObstacleSim/)
```
Assets/_Project/
  Scripts/Runtime/   Core, Road, Obstacles, Spawning, TestRig
  Scripts/Editor/
  Tests/EditMode/
  Prefabs/           Obstacles, Road, TestRig
  Data/              Obstacles, Difficulty   (ScriptableObjects)
  Scenes/
  Materials/
  ASSETS.md          every imported asset and its licence
```
Use assembly definitions: BusSim.Runtime, BusSim.Editor, BusSim.Tests. This keeps Phase 2 packaging clean.

## 2. Design decision: plan first, activate later
The spawner works in two stages.
1. Plan: at run start, build the full list of spawn events for the whole road from the seed. Pure C#, no scene access. Easy to validate, test and replay.
2. Activate: at runtime, when the bus gets within spawn-ahead distance of an event, take an object from the pool and activate it. Despawn after the bus passes.

Reason: FR3 (always passable) and FR7 (same seed, same run) are far easier to guarantee on a precomputed plan than on live random spawning.

## 3. Components

| Class | Kind | Responsibility |
|---|---|---|
| RoadSettings | ScriptableObject | lane count, lane width, footpath width, road length |
| RoadSampler | MonoBehaviour | wraps SplineContainer. Length, GetPoint(s, t), GetForward(s), GetRight(s), ProjectToRoad(worldPos) returns (s, t) |
| ObstacleDefinition | ScriptableObject | id, name, category, prefab, isDynamic, footprint (width, length), allowed t range, weight, minDifficulty, minGapAfter, maxBlockSeconds, triggerTimeToArrival, dangerLevel 1 to 5 |
| DifficultyProfile | ScriptableObject | eventsPerKm, type weight overrides, allowed categories, min gap |
| SpawnEvent | plain struct | definition, s, t, yaw, eventIndex |
| SpawnPlanner | plain C# class | seed + profile + definitions + road settings produces List of SpawnEvent |
| ClearanceValidator | static class | pure function, no Unity scene calls, unit tested |
| ObstacleSpawner | MonoBehaviour | holds the plan, activates events near the bus, pools, despawns |
| ObjectPool | plain C# class | pooling per prefab |
| ObstacleBehaviour | abstract MonoBehaviour | Init(context), Activate(), Tick(), Finish(). Context holds RoadSampler, bus transform, per-event rng |
| StaticObstacle, PedestrianCrossing, ... | MonoBehaviour | one behaviour per catalog type (see OBSTACLE_CATALOG.md) |
| TestBusController | MonoBehaviour | WASD or arrows, Rigidbody, Input System |
| FollowCamera | MonoBehaviour | chase camera for the test bus |
| RunLogger | MonoBehaviour | seed, counts per type, rejected placements |
| DebugGizmos | MonoBehaviour | draws corridor, spawn windows, rejected placements |

## 4. Planning algorithm
1. rng = new System.Random(seed).
2. s = startBuffer (80 m). Loop while s < roadLength minus endBuffer:
   1. Next event distance: baseSpacing = 1000 / eventsPerKm. Advance s by max(minGap, baseSpacing * random factor 0.5 to 1.5).
   2. Pick a definition by weighted random among allowed definitions.
   3. Pick t within the definition's allowed range.
   4. Build the footprint rectangle at (s, t).
   5. Static obstacle: run ClearanceValidator. If invalid, retry up to 8 times with a new t or type. If still invalid, skip and count a rejection.
   6. Dynamic obstacle: only enforce min gap from other events. Its runtime block is limited by maxBlockSeconds.
   7. Add the event to the plan.
3. Return the plan.

## 5. ClearanceValidator algorithm
Input: candidate footprint, accepted static obstacles, road settings, min clear corridor.
1. Window W = candidate length plus 15 m before and after, centred on candidate s.
2. Collect the candidate and all accepted static obstacles whose footprint overlaps W.
3. For each, take the lateral interval [t minus width/2, t plus width/2]. Merge overlapping intervals.
4. Free gaps = space between road edges (-roadWidth/2 to +roadWidth/2) and the merged intervals.
5. Valid only if the largest free gap is at least the min clear corridor.
This is conservative: it guarantees a straight line through the window, so the bus never has to weave.

## 6. Runtime activation
- Each frame, the spawner finds the bus s with ProjectToRoad. When busS + spawnAhead >= event.s, activate the event.
- Despawn when busS minus despawnBehind > event.s plus event length.
- Dynamic behaviours start moving when the bus time-to-arrival at the obstacle falls below triggerTimeToArrival (bus speed divided by distance).
- Any randomness inside a behaviour uses an rng seeded from (runSeed, eventIndex), so replays match.

## 7. Tests (EditMode, Unity Test Framework)
- ClearanceValidator: empty road passes, obstacle that leaves exactly 3.2 m passes, 3.19 m fails, two obstacles that together block the road fail, window edges.
- SpawnPlanner: same seed gives identical plan. Different seeds differ. Min gap respected.
- Property test: for 1000 seeds, no plan ever violates the corridor rule.
