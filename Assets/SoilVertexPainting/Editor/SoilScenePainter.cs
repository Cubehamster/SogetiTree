using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TreePlanting.Editor {
public sealed class SoilScenePainter : EditorWindow {
    [SerializeField] SoilSurface target;
    [SerializeField] Mesh editableMesh;
    [SerializeField] bool brushEnabled;
    [SerializeField] SoilChannel channel = SoilChannel.Water;
    [SerializeField] SoilPaintMode mode = SoilPaintMode.Set;
    [SerializeField] float radius = .5f, value = .5f, strength = .2f, falloff = 1;
    [SerializeField] Color startingValues = new Color(.7f,.3f,.1f,1);
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
        SceneView.duringSceneGui -= OnSceneGUI;
        Undo.undoRedoPerformed -= OnUndoRedo;
        EditorApplication.playModeStateChanged -= OnPlayMode;
    }
    void OnLostFocus() { FinishStroke(); }
    void OnPlayMode(PlayModeStateChange state) { FinishStroke(); brushEnabled = false; Repaint(); }
    void OnUndoRedo() { positions = null; colors = null; SceneView.RepaintAll(); Repaint(); }
    MeshFilter Filter => target != null ? target.GetComponent<MeshFilter>() : null;
    MeshCollider Collider => target != null ? target.GetComponent<MeshCollider>() : null;
    string Problem() {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "Painting is available only outside Play mode.";
        if (target == null) return "Choose a scene object with SoilSurface.";
        if (EditorUtility.IsPersistent(target) || !target.gameObject.scene.IsValid()) return "Choose a scene instance, not a prefab asset.";
        if (Filter == null || Filter.sharedMesh == null) return "The target needs a MeshFilter with a mesh.";
        if (!Filter.sharedMesh.isReadable) return "Enable Read/Write in the mesh import settings.";
        if (Collider == null || Collider.convex || !Collider.enabled || !target.gameObject.activeInHierarchy)
            return "The target needs an enabled, non-convex MeshCollider on the same active object.";
        return null;
    }
    bool Ready => Problem() == null && editableMesh != null && Filter.sharedMesh == editableMesh
        && Collider.sharedMesh == editableMesh && AssetDatabase.GetAssetPath(editableMesh).EndsWith(".asset")
        && !AssetDatabase.IsSubAsset(editableMesh);

    void OnGUI() {
        EditorGUI.BeginChangeCheck();
        SoilSurface chosen = (SoilSurface)EditorGUILayout.ObjectField("Soil target", target, typeof(SoilSurface), true);
        if (EditorGUI.EndChangeCheck()) {
            FinishStroke(); target = chosen; editableMesh = null; brushEnabled = false;
        }
        if (GUILayout.Button("Use Selected SoilSurface")) {
            FinishStroke(); target = Selection.activeGameObject != null
                ? Selection.activeGameObject.GetComponent<SoilSurface>() : null;
            editableMesh = null; brushEnabled = false;
        }
        string problem = Problem();
        if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Info);
        using (new EditorGUI.DisabledScope(problem != null)) {
            if (GUILayout.Button("Create Editable Mesh Copy…")) CreateCopy();
        }
        EditorGUILayout.HelpBox("Create a separate mesh asset before painting. It is assigned to this object's renderer and collider; Preserve Existing Colors is enabled automatically.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!Ready)) {
            brushEnabled = EditorGUILayout.Toggle("Enable Scene Brush", brushEnabled);
            channel = (SoilChannel)EditorGUILayout.EnumPopup("Channel", channel);
            mode = (SoilPaintMode)EditorGUILayout.EnumPopup("Mode", mode);
            radius = Mathf.Max(.001f, EditorGUILayout.FloatField("Radius (world units)", radius));
            value = EditorGUILayout.Slider(mode == SoilPaintMode.Set ? "Target value" : "Signed amount", value,
                mode == SoilPaintMode.Set ? 0 : -1, 1);
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
        if (!target.preserveExistingColors || c.Length != copy.vertexCount) {
            c = new Color[copy.vertexCount];
            for (int i = 0; i < c.Length; i++) c[i] = Clamp(target.initialValues);
        } else for (int i = 0; i < c.Length; i++) c[i] = Clamp(c[i]);
        copy.colors = c;
        AssetDatabase.CreateAsset(copy, path);
        Undo.RegisterCompleteObjectUndo(new Object[] { Filter, Collider, target }, "Assign soil mesh copy");
        Filter.sharedMesh = copy; Collider.sharedMesh = copy; target.preserveExistingColors = true;
        PrefabUtility.RecordPrefabInstancePropertyModifications(Filter);
        PrefabUtility.RecordPrefabInstancePropertyModifications(Collider);
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
        EditorUtility.SetDirty(Filter); EditorUtility.SetDirty(Collider); EditorUtility.SetDirty(target);
        EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        editableMesh = copy; positions = null; colors = null;
        AssetDatabase.SaveAssets(); SceneView.RepaintAll();
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
            c[cIndex] = mode == SoilPaintMode.Set ? Mathf.Lerp(c[cIndex], desired, w)
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
        if (!Collider.Raycast(ray, out RaycastHit hit, 10000)) { hasLastPoint = false; return; }
        if (e.type == EventType.Repaint) {
            Handles.color = channel == SoilChannel.Roughness ? Color.red : channel == SoilChannel.Nutrients ? Color.green : Color.blue;
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
