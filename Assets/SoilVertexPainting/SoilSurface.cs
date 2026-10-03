using UnityEngine;
namespace TreePlanting {
public enum SoilChannel { Roughness, Nutrients, Water }
public enum SoilPaintMode { Add, Set }
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
public sealed class SoilSurface : MonoBehaviour {
    [Tooltip("R=roughness, G=nutrients, B=water; each in [0,1].")]
    public Color initialValues = new Color(.7f, .3f, .1f, 1);
    public bool preserveExistingColors = false;
    Mesh mesh, originalMesh, originalColliderMesh;
    MeshFilter filter; MeshCollider meshCollider;
    Vector3[] vertices; int[] triangles; Color[] values;
    void Awake() {
        filter = GetComponent<MeshFilter>(); meshCollider = GetComponent<MeshCollider>();
        originalMesh = filter.sharedMesh; originalColliderMesh = meshCollider.sharedMesh;
        if (originalMesh == null || !originalMesh.isReadable || meshCollider.convex) {
            Debug.LogError("SoilSurface requires a readable mesh and a non-convex MeshCollider.", this);
            enabled = false; return;
        }
        mesh = Instantiate(originalMesh); mesh.name = originalMesh.name + " (Runtime Soil)";
        mesh.MarkDynamic(); vertices = mesh.vertices; triangles = mesh.triangles;
        values = preserveExistingColors ? mesh.colors : null;
        if (values == null || values.Length != vertices.Length) {
            values = new Color[vertices.Length];
            for (int i=0; i<values.Length; i++) values[i] = Clamp(initialValues);
        } else for (int i=0; i<values.Length; i++) values[i] = Clamp(values[i]);
        mesh.colors = values; filter.sharedMesh = mesh; meshCollider.sharedMesh = mesh;
    }
    static Color Clamp(Color c) => new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1);
    // World-space spherical brush. Returns number of changed vertices.
    public int Paint(Vector3 center, float radius, SoilChannel channel, float value,
                     float amount, SoilPaintMode mode = SoilPaintMode.Add, float falloffPower = 1) {
        if (!enabled || mesh == null || radius <= 0 || amount <= 0) return 0;
        int changed = 0, index = (int)channel;
        for (int i=0; i<vertices.Length; i++) {
            float distance = Vector3.Distance(transform.TransformPoint(vertices[i]), center);
            if (distance >= radius) continue;
            float weight = Mathf.Pow(1 - distance/radius, Mathf.Max(.01f, falloffPower));
            Color c = values[i]; float old = c[index];
            c[index] = mode == SoilPaintMode.Add
                ? Mathf.Clamp01(old + value * amount * weight)
                : Mathf.Lerp(old, Mathf.Clamp01(value), Mathf.Clamp01(amount * weight));
            if (c[index] == old) continue;
            values[i] = c; changed++;
        }
        if (changed > 0) mesh.colors = values;
        return changed;
    }
    // Use a RAYCAST hit on this object's non-convex MeshCollider, not a spherecast hit.
    public bool TrySample(RaycastHit hit, out Color soil) {
        soil = default;
        if (mesh == null || hit.collider != meshCollider || hit.triangleIndex < 0) return false;
        int t = hit.triangleIndex * 3;
        if (t + 2 >= triangles.Length) return false;
        Vector3 b = hit.barycentricCoordinate;
        soil = values[triangles[t]]*b.x + values[triangles[t+1]]*b.y + values[triangles[t+2]]*b.z;
        return true;
    }
    void OnDestroy() {
        if (mesh == null) return;
        if (filter != null && filter.sharedMesh == mesh) filter.sharedMesh = originalMesh;
        if (meshCollider != null && meshCollider.sharedMesh == mesh) meshCollider.sharedMesh = originalColliderMesh;
        Destroy(mesh);
    }
}
}
