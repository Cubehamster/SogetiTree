using UnityEditor;
using UnityEngine;
namespace TreePlanting.Editor {
public static class SoilMaterialSetup {
    [MenuItem("Tools/Tree Planting/Create Soil Material")]
    public static void CreateMaterial() {
        const string path = "Assets/SoilVertexPainting/Soil.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) { Selection.activeObject = existing; return; }
        Shader shader = Shader.Find("TreePlanting/Soil Vertex Data");
        if (shader == null) { Debug.LogError("Import SoilVertex.shader first."); return; }
        Material material = new Material(shader) { name = "Soil", enableInstancing = true };
        AssetDatabase.CreateAsset(material, path); AssetDatabase.SaveAssets();
        Selection.activeObject = material;
    }
}
}
