# Obstacle catalog

All sizes are placeholders. Check them against the real prefab before relying on them.
Milestones refer to docs/WORKFLOW.md.

## 1. Types

| ID | Name | Static or dynamic | Footprint W x L (m) | Where | Behaviour | Danger 1 to 5 | Milestone |
|---|---|---|---|---|---|---|---|
| CONE_CLUSTER | Traffic cone taper | static | 1.0 x 8 | lane edge | cones in a short taper that narrows the lane | 2 | M3 |
| PED_JAYWALK_ADULT | Jaywalking adult | dynamic | 0.6 x 0.6 | footpath to opposite footpath | starts when bus time-to-arrival is 3.5 s, walks at 1.4 m/s | 4 | M4 |
| ROADWORK_BARRIER | Road works lane closure | static | 3.5 x 15 | closes one lane | barrier, cones, sign, optional worker | 3 | M5 |
| DEBRIS_BRANCH | Fallen branch | static | 0.4 x 2.5 | any lane | none | 2 | M5 |
| DEBRIS_CARGO | Fallen cargo or tyre | static | 0.8 x 0.8 | any lane | none | 2 | M5 |
| STALLED_CAR | Breakdown with hazards | static | 1.8 x 4.5 | one lane | hazard lights blink, warning triangle 30 m behind | 3 | M5 |
| DOUBLE_PARKED_VAN | Delivery van double parked | static | 2.0 x 5.5 | left edge | hazard lights, optional driver steps out | 3 | M5 |
| PED_ELDERLY | Elderly slow crosser | dynamic | 0.6 x 0.6 | crossing | 0.8 m/s, may pause mid-road | 5 | M5 |
| CYCLIST_EDGE | Cyclist or PMD rider | dynamic | 0.7 x 1.8 | within 0.8 m of kerb | rides at 4 m/s, may swerve | 3 | M5 |
| MOTORCYCLE_FILTER | Motorcycle lane filtering | dynamic | 0.8 x 2.1 | between lanes | overtakes the bus alongside, higher speed | 3 | M5 |
| CAR_CUTIN | Car cutting in and braking | dynamic | 1.8 x 4.5 | adjacent lane merging in | merges ahead of bus then brakes hard | 4 | M5 |
| PED_CHILD_RUN | Child running across | dynamic | 0.5 x 0.5 | school zone | 2.5 m/s, no warning | 5 | M6 |
| BUSSTOP_BLOCK | Vehicle blocking bus stop bay | static | 2.0 x 5.5 | bus stop zone | none | 2 | M6 |
| BUSSTOP_RUSH | Passenger rushing to the bus | dynamic | 0.6 x 0.6 | bus stop zone | runs toward the bus door | 4 | M6 |

Types in M6 need road zones (bus stop, school) marked along the spline.

## 2. Rules every type must follow
- Static types obey the clearance rule in docs/PROJECT_SPEC.md FR3.
- Dynamic types declare maxBlockSeconds. Example: crossing 7.0 m at 1.4 m/s takes 5 s. Elderly at 0.8 m/s takes about 8.75 s, so set 10.
- Dynamic types never start moving until their trigger condition is met.
- The pedestrian crossing speed and the trigger time-to-arrival must give the bus driver a fair chance to brake at the test bus speed.

## 3. Prefab requirements
- Root has an ObstacleBehaviour subclass, a Collider, tag "Obstacle", layer "Obstacles".
- Pivot at the ground centre of the footprint. Forward axis points along the road at yaw 0.
- Footprint in the ObstacleDefinition matches the real collider bounds.
- Start with primitives (cube, capsule). Swap in realistic models later without changing behaviour scripts.

## 4. How to add a new type
1. Make the prefab following section 3.
2. Write or reuse a behaviour script.
3. Create an ObstacleDefinition asset in Assets/_Project/Data/Obstacles.
4. Add it to a DifficultyProfile.
5. Run the EditMode tests. Add the row to the table above.

## 5. Singapore context notes
Verify each against LTA or other official sources before treating it as fact.
- Left-hand traffic.
- Jaywalking is most common near bus stops, hawker centres and housing estates. [Likely]
- School zones and Silver Zones exist, with lower speed limits. [Likely]
- Delivery vans stopping in a lane is common. [Likely]
- Cyclists and personal mobility device riders share road edges. [Likely]
- Sudden heavy rain can bring down branches and cut visibility. [Likely]
