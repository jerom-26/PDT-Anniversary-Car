# PDT Meadow Road — free driving environment

## What was built

One baked scenic road loop in the existing **DrivingTestV2** scene. It includes
textured asphalt, ivory edge lines, broken amber centre markings, flush gravel
shoulders, a widened arrival area, meadow hills, two shades of pine groves,
rocks, roadside signs, benches, delineators, and guardrails on the outer hillside.
There are no race systems or new gameplay behaviours.

The layout is **830.5 m** long. Pavement is **10 m** wide, with **2 m** shoulders
on both sides. The tightest centreline bend radius is **24.9 m** and the maximum
grade is **2.7%**. A casual loop at 8–12 m/s takes approximately **69–104 seconds**.
The car's maximum remains 16 m/s; ease off for the tighter bend.

The existing DreamMobile model is approximately **1.10 × 0.58 × 2.00 m**
(width × height × length). Its existing collision body is **1.2 × 0.4 × 2.4 m**.
The road was designed and tested against this actual prefab.

## Project inspection and reuse

- Unity **6000.3.10f1**, URP, Input System, and TextMesh Pro were already present.
- No Splines, ProBuilder, road package, or scenery collection was installed.
- No packages were added. The environment uses generated meshes, small grain
  textures, primitives, existing TMP font assets, and native mesh/box colliders.
- `Assets/PDT/Scenes/DrivingTestV2.unity` remains the enabled build scene.
- The car, vehicle controller, camera, spawn transform, wallet components,
  entitlement mapping, and Web3 scripts are unchanged by this task.
- Existing Directional Light and Global Volume are retained. Only environment
  ambient lighting and distance fog were adjusted in the scene RenderSettings.

## Exactly how to test

1. Open the project in Unity 6000.3.10f1 and let compilation/import finish.
2. Open **Assets/PDT/Scenes/DrivingTestV2.unity**. If it was already open while
   files changed, reopen it from the Project window to load the updated scene.
   Preserve any unrelated unsaved scene edits in a separate scene before reloading.
3. In the Hierarchy, verify **PDT Scenic Road** is present and **Ground** is inactive.
   The original ground is retained only as a disabled fallback.
4. Enter Play mode and connect/verify through the same working wallet flow.
   The entitled car should appear at the unchanged **VehicleSpawnPoint** on the
   widened paved arrival area, facing along the road.
5. Click Game view. Drive with **W/S/A/D or arrow keys**. Follow the road either
   way, try both shoulders, climb the gentle hillside, and return to the arrival
   area. There are no checkpoints, timers, or prescribed start/finish.
6. Approach a hillside guardrail slowly and confirm collision. Trees, rocks,
   signs, and benches also have collision; slender delineators are decorative.
7. Press **R** while moving or after leaving the pavement. The same car should
   return upright to the arrival area and the same camera should keep following.
   The existing fall recovery also remains available if you drive beyond the map.
8. Confirm the Console has no red errors. The headless checks do not exercise
   the live wallet connection or subjective keyboard/camera feel.

## Adjusting the environment

Select **Assets/PDT/Environment/Road Settings.asset** in the Project window:

| Setting | Default | What it changes |
| --- | ---: | --- |
| Road Width | 10 | Paved width in metres |
| Shoulder Width | 2 | Gravel width per side in metres |
| Elevation Scale | 1 | Road elevation; 0 flattens the road |
| Samples Per Span | 48 | Curve mesh resolution; keep 48 initially |
| Tree Count | 190 | Requested vegetation density, subject to clearance checks |
| Rock Count | 65 | Scenery placement attempts; road-adjacent placements are skipped |
| Scenery Seed | 80 | Reproducible tree/rock distribution |
| Background Hill Height | 12 | Height contribution of hills away from the road |
| Control Points | 11 points | Closed road centreline, in metres |

After editing, run **PDT → Environment → Rebuild Scenic Road Assets**.
The scene's prefab updates from the baked assets; there is no generator running
every frame. This operation replaces generated prefab children/meshes. Keep
hand-placed additions in a separate scene root so rebuilding cannot replace them.
Material changes can be made directly in the Environment folder; rebuilding
restores the builder's palette, so change `ScenicRoadBuilder.Mat` calls for a
permanent custom palette.

Keep the first three control points on **x=0, y=0**, with the middle one at the
origin: they preserve the existing spawn straight. Keep the loop inside the
terrain rectangle (**x=-140…360, z=-260…260**) with ample edge clearance.
Avoid crossing the curve over itself, sharply moving adjacent points, or adding
steep height changes. Retest collision and handling after geometry changes.
The builder is a small prototype authoring tool, not a general road editor.

For individual scenery changes, expand **PDT Scenic Road**. Named groups contain
road meshes, rolling meadow, tree groves, rock meshes, guardrails, signage, and
arrival benches. Most repeated scenery is combined into a few meshes; regenerate
its distribution using the seed/count controls. Individual tree/rock collision
objects are under **Scenery colliders** and must stay aligned with those meshes.

**PDT → Environment → Install In Open DrivingTestV2** is only needed to reinstall
the prefab if you remove it. It disables the old Ground and adds the environment;
it does not alter the vehicle, camera, spawn point, or wallet components. Save
the scene after using this menu.

## Files and validation

Completion verification on **2026-10-01**: Unity 6000.3.10f1 compiled the current
project copy and loaded the installed DrivingTestV2 scene successfully, exiting
with status **0**. No scene scripts or baked asset references were missing.
The existing spawn position and single camera were retained. See
`Environment-installed-scene.txt` for the final installation check and
`Environment-results.txt` for the full-loop collision/driving checks.

- `Assets/PDT/Environment/`: baked prefab, meshes, materials, textures, and settings.
- `Assets/PDT/Editor/ScenicRoadBuilder.cs`: editor-only road/environment authoring.
- `Assets/PDT/Scripts/ScenicRoadSettings.cs`: authoring data; no runtime behaviour.
- `Assets/PDT/Scenes/DrivingTestV2.unity`: environment prefab, disabled old Ground,
  and environment ambient/fog settings.
- `Validation/Environment/`: metrics, collision/driving results, rendered previews,
  and a validation harness kept outside the game's Assets directory.

The harness is run in an isolated project copy. It probes the actual baked road
at **2,640 positions**, drives the unchanged controller around the entire loop,
checks a direct guardrail impact, and verifies reset/fall recovery. The automated
steering exists only in this validation harness, not in the game. See the saved
results for the measured outcomes. Preview cameras and the preview car also exist
only in validation and are never saved into the gameplay scene.

To repeat in a disposable project copy, place `EnvironmentValidation.cs` under
its `Assets/Editor` directory and use Unity batch mode with
`-executeMethod EnvironmentValidation.RunBatch` (without `-quit`). It enters
Play mode, tests, writes `Logs/Environment-results.txt`, and exits with status 0
on success. To render images, use `EnvironmentValidation.RenderBatch` with
`-quit` and graphics enabled. Do not run the harness in your working scene.
