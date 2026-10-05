# Project spec

## 1. Goal
Generate random, realistic road obstacles in a Unity scene for a Singapore bus driver training simulator. Each run differs, and the driver always has room to pass.

## 2. Scope
In scope: road, obstacle prefabs and behaviours, spawner, clearance validator, difficulty settings, simple drivable test bus with camera, tests, docs.
Out of scope: realistic bus model or vehicle physics, oncoming traffic, traffic lights, scoring, VR, multiplayer, elevation.

## 3. Phases
- Phase 1: Unity scripts and a working scene, tested in the Unity editor.
- Phase 2: package it as a reusable Unity plugin (UPM package) that anyone can install and use to generate obstacles.
- Do not start Phase 2 until Phase 1 passes every acceptance test in docs/WORKFLOW.md and the supervisor has seen it.
- Write Phase 1 so Phase 2 is a repackage, not a rewrite (see CLAUDE.md rule 14).

## 4. Fixed numbers
Every value is configurable. Status "confirm" means the supervisor has not approved it yet.

| Item | Default | Status |
|---|---|---|
| Engine | Unity 6.6 (6000.6.4f1), URP | confirmed |
| Traffic side | Left | confirmed |
| Space | Flat road, ignore Y, plan in XZ | confirmed by supervisor |
| Road | One carriageway, 2 lanes, same direction, 3.5 m each, 7.0 m drivable width, 1000 m long | decision, confirm |
| Footpath | 2.0 m each side, used as pedestrian start point | decision |
| Bus size | 2.5 m wide, 12 m long | assumption, confirm |
| Min clear corridor | 3.2 m (bus 2.5 m plus 0.35 m each side) | decision, confirm |
| Oncoming lane | Never used for passing | decision |
| Test bus top speed | 50 km/h, adjustable | assumption |
| Spawn ahead distance | 150 m ahead of bus | default |
| Despawn behind | 50 m behind bus | default |
| Min gap between obstacle events | 40 m along road | default |
| Difficulty presets | Easy 4, Normal 8, Hard 14 events per km | default |

## 5. Space and coordinates
- Road space: s is metres along the spline. t is lateral offset from the centreline, positive to the right of travel.
- Lane centres: left lane t = -1.75, right lane t = +1.75. The bus starts in the left lane.
- Convert road space to world space only through RoadSampler. Use world X and Z. Y is the road surface height.
- An obstacle footprint is a rectangle in (s, t) space.

## 6. Requirements
- FR1: The road exists in the scene, built from a spline, with lane markings, kerbs and footpaths.
- FR2: Obstacles appear at random positions along the road ahead of the bus, driven by a seed.
- FR3: Every static obstacle is validated. The road must keep a clear corridor of at least the min clear corridor width across the obstacle length plus 15 m before and after.
- FR4: Dynamic obstacles (pedestrians, vehicles) may block the road briefly. Each declares maxBlockSeconds and must clear within it.
- FR5: Obstacle types are ObstacleDefinition ScriptableObjects. Adding a type means a new asset and prefab, with no spawner code change.
- FR6: A difficulty profile controls density, type weights and allowed categories.
- FR7: The same seed produces the same obstacle sequence.
- FR8: The test bus drives with W A S D or the arrow keys, with a follow camera.
- FR9: A debug view (gizmos) shows the corridor, spawn windows and rejected placements.
- FR10: Every run logs the seed, obstacle count per type, and rejected placement count.

## 7. Realism targets
- Behavioural realism first: obstacles act the way they do in Singapore traffic.
- Visual realism second: start with placeholder shapes, then swap in realistic models without changing behaviour scripts.
- Model sources: Unity Asset Store free packs, Sketchfab (check the licence). Do not model assets by hand.

## 8. Open questions for the supervisor
These do not block Phase 1.
1. Confirm bus size and the 3.2 m corridor.
2. Is a short full-road block by a crossing pedestrian acceptable, since the driver must brake?
3. Should an oncoming lane exist visually, even though it is never used?
4. Phase 2 form: an installable Unity package, or a tool that generates scripts on request?
5. Who judges realism, and against what checklist?
