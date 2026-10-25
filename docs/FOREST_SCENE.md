# ForestDemo: how it is built, what is verified, what is not

## Role in the product

The forest is **one optional relaxation use case of the eye-tracking app**. The core outcome is an experimental health/fatigue wellness report, and users must be able to complete a report-only session without entering `ForestDemo` or selecting scene settings. Only an explicit participation choice should load/apply this experience; receiving a `SceneConfig` is not that choice. The current app still routes normal completion through Scene/Re-rating; see [objectives and implementation gaps](OBJECTIVES.md). This builder does not implement the report-only completion path.

Owner: environment and experience. The scene is produced by an Editor script rather than by hand, so it is reproducible
and reviewable. **Nothing here has been run in Unity yet**: the code was written on a machine without the editor
(Unity 6000.6.7f1 is used by the project). Expect one round of compile/visual fixes on the first run.

## 1. Build it (once, on one machine)

1. Open `unity/` in Unity 6000.6.7f1, wait for the import, confirm there are no compile errors.
2. Menu **App > Forest > Build ForestDemo** (or `-executeMethod App.Relaxation.Editor.ForestSceneBuilder.BuildBatch`).
3. Open `Assets/App/Scenes/ForestDemo.unity`, press Play with `Assessment.unity` loaded, or run the full app flow.
4. Commit the result **including every `.meta`** (scene, `Assets/App/Forest/{Prefabs,Materials,Generated,Audio}`).
   Do not rebuild on several machines: regenerated materials/meshes get new GUIDs and would conflict.

The builder replaces `ForestDemo.unity`; the older **App > Create ForestDemo Placeholder** only creates the scene if it is
missing, so it never overwrites this one (the builder calls it only to register the scene in Build Settings).
Once the real scene is committed, the placeholder builder and `Assets/App/Relaxation/Placeholder/` can be deleted in a
separate cleanup PR.

## 2. Code map (no duplicated responsibilities)

| Piece | Location | Role |
| --- | --- | --- |
| `ISceneApplier` seam | `Core/Scene` (integrator) | The only interface the flow knows |
| `ForestSceneApplier` | `Relaxation` (integrator) | Core `SceneConfig` -> forest DTO, calls the applier |
| `SceneConfigApplier`, forest `SceneConfig`, `SceneConfigMapper` | `Relaxation` (this module) | Applies/clamps config; chip rules; installs the sky |
| `SquirrelIdle` | `Squirrel` | `IAmbientMotion` for the squirrel |
| `ForestSceneBuilder`, `ForestTerrain`, `ForestAudioSynth` | `Relaxation/Editor` (Editor only) | Build the scene, ground, audio |
| `ForestDemoPlaceholderBuilder` | `UI/Editor` (integrator) | Old placeholder; reused only for Build Settings registration |

Assembly references: `App.Relaxation.Editor -> App.Relaxation, App.Squirrel, App.UI.Editor`. Nothing references the Editor
assembly, so there are no cycles. The core and forest `SceneConfig` types are intentionally separate (the forest DTO uses
`JsonUtility` strings); their agreement is guarded by `ForestBridgeTests` and `ForestMapperRulesTests` against
`fixtures/integration/scene_rules.json`. Merging them into one type is a possible later cleanup, deliberately not done
here to avoid breaking the integration branch.

## 3. What the builder produces

- **Ground**: 64 m mesh (112x112 quads, about 25k triangles) with gentle hills from one height function, flat around the
  viewer, a pond basin, and a 2048x2048 painted albedo (grass, moss, soil, a damp ring around the pond). Heights and
  texture fade to a flat 800 m far plane so the ground never ends in an island edge.
- **Layout**: viewer at (0, 0, -10) looking along +Z (the Assessment camera is at (0, 1, -10)). A 5 m clearing around the
  viewer, a kept-open sightline to a pond at (3, 4) framed by two willows, rocks, plants and lily pads. Squirrel on a
  mossy stump 4.8 m away, to the right. About 70 main trees (denser towards the edge, never within 7 m of the viewer),
  48 far-plane trees, 48 bushes, 110 plants, 340 grass tufts, 3 flower meadows, 20 rocks, stumps. Fixed seed 20260101.
- **Models**: 45 curated Quaternius FBX files (CC0), each wrapped in a prefab that normalises height/size, puts the pivot
  on the ground, converts materials to URP/Lit (slightly desaturated, no specular highlights), uses two-sided materials
  for thin foliage, and adds a culling-only `LODGroup` for grass/plants/flowers/small rocks (the pack has no LOD meshes).
- **Squirrel**: `tools/generate_squirrel.py` (original model, about 6.8k triangles, smooth head/tail) with fur/belly/dark
  URP materials and `SquirrelIdle`. The Rifat3D Sketchfab model was **not** imported (download needs an account); swap it
  in under `SquirrelAnchor` if someone with access downloads it.
- **Sky**: the 4096x2048 tone-mapped panorama of the supplied "Day Environment HDRI 113 4K" on `Skybox/Panoramic`,
  imported with ASTC 6x6 for Android (about 5 MB on the GPU with mips, by calculation, not measured). `SceneConfigApplier`
  installs it as the skybox and ambient source at runtime because ForestDemo is loaded additively.
- **Why not the 4K EXR**: the 37 MB `.exr` would be a half-float 4K texture (tens of MB) and ASTC HDR support on Quest Pro
  is unverified. The tone-mapped 4K JPG keeps the resolution without that risk. If an HDR path is wanted later, first
  test it on the headset.
- **Audio**: four seamless loops (wind, birds, water, pad) synthesised by `ForestAudioSynth`; original and license-free,
  intentionally simple. Replace the `.wav` files with real recordings later, same names. Water is spatialised at the pond.
- **Effects**: slow fireflies and pollen (additive soft points, smooth fade in/out, prewarmed, none within 3 m of the
  viewer) and the breathing orb 3.5 m ahead. All toggled and timed by the existing `SceneConfig` fields.
- **Lighting**: one realtime soft-shadow directional light driven by `SceneConfigApplier` (day/sunset, warmth,
  brightness), skybox ambient, no realtime reflection probes, no post-processing. Lightmap baking was **not** set up.

## 4. Config contract

Unchanged: no new fields. `SceneConfigApplier` only gained skybox install/restore alongside its existing fog/ambient
save/restore. Quiet and animal-free choices still override everything (mapper + applier); mute still cuts every source.

## 5. Budgets and what must be measured

The builder logs the real triangle and renderer count after each build; none is recorded here. Design target: a few
hundred thousand triangles before culling, shared materials with GPU instancing, no transparency except the particles and
no realtime reflections. **Not measured**: frame time at the Quest Pro refresh rate, GPU/CPU headroom, texture memory
(sky, ground), draw calls after static batching, shader correctness on device, visual comfort over a 3-minute session,
audio levels and mute on device, squirrel/orb comfort. If frame time is tight, reduce in this order: far-plane trees,
grass count, shadow distance (URP asset), particle counts.

## 6. Known risks to check first

- Quaternius materials may import as non-URP or look flat: the builder reads each source colour and recreates URP/Lit
  materials, but verify in the Scene view.
- The particle material is configured by script; if it renders opaque, set Surface = Transparent, Blend = Additive.
- The squirrel was never rendered by its author; proportions may need tweaks in `tools/generate_squirrel.py`.
- Ground fog colour and sun angles come from `SceneConfigApplier` presets; tune there, not in the scene.
