using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TreePlanting.Editor {
public sealed class SoilScenePainter : EditorWindow {
    [SerializeField] SoilManager target;
    [SerializeField] Mesh editableMesh;
    [SerializeField] bool brushEnabled;
    [SerializeField] SoilPainter.Channel channel = SoilPainter.Channel.Water;
    [SerializeField] SoilPainter.PaintMode mode = SoilPainter.PaintMode.Set;
    [SerializeField] float radius = .5f, value = .5f, strength = .2f, falloff = 1;
    [SerializeField] Color startingValues = new Color(.7f,.3f,.1f,1);
    GameObject rayObject;
    MeshCollider rayCollider;
    bool stroke;
    int strokeGroup, capturedControl;
    Vector3 lastPoint;
    bool hasLastPoint;
    Vector3[] positions;
    Color[] colors;

    [MenuItem("Tools/Tree Planting/Soil Scene Painter")]
    static void Open() => GetWindow<SoilScenePainter>("Soil Painter");
    void OnEnable() {
        SceneView.duringSceneGui += OnSceneGUI;
        Undo.undoRedoPerformed += OnUndoRedo;
        EditorApplication.playModeStateChanged += OnPlayMode;
    }
    void OnDisable() {
        FinishStroke();
        DestroyRayCollider();
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.playModeStateChanged -= OnPlayMode;
    }
    void OnLostFocus() { FinishStroke(); }
    void OnPlayMode(PlayModeStateChange state) { FinishStroke(); DestroyRayCollider(); brushEnabled = false; Repaint(); }
    void OnUndoRedo() { positions = null; colors = null; SceneView.RepaintAll(); Repaint(); }
    MeshFilter Filter => target != null ? target.GetComponent<MeshFilter>() : null;
    MeshCollider Collider => target != null ? target.GetComponent<MeshCollider>() : null;
    string Problem() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Painting is available only outside Play mode.";
        if (target == null) return "Choose a scene object with SoilManager.";
        if (EditorUtility.IsPersistent(target) || !target.gameObject.scene.IsValid()) return "Choose a scene instance, not a prefab asset.";
        if (Filter == null || Filter.sharedMesh == null) return "The target needs a MeshFilter with a mesh.";
        if (!Filter.sharedMesh.isReadable) return "Enable Read/Write in the mesh import settings.";
        if (Collider == null || Collider.convex)
            return "The target needs a non-convex MeshCollider on the same object. Inactive placement soil is supported.";
        return null;
    }
    bool Ready => Problem() == null && editableMesh != null && Filter.sharedMesh == editableMesh
        && Collider.sharedMesh == editableMesh && AssetDatabase.GetAssetPath(editableMesh).EndsWith(".asset")
        && !AssetDatabase.IsSubAsset(editableMesh);

    void OnGUI() {
        EditorGUI.BeginChangeCheck();
        SoilManager chosen = (SoilManager)EditorGUILayout.ObjectField("Soil target", target, typeof(SoilManager), true);
        if (EditorGUI.EndChangeCheck()) {
            FinishStroke(); DestroyRayCollider(); target = chosen; editableMesh = null; brushEnabled = false;
        }
        if (GUILayout.Button("Use Selected SoilManager")) {
            FinishStroke(); DestroyRayCollider(); target = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<SoilManager>() : null;
            editableMesh = null; brushEnabled = false;
        }
        string problem = Problem();
        if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Info);
        using (new EditorGUI.DisabledScope(problem != null)) {
            if (GUILayout.Button("Create Editable Mesh Copy…")) CreateCopy();
            if (GUILayout.Button("Use Existing Assigned Mesh Asset")) UseExisting();
        }
        EditorGUILayout.HelpBox("Create a separate mesh asset before painting. It is assigned to this object's renderer and collider; Existing RGB colors are preserved. SoilManager reads the saved asset as its runtime baseline.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Ready)) {
            brushEnabled = EditorGUILayout.Toggle("Enable Scene Brush", brushEnabled);
            channel = (SoilPainter.Channel)EditorGUILayout.EnumPopup("Channel", channel);
            mode = (SoilPainter.PaintMode)EditorGUILayout.EnumPopup("Mode", mode);
            radius = Mathf.Max(.001f, EditorGUILayout.FloatField("Radius (world units)", radius));
            value = EditorGUILayout.Slider(mode == SoilPainter.PaintMode.Set ? "Target value" : "Signed amount", value,
                mode == SoilPainter.PaintMode.Set ? 0 : -1, 1);
            strength = EditorGUILayout.Slider("Strength per dab", strength, .001f, 1);
            falloff = EditorGUILayout.Slider("Falloff power", falloff, .1f, 8);
            EditorGUILayout.Space();
            startingValues = EditorGUILayout.ColorField("Fill all RGB values", startingValues);
            if (GUILayout.Button("Fill Mesh with RGB Values (Undoable)")) Fill();
            if (GUILayout.Button("Save Mesh Asset and Scene")) {
                FinishStroke(); AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(target.gameObject.scene);
            }
        }
        EditorGUILayout.HelpBox("Left-drag: paint. Shift: subtract in Add mode / paint toward 0 in Set mode. Alt: orbit. Ctrl/Cmd+Z: Undo one stroke. Brush strength is per spaced dab, not per second. Set material View to RGB Data or an individual channel to inspect values.", MessageType.Info);
        if (Ready) EditorGUILayout.LabelField("Mesh asset", AssetDatabase.GetAssetPath(editableMesh));
        if (!brushEnabled) FinishStroke();
    }
    void CreateCopy() {
        FinishStroke();
        string path = EditorUtility.SaveFilePanelInProject("Save soil mesh copy", target.name + "_Soil", "asset", "Choose a new mesh asset path.");
        if (string.IsNullOrEmpty(path)) return;
        if (AssetDatabase.LoadMainAssetAtPath(path) != null) {
            EditorUtility.DisplayDialog("Choose a new path", "An asset already exists here. Choose another filename.", "OK"); return;
        }
        Mesh copy = Instantiate(Filter.sharedMesh); copy.name = System.IO.Path.GetFileNameWithoutExtension(path);
        Color[] c = copy.colors;
        if (c.Length != copy.vertexCount) {
            c = new Color[copy.vertexCount];
            for (int i = 0; i < c.Length; i++) c[i] = Clamp(DefaultSoil());
        } else for (int i = 0; i < c.Length; i++) c[i] = Clamp(c[i]);
        copy.colors = c;
        AssetDatabase.CreateAsset(copy, path);
        Undo.RegisterCompleteObjectUndo(new Object[] { Filter, Collider, target }, "Assign soil mesh copy");
        Filter.sharedMesh = copy; Collider.sharedMesh = copy;
        PrefabUtility.RecordPrefabInstancePropertyModifications(Filter);
        PrefabUtility.RecordPrefabInstancePropertyModifications(Collider);
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        EditorUtility.SetDirty(Filter); EditorUtility.SetDirty(Collider); EditorUtility.SetDirty(target);
        EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        editableMesh = copy; positions = null; colors = null;
        AssetDatabase.SaveAssets(); SceneView.RepaintAll();
    }
    Color DefaultSoil()
    {
        SerializedObject serialized = new SerializedObject(target);
        return serialized.FindProperty("defaultSoil").colorValue;
    }
    void UseExisting()
    {
        FinishStroke();
        Mesh mesh = Filter.sharedMesh;
        string path = AssetDatabase.GetAssetPath(mesh);
        if (!path.EndsWith(".asset") || AssetDatabase.IsSubAsset(mesh))
        {
            EditorUtility.DisplayDialog("Soil mesh", "Select a standalone .asset mesh, or create an editable copy first.", "OK");
            return;
        }
        Undo.RecordObject(Collider, "Assign soil baseline collider");
        Collider.sharedMesh = mesh;
        PrefabUtility.RecordPrefabInstancePropertyModifications(Collider);
        EditorUtility.SetDirty(Collider);
        EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        editableMesh = mesh; positions = null; colors = null;
        // Initialize missing colors explicitly with Undo; never replace existing colors.
        if (mesh.colors.Length != mesh.vertexCount)
        {
            Undo.RegisterCompleteObjectUndo(mesh, "Initialize soil colors");
            Color[] initial = new Color[mesh.vertexCount];
            for (int i=0;i<initial.Length;i++) initial[i] = Clamp(DefaultSoil());
            mesh.colors = initial; EditorUtility.SetDirty(mesh);
        }
    }
    void UpdateRayCollider()
    {
        if (rayObject == null)
        {
            rayObject = new GameObject("Soil Editor Ray Surface");
            rayObject.hideFlags = HideFlags.HideAndDontSave;
            rayObject.layer = 2;
            rayCollider = rayObject.AddComponent<MeshCollider>();
        }
        rayObject.transform.SetPositionAndRotation(target.transform.position, target.transform.rotation);
        rayObject.transform.localScale = target.transform.lossyScale;
        if (rayCollider.sharedMesh != editableMesh) rayCollider.sharedMesh = editableMesh;
    }
    void DestroyRayCollider()
    {
        if (rayObject != null) DestroyImmediate(rayObject);
        rayObject = null; rayCollider = null;
    }
    static Color Clamp(Color c) => new Color(Mathf.Clamp01(c.r),Mathf.Clamp01(c.g),Mathf.Clamp01(c.b),1);
    void Cache() {
        if (positions == null || positions.Length != editableMesh.vertexCount) positions = editableMesh.vertices;
        if (colors == null || colors.Length != editableMesh.vertexCount) colors = editableMesh.colors;
    }
    void Fill() {
        FinishStroke(); Cache();
        Undo.RegisterCompleteObjectUndo(editableMesh, "Fill soil vertex colors");
        for (int i=0; i<colors.Length; i++) colors[i] = Clamp(startingValues);
        Commit();
    }
    void Commit() {
        editableMesh.colors = colors; EditorUtility.SetDirty(editableMesh); SceneView.RepaintAll();
    }
    void BeginStroke(int control) {
        Undo.IncrementCurrentGroup(); strokeGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Paint soil " + channel);
        Undo.RegisterCompleteObjectUndo(editableMesh, "Paint soil " + channel);
        positions = null; colors = null; Cache();
        capturedControl = control; GUIUtility.hotControl = control; stroke = true; hasLastPoint = false;
    }
    void FinishStroke() {
        if (!stroke) return;
        Undo.CollapseUndoOperations(strokeGroup);
        if (GUIUtility.hotControl == capturedControl) GUIUtility.hotControl = 0;
        stroke = false; hasLastPoint = false; capturedControl = 0;
    }
    void Dab(Vector3 center, bool reverse) {
        int cIndex = (int)channel;
        float amount = reverse ? -Mathf.Abs(value) : value;
        float desired = reverse ? 0 : Mathf.Clamp01(value);
        for (int i=0; i<positions.Length; i++) {
            float d = Vector3.Distance(target.transform.TransformPoint(positions[i]), center);
            if (d >= radius) continue;
            float w = strength * Mathf.Pow(1-d/radius, falloff);
            Color c = colors[i];
            c[cIndex] = mode == SoilPainter.PaintMode.Set ? Mathf.Lerp(c[cIndex], desired, w)
                : Mathf.Clamp01(c[cIndex] + amount*w);
            colors[i] = c;
        }
    }
    void OnSceneGUI(SceneView view) {
        Event e = Event.current;
        if (stroke && e.rawType == EventType.MouseUp && e.button == 0) {
            FinishStroke(); e.Use(); return;
        }
        if (!brushEnabled || !Ready) { FinishStroke(); return; }
        int id = GUIUtility.GetControlID("SoilScenePainter".GetHashCode(), FocusType.Passive);
        if (!e.alt && e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
        if (e.alt) { FinishStroke(); return; }
        Physics.SyncTransforms();
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        // Paint only this selected collider, even if another object overlaps it.
        UpdateRayCollider();
        Physics.SyncTransforms();
        if (!rayCollider.Raycast(ray, out RaycastHit hit, 10000)) { hasLastPoint = false; return; }
        if (e.type == EventType.Repaint) {
            Handles.color = channel == SoilPainter.Channel.Roughness ? Color.red : channel == SoilPainter.Channel.Nutrients ? Color.green : Color.blue;
            Handles.DrawWireDisc(hit.point, hit.normal, radius);
            Handles.DrawLine(hit.point, hit.point + hit.normal * radius * .2f);
        }
        if (e.type == EventType.MouseDown && e.button == 0 && HandleUtility.nearestControl == id) {
            BeginStroke(id); Dab(hit.point,e.shift); lastPoint=hit.point; hasLastPoint=true;
            Commit(); e.Use();
        } else if (stroke && e.type == EventType.MouseDrag && e.button == 0) {
            float spacing = Mathf.Max(.001f,radius*.15f);
            float distance = hasLastPoint ? Vector3.Distance(lastPoint,hit.point) : 0;
            if (!hasLastPoint || distance >= spacing) {
                // Avoid bridging disconnected surfaces or large cursor jumps.
                if (!hasLastPoint || distance > radius*4) Dab(hit.point,e.shift);
                else {
                    int steps = Mathf.Clamp(Mathf.FloorToInt(distance/spacing),1,128);
                    Vector3 start = lastPoint;
                    for (int i=1; i<=steps; i++) Dab(Vector3.Lerp(start,hit.point,(float)i/steps),e.shift);
                }
                lastPoint=hit.point; hasLastPoint=true; Commit();
            }
            e.Use();
        }
        if (e.type == EventType.MouseMove) view.Repaint();
    }
}
}
