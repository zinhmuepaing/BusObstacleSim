# Known issues and working notes

Open items to fix later, plus workarounds learned the hard way. Newest first. Update when an item is fixed.

## Open

### KI-3 Branch at 40 km/h is driven over without moving
- Where: `DEBRIS_BRANCH`, hit suite case at 40 km/h. The car logs one hit, ends up 8 m past the branch, and the branch moved 0.01 m. At 20 km/h it is pushed 1.7 m as expected.
- Facts: limb is 0.44 m tall (radius 0.22), car chassis clearance is 0.3 m, so the chassis should catch it. The branch lies along the direction of travel in the test (yaw 0), which is an edge case.
- How to investigate: in Play mode run
  `BusSim.Editor.HitTestHarness.TraceCaseId = "DEBRIS_BRANCH";` then
  `BusSim.Editor.HitTestHarness.RunSuiteDeferred("Branch trace", new float[] { 40f });`
  and read `BusObstacleSim/Logs/Harness/Hit suite Branch trace.txt` (car y, pitch, branch position around impact).
- Suspects: the car rides over the limb end (wheel climbs, chassis pitches up), or the branch body is asleep and not woken by a single fast contact.

### KI-1 Cut-in car and motorcycle could launch the player car (fixed in code, NOT yet re-verified)
- Symptom: Hard profile, seed 555, full drive: car reached 2383 km/h and flipped.
- Cause: a hidden mover appeared beside the player by being steered with velocity from its idle position about 280 m away (about 14,000 m/s), and could spawn overlapping the car.
- Fix applied: `ObstacleBehaviour.PlaceAtImmediately` (teleport) and `AreaFree` (waits for a clear space). Used in `CarCutIn` and `MotorcycleFilter`.
- To verify: rerun `SimulationHarness.RunDeferred(Hard, 555, true, 150f, true, "...")` and check top speed stays under 50 km/h and min upright is above 0.7.
- **Update 2026-10-05:** the fix above was NOT enough. The seed 555 Hard drive still logged `CAR_CUTIN` at 830 m/s relative speed and the car flipped. Real cause: the cut-in car drove into the solid `ROADWORK_BARRIER` (s=384, lane 0) that the planner put in its path. The body stopped but the script kept advancing, so the demanded steering velocity grew every step (up to about 2900 m/s) and the player hit a body moving at that speed. Second fix: `ObstacleBehaviour.PlaceAt` stops driving a body that lags its scripted pose by more than `maxPoseLag` (3 m). A first version (clamping the demanded velocity to 45 m/s) gave a clean drive: no collisions, min upright 1.00, car reached s=983. The lag rule replaced the clamp and is NOT yet re-verified.
- Still open (KI-8): the planner can put a moving obstacle's path through a static one.

### KI-2 Restarting a run leaked pooled obstacles (fixed in code, NOT yet re-verified)
- Symptom: pool soak, rigidbody count rose 145 to 334 over 5 runs.
- Cause: `ObstacleSpawner.ResetRun` built a new `ObjectPool` without destroying the old one's instances.
- Fix applied: `ObjectPool.Clear()` called before the pool is replaced.
- To verify: `HitTestHarness.RunSoakDeferred(5, 45f)`, result in `Logs/Harness/Pool soak.txt`, expect "SOAK RESULT ... PASS".

### KI-8 Planner can overlap a moving obstacle's path with a static obstacle
- Seed 555 Hard: `CAR_CUTIN` (s=337, merges into lane 0 and brakes) is followed 47 m later by `ROADWORK_BARRIER` (s=384, lane 0). The cut-in car runs into the barrier and stays there.
- Impact: realistic enough physically, but it makes the cut-in hazard unreachable for the test and can confuse the autopilot. Fix idea: `SpawnPlanner` keeps a no-static zone ahead of a cut-in or filter event (its travel distance), or the cut-in checks its path with a box cast.

### KI-9 Pool soak check is unreliable
- `Pool soak.txt` (2026-10-05) reports FAIL because rigidbody count and pooled instances differ between runs. Each run uses a different seed and so a different obstacle set. The test should reuse one seed and compare counts after `ResetRun`. Not a confirmed leak.
- Collision records show `Time` 0.0 s under the harness because `Time.time` does not advance with manual physics stepping.

### KI-10 Test autopilot drives into the roadwork barrier (Hard, seed 555)
- Barrier at lane 0 leaves a 3.5 m free gap on the right (car needs about 2.8 m), yet the car hits it at 6 to 9 m/s and shoves it. Reproduced in several runs, with and without the cut-in car ahead. Not diagnosed. Suspects: the scan folds nearby cone clusters into the gap calculation, or the lateral shift is too slow for the taper. The autopilot is a test aid (KI-5), so this does not block the deliverable.

### KI-11 Traffic code review findings still open (ai-programmer agent, 2026-10-05, read-only review)
- Fixed same day: turning cars were invisible to followers (Direction stayed -1; now 1 when the turn starts), autopilot traffic probe buffer 8 to 32, traffic hit ID now unique, traffic stops when already overlapping a static obstacle footprint.
- Open: side-road cars have no car-following and no spawn clearance check (two entries released close together can overlap); gap acceptance is a one-shot check against 2 to 5 s while the turn takes about 7 s; spawn clearance ignores speed difference; magic numbers in `TrafficManager`, `TrafficVehicle`, `TrafficPlanner` (spawn clearance, hit-release seconds, prewarm count, `Lanes = 2`, `/3.6f`); the traffic hit filter reads `collision.gameObject.layer` (check it if hits with the car go unlogged) [Guessing]; `BoxCastNonAlloc` and `GetComponentInParent` run every FixedUpdate in the autopilot.
- Harness result (Easy, seed 77): 15 traffic cars planned, 7 active at once, no collisions, but the autopilot only reached s=798 in 160 s because it follows slower traffic and cannot overtake.

### KI-12 `RoadSampler.ProjectToRoad` is unreliable for points far from the road
- A point 1,000 m along the main road projected onto the 160 m side road returned s=0 and a small t, so the scenery builder thought a building site was inside the side road's corridor and left a 150 m gap. `SceneryBuilder` now measures distance to the side road's centre segment instead. `ProjectToRoad` is fine for points near the road (cars, obstacles, pedestrians). Check it before using it for anything far away.

### KI-13 Test autopilot is slow with traffic on a 3 km road
- With traffic on, the autopilot averages about 14 km/h over 1,270 m (seed 91 run), because it follows the slowest car ahead (traffic runs 25 to 45 km/h, side-road turners 15 km/h) and cannot overtake. A fast cyclist (CYCLIST_FAST, 7 m/s) held it behind for 143 s. A person driving is not affected. Fix idea: allow the autopilot to overtake into lane 1, or turn traffic off for obstacle acceptance runs (`TrafficManager.trafficEnabled`).

### KI-14 Aggressive traffic and Hard density are only lightly verified
- One Hard drive (seed 2026, 240 s simulated, about 15 minutes wall clock) logged 5 lane changes, 1 chaser released, 0 collisions and an upright car. The autopilot only reached s=381 because it brakes for nearly every one of the 54 planned events and follows slow traffic (KI-13), so it met few aggressive drivers. Chaser tailgating, cut-ins in front of the player and the Space brake have not been exercised by a person yet. Play it by hand before trusting the tuning (`TrafficSettings`: `aggressiveShare`, `chasers`, `overtakeGap`, `laneChangeClearance`).
- Long harness runs now take 10 to 15 minutes of wall-clock time with about 2,400 scenery objects and 40+ traffic cars. Use shorter runs (120 s) or turn the scenery group off for physics checks.

## Accepted deviations

- **KI-4 Car trajectory is not bit-reproducible.** Identical runs end up to about 0.35 m apart after 755 m (0.05 percent). WheelCollider physics is not deterministic across scene resets. The obstacle plan itself IS exactly reproducible (unit tested, FR7). The Plan target of 1 mm for the car was dropped.
- **KI-5 Test autopilot limits.** It overtakes static obstacles when a gap of car width plus 1 m exists, brakes for moving ones, and stops if the road is blocked. It does not react to oncoming or overtaking traffic. It is a test aid only.
- **KI-6 Hit-suite thresholds are physics-based.** Heavy handbraked vehicles must shift at least 0.25 m (van 0.1 m: a 2.2 tonne van hit at 20 km/h by a braking car slides about 0.15 m). Pedestrians fly 3.5 m at 20 km/h and about 9 m at 40 km/h.
- **KI-7 Corridor is 2.5 m** (car 1.8 m + 0.35 m each side), no longer 3.2 m for a bus. See `docs/PROJECT_SPEC.md` lines for bus size and corridor, which are now out of date.

## Workflow gotchas (MCP and Unity)

- **Do not enter Play mode right after editing scripts.** Unity defers the compile until Play ends and the old code runs. Call `refresh_unity` with wait, confirm the console is clean and no compile is running, then play.
- **The console tool shows only the first line of multi-line messages.** Harness reports are also written to `BusObstacleSim/Logs/Harness/*.txt` (`HarnessLog`). Read those files.
- **Long runs block Unity.** While a deferred harness run is simulating, MCP calls answer "ping not answered". Wait, then retry. One long run per call, never loop.
- **A script reload can drop the MCP bridge for minutes** ("no_unity_session"). Unity keeps running. Wait, or use Window > MCP for Unity > Connect.
- **Play mode can be left on** if a stop call fails. Check `Application.isPlaying` before editing the scene; edits made in Play mode are lost.
- **Python-written C# strings:** a `\n` inside a Python string becomes a real line break in the C# file. Use the Edit tool for C# that contains escape sequences.
- **`GetComponentInParent` skips prefab assets** unless `includeInactive` is true. Tests that inspect prefabs must pass `true`.
- **Menu-built content:** obstacle prefabs, definitions, profiles and the car are generated by menu commands (`BusSim > Obstacles > Build Default Content`, `BusSim > Car > Recreate Car`). Rerun them after changing the builders.

## Docs that are out of date (waiting for owner approval to edit)

- `PROJECT_SPEC.md`: bus size, 3.2 m corridor, one-carriageway road, "oncoming traffic out of scope", FR8, open question 3.
- `ARCHITECTURE.md`: RoadSettings/RoadSampler description, test bus controller, clearance window width.
- `OBSTACLE_CATALOG.md`: footprints for branch, cargo, child, elderly limit 11 s, collider description (solid bodies plus a Footprint trigger).
- `WORKFLOW.md`: M1 and M2 rows, decision log.
- `CLAUDE.md`: bus is no longer the test rig (it is a car), asset packs are now approved and imported, code lives in `BusObstacleSim/Packages/com.bussim.obstacles/`.
