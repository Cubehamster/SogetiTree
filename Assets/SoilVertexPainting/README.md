# Soil vertex painting — Unity 6 / URP

## Soil data
R = roughness (0 smooth, 1 rough). G = nutrients (0 depleted, 1 rich).
B = water (0 dry, 1 saturated). A is reserved, initialized to 1.
Channels are independent: they do NOT sum to 1. All values are normalized.
R controls shading roughness, not geometry or physical ground unevenness.
Water darkens soil and increases its smoothness; nutrients subtly tint soil.
Material View switches between soil shading, RGB data, and individual grayscale channels.

## Install
Copy the included Assets/SoilVertexPainting folder into your Unity project.
Run Tools > Tree Planting > Create Soil Material to create Soil.mat with the shader assigned.
Assign Soil.mat to a subdivided mesh (a Unity Plane is enough for an initial test).
Add SoilSurface on the same object as the MeshFilter, MeshRenderer and MeshCollider.
Use a non-convex MeshCollider, and enable Read/Write for imported meshes.
Set Initial Values before Play. Existing mesh colors are ignored unless Preserve Existing Colors is enabled.

Add SoilPainter to a hand tool or empty object. Assign Brush Source: its local +Z is the cast direction.
Choose Raycast or Spherecast. Set Layers to include soil and any objects that should block the brush.
Sphere Cast Radius controls collision detection; Brush Radius controls the painted area.
The first collider hit blocks painting, even if it is not soil. Triggers are ignored.
Spherecasts starting inside a collider may miss; position the source clear of the ground.
Enable Painting in Play mode to test. Use gizmos to see the ray and brush.

## VR interaction
Connect your existing Meta pinch/grab event to BeginPainting() / EndPainting().
No Meta SDK dependency is required. The script does not create a gesture recognizer.
Do not use a raw tracked hand orientation unless its +Z aims as intended; use a child tool transform.
Only enable painting while the intended gesture/tool action is active.

Watering: Channel=Water, Mode=Add, Value=0.4, Strength=1.
Fertilizing: Channel=Nutrients, Mode=Add, Value=0.2.
Smoothing: Channel=Roughness, Mode=Add, Value=-0.3.
Target value: Mode=Set, Value=desired 0..1; Strength controls approach speed.
Add uses value * strength * deltaSeconds * radial falloff. Set approaches target over time.
Each channel is clamped to 0..1. Paint changes only the selected channel.

## Read values for planting
Use a Physics.Raycast hit on the SoilSurface MeshCollider (not a SphereCast hit):

```csharp
if (Physics.Raycast(origin, direction, out RaycastHit hit, 5f)) {
    var soil = hit.collider.GetComponent<TreePlanting.SoilSurface>();
    if (soil != null && soil.TrySample(hit, out Color state)) {
        float roughness = state.r;
        float nutrients = state.g;
        float water = state.b;
        // Feed into your growth/planting rules.
    }
}
```

TrySample interpolates the triangle's three vertex values, matching GPU color interpolation.
Keep the collider on the same object; geometry/topology changes after Awake are not supported.
SoilSurface clones the mesh per object at runtime and restores original references on destruction.
There is no automatic evaporation, nutrient depletion, or persistence. Runtime colors reset when Play ends.

## Limits and verification
Vertex spacing controls painting detail. A brush smaller than spacing can hit a triangle but change no vertices.
Use denser meshes or a larger brush. The brush is a world-space sphere and can affect nearby vertices on
both sides of thin meshes; it is not a geodesic or occlusion-aware brush.
Painting scans all vertices and uploads colors per changed mesh per frame. Start with small terrain chunks,
and profile on Quest before using large meshes. Unity Terrain and SkinnedMeshRenderer are not supported.
The lightweight shader supports main-light PBR shading, ambient probes, receiving/casting shadows and
XR stereo macros; it does not implement additional lights, lightmaps, normal maps, fog or a DepthNormals pass.
Inherited Lit shadow/depth passes can prevent full SRP Batcher compatibility; profile before shipping.

Unity is not installed in the generation environment: shader compilation and headset execution are unverified.
In Unity: check Console after import, test both cast modes, paint each channel in RGB view, verify untouched
channels stay unchanged, sample a painted hit, and inspect both eyes on Quest.

## Scene view painting (starting conditions)
Open Tools > Tree Planting > Soil Scene Painter outside Play mode. Choose a SoilSurface scene
object, then Create Editable Mesh Copy and save a new .asset. The tool assigns that mesh to the
MeshFilter and MeshCollider and enables Preserve Existing Colors automatically. Imported meshes
are not modified. After reopening the window or changing target, create another copy to authorize
editing; previous assets remain saved.

Enable Scene Brush, choose channel, mode, value, radius and strength. Left-drag over the mesh.
Shift subtracts in Add mode or paints toward zero in Set mode. Alt allows Scene view orbiting.
Undo/Redo works per stroke; Fill Mesh is also undoable. Save Mesh Asset and Scene to persist.
The window only edits its selected collider, even when other objects cover it. Interpolated dabs
may bridge small gaps; the brush remains a spherical world-space brush, not a surface-aware one.
Existing shared users of your new mesh asset see the same colors; create another copy for independent patches.

Change the soil material View to RGB Data or a grayscale channel to inspect starting conditions.
Switch View back to Soil before playing. Editor brush strength is per spatially spaced dab, unlike
runtime strength per second; holding still does not continuously apply paint. Asset creation itself
is not undone; undoing assignment leaves the new mesh asset in the project. Save after Undo/Redo
if those changes should be persisted. Avoid editing topology while the brush window is active.
The Scene tool requires a readable mesh and a matching enabled non-convex MeshCollider.

Manual validation: create a copy, paint water, verify R/G unchanged, undo/redo a complete stroke,
save/reopen the scene and enter Play; the runtime should preserve the saved colors and its runtime
clone should not modify the asset. Unity Editor execution has not been available for verification.
