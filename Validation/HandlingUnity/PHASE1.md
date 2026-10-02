# Phase 1: arcade handling

## Inspection and scope

- Unity version: **6000.3.10f1**. The only enabled build scene is
  `Assets/PDT/Scenes/DrivingTestV2.unity`; `Assets/Scenes/SampleScene.unity`
  is the other saved scene.
- Existing `PlayerCarController` used the Input System keyboard, assigned
  immediate 5 m/s forward/reverse velocity, and rotated at 90 degrees/second
  using throttle direction. This same component and its script GUID are reused.
- `DreamMobileCar` has a gravity-driven Rigidbody, mass 1, zero linear damping,
  angular damping 0.05, and frozen X/Z rotation. Its `CarBody` box collider is
  1.2 x 0.4 x 2.4 metres. The imported visual model and `CameraTarget` are reused.
  There are no WheelColliders. Physics runs at the existing 0.02-second timestep.
- The saved driving scene has **one Main Camera** with `CameraFollow`, offset
  (0, 4, -6). No four-view camera switcher was found in this checkout. Camera
  source, scene references, target hierarchy, and visual model were left unchanged.
- Existing flow: wallet/ownership verification ->
  `VerifiedVehicleUnlockCoordinator` resolves entitlements ->
  `OwnedVehicleRegistry`/`VehicleCatalog` selects `VehicleData` ->
  `VehicleSpawner.TrySpawn` checks the registry and instantiates the prefab at
  `VehicleSpawnPoint` -> existing `CameraTarget` is assigned to `CameraFollow`.
  No part of this flow was edited.
- No track, checkpoints, timer, race UI, or lap systems were added. Handling
  acceptance in Unity is the next step before Phase 2.

## Changes

The existing controller now applies incremental forward velocity changes,
separate brake/reverse behaviour, coasting, lateral grip, smoothed steering,
and reduced steering at high speed. Steering follows actual motion, including
coasting and reverse, and cannot rotate the stationary car in place.

Ground contact is required for driving and grip. Wall/ceiling contacts do not
provide traction. Airborne motion keeps gravity and existing momentum.

The prefab retains its pitch/roll locks and gains Rigidbody interpolation and
Continuous Dynamic collision detection. `ArcadeCar.physicMaterial` removes
ordinary sliding friction/bounce from the car's box; the controller supplies
directional grip. Leave the material attached when tuning.

**R** resets the same spawned instance to its original position and upright
heading, clearing linear/angular momentum. Falling 15 metres below its spawn
height also resets it. Each instantiated controller records its own spawn pose;
there is no hard-coded vehicle name, entitlement key, or spawning bypass.
This is spawn recovery, not checkpoint recovery yet.

## Test in Unity

1. Open the project in Unity **6000.3.10f1**, exit Play mode if necessary, and
   let the editor import/compile. Check the Console for red errors.
2. Open `Assets/PDT/Scenes/DrivingTestV2.unity`.
3. For a useful handling test, select `Ground` and temporarily change its
   Transform Scale from **(20, 0.5, 20)** to **(200, 0.5, 200)**. Keep its
   position **(0, -0.25, 0)**. Leave the scene unsaved and revert this change
   after testing if desired. This is a flat testing area, not a new track.
4. Enter Play mode and use the existing wallet connection/verification flow.
   Wait for the entitled car to spawn; do not place a second car in the scene.
5. Click the Game view. **W/Up** accelerates, **S/Down** brakes and then reverses,
   **A/D or Left/Right** steer, and **R** resets.
6. Hold W on a straight: speed should build progressively, reaching about
   **57.6 km/h (16 m/s)** after roughly 2 seconds on level ground. Hold S:
   the car should brake first, then reverse up to **18 km/h (5 m/s)**.
   W should also brake reverse travel before moving forward.
7. Release W: the car should coast down, with steering still available. Compare
   turns at low and high speed; steering should become gentler at high speed.
   Steer while stationary: there should be no rotation in place. Try reversing
   and steering, and short left/right taps while travelling forward.
8. Drive in circles and make sudden turns: the car should stay upright and
   recover from small slides. The current body remains level because it keeps
   the prototype's frozen pitch/roll; this is intentionally not suspension.
9. Press R while moving: the same car should return upright to the spawn point,
   stop, and remain followed by the camera. Drive off the ground edge and test
   R in midair; then repeat without R to test automatic fall recovery.
10. Optional flip check: pause Play mode, select the spawned car root and set
    Transform Z rotation to 180, resume, then press R. It should recover upright.
11. Disconnect through the existing wallet UI: the car should still despawn.
    Reconnect/reverify: the newly spawned car should have the same handling.
12. Stop Play mode before saving lasting handling settings. Changes made to a
    live clone are discarded when Play mode stops.

The saved checkout only allows verification of its one follow camera. If the
four-camera implementation lives elsewhere, test all four views there before
calling camera compatibility verified.

## Inspector tuning

Open `Assets/PDT/Prefabs/DreamMobileCar.prefab` and select the root's
**Player Car Controller** component. Settings on the prefab apply to vehicles
created by the existing entitlement flow.

| Inspector field | Default | Effect |
| --- | ---: | --- |
| Max Forward Speed | 16 | Forward target limit in m/s; multiply by 3.6 for km/h |
| Max Reverse Speed | 5 | Reverse target limit in m/s |
| Acceleration | 8 | Higher = quicker forward acceleration, in m/s² |
| Reverse Acceleration | 5 | Higher = quicker reverse acceleration |
| Braking | 18 | Higher = shorter stopping distance with opposite input |
| Coasting Deceleration | 2.5 | Lower = longer coast after releasing throttle |
| Turn Speed | 90 | Base yaw rate in degrees/second |
| High Speed Steering Multiplier | 0.4 | Lower = gentler high-speed turns |
| Full Steering Speed | 3 | Speed in m/s where the low-speed steering ramp reaches full strength |
| Steering Response | 6 | Higher = faster steering input response |
| Lateral Grip | 10 | Higher = faster removal of sideways sliding |
| Max Grip Acceleration | 25 | Higher = more lateral correction available during harder turns |
| Fall Reset Distance | 15 | Metres below the initial spawn height before automatic recovery |

Start with Acceleration, Braking, Turn Speed, and High Speed Steering Multiplier.
Change one value at a time. The mass, constraints, material, collider, and global
physics settings do not need adjustment for the initial test.

## Automated validation

Verified on **2026-09-30** with Unity **6000.3.10f1**: gameplay and editor
assemblies compiled; all runtime physics checks passed; the validation editor
exited with code **0**. See `Phase1-results.txt` for the recorded checks.
The tested controller and prefab were byte-identical to the main project's
files. Driving feel, real keyboard focus, and live wallet/camera integration
still need the manual Play-mode check above.

`Phase1HandlingValidation.cs` is an editor-only harness kept outside Assets so
it adds no runtime or editor behaviour to the main project. In a disposable
project copy, copy it into `Assets/Editor` and run the installed Unity editor:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.10f1\Editor\Unity.exe' `
  -batchmode -nographics -projectPath '<absolute disposable copy path>' `
  -executeMethod Phase1HandlingValidation.RunBatch `
  -logFile '<absolute log path>'
```

Do **not** add `-quit`: the harness enters Play mode before testing, writes
`Logs/Phase1-results.txt`, and exits with success/failure status. It creates an
empty scene so no wallet/network components run, loads the actual car prefab,
and manually steps a separate Unity physics scene. Inputs are set directly to
make physics checks deterministic; actual keyboard/focus/camera feel still
requires the manual checks above. The harness does not test live wallet access.
