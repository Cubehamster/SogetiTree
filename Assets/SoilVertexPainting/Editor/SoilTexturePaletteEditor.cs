using UnityEditor;
using UnityEngine;
namespace TreePlanting.Editor {
[CustomEditor(typeof(SoilTexturePalette))]
public sealed class SoilTexturePaletteEditor : UnityEditor.Editor {
    SoilTexturePalette Palette => (SoilTexturePalette)target;
    public override void OnInspectorGUI() {
        serializedObject.Update();
        SerializedProperty textures = serializedObject.FindProperty("textures");
        if (textures.arraySize != SoilTexturePalette.TextureCount) textures.arraySize = SoilTexturePalette.TextureCount;
        EditorGUILayout.HelpBox("Each slot is an RGB anchor. Supply 27 tileable sRGB albedo textures of the same dimensions, with Read/Write enabled. The builder packs them into an RGBA32 texture array with mipmaps. It does not modify source import settings.", MessageType.Info);
        for (int i=0; i<SoilTexturePalette.TextureCount; i++) {
            int r=i/9, g=(i/3)%3, b=i%3;
            if (i%9 == 0) {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Roughness R = " + (r*.5f).ToString("0.0"), EditorStyles.boldLabel);
            }
            string label = SoilTexturePalette.TerrainNames[i];
            string tooltip = "RGB = ("+(r*.5f)+", "+(g*.5f)+", "+(b*.5f)+"); slice "+i;
            EditorGUILayout.PropertyField(textures.GetArrayElementAtIndex(i), new GUIContent(label, tooltip));
        }
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("soilMaterial"));
        using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(serializedObject.FindProperty("bakedArray"));
        serializedObject.ApplyModifiedProperties();
        if (Palette.textures[0] != null) {
            double bytes = 27d * Palette.textures[0].width * Palette.textures[0].height * 4 * 4 / 3;
            EditorGUILayout.LabelField("Approximate GPU memory", (bytes/(1024*1024)).ToString("0.0")+" MiB (uncompressed)");
        }
        if (GUILayout.Button("Build / Rebuild 27-Texture Array…")) Build();
        using (new EditorGUI.DisabledScope(Palette.bakedArray == null || Palette.soilMaterial == null))
            if (GUILayout.Button("Assign Array to Soil Material")) AssignMaterial();
        EditorGUILayout.HelpBox("Smooth Blend uses 8 array samples per pixel. Nearest Soil Type uses 1, with abrupt transitions. Profile the shader and texture memory on Quest. This palette contains texture slots, not image assets.", MessageType.Info);
    }
    bool Validate(out string error) {
        error = null;
        if (!SystemInfo.supports2DArrayTextures) { error="This graphics device does not support texture arrays."; return false; }
        if (Palette.textures == null || Palette.textures.Length != 27) { error="Exactly 27 texture slots are required."; return false; }
        int width=0, height=0;
        for (int i=0; i<27; i++) {
            Texture2D t=Palette.textures[i]; string name=SoilTexturePalette.TerrainNames[i];
            if (t == null) { error="Assign the texture for "+name+". You may reuse a texture in multiple slots."; return false; }
            if (!t.isReadable) { error=name+": enable Read/Write in the texture import settings."; return false; }
            if (i==0) { width=t.width; height=t.height; }
            if (t.width != width || t.height != height) { error=name+": all imported textures must have the same width and height."; return false; }
            TextureImporter importer=AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
            if (importer != null && (importer.textureType != TextureImporterType.Default || !importer.sRGBTexture || importer.crunchedCompression)) {
                error=name+": use Texture Type Default, sRGB enabled, and Crunch Compression disabled. For pixel-read errors choose Compression None."; return false;
            }
        }
        return true;
    }
    void Build() {
        if (!Validate(out string error)) { EditorUtility.DisplayDialog("Soil palette",error,"OK"); return; }
        string path=Palette.bakedArray != null ? AssetDatabase.GetAssetPath(Palette.bakedArray) : "";
        if (string.IsNullOrEmpty(path)) {
            path=EditorUtility.SaveFilePanelInProject("Save soil texture array",Palette.name+"_Textures","asset","Choose a new texture array asset path.");
            if (string.IsNullOrEmpty(path)) return;
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) {
                EditorUtility.DisplayDialog("Choose a new path","An asset already exists at this path.","OK"); return;
            }
        } else if (!path.EndsWith(".asset") || AssetDatabase.IsSubAsset(Palette.bakedArray)) {
            EditorUtility.DisplayDialog("Soil palette","The existing array must be a standalone .asset. Clear its reference in Debug Inspector to create a new array.","OK"); return;
        }
        Texture2DArray array=null;
        try {
            Texture2D first=Palette.textures[0];
            array=new Texture2DArray(first.width,first.height,27,TextureFormat.RGBA32,true,false) {
                name=Palette.name+"_Textures", wrapMode=TextureWrapMode.Repeat,
                filterMode=FilterMode.Trilinear, anisoLevel=2
            };
            for (int i=0; i<27; i++) {
                EditorUtility.DisplayProgressBar("Building soil textures",SoilTexturePalette.TerrainNames[i],i/27f);
                array.SetPixels32(Palette.textures[i].GetPixels32(0),i,0);
            }
            array.Apply(true,true);
            if (Palette.bakedArray != null) {
                Undo.RegisterCompleteObjectUndo(Palette.bakedArray,"Rebuild soil texture array");
                EditorUtility.CopySerialized(array,Palette.bakedArray);
                EditorUtility.SetDirty(Palette.bakedArray);
                DestroyImmediate(array); array=null;
            } else {
                AssetDatabase.CreateAsset(array,path);
                Undo.RecordObject(Palette,"Assign soil texture array");
                Palette.bakedArray=array; array=null;
            }
            EditorUtility.SetDirty(Palette); AssetDatabase.SaveAssets();
            if (Palette.soilMaterial != null) AssignMaterial();
            Debug.Log("Built soil texture array: "+path,Palette);
        } catch (System.Exception ex) {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Array build failed","Check the Console. Ensure textures are readable, non-Crunch, and use a GetPixels32-compatible format (Compression None is a safe starting point).","OK");
        } finally {
            EditorUtility.ClearProgressBar();
            if (array != null) DestroyImmediate(array);
        }
    }
    void AssignMaterial() {
        Material m=Palette.soilMaterial;
        if (m == null || Palette.bakedArray == null) return;
        if (m.shader == null || m.shader.name != "TreePlanting/Soil Vertex Data" || !m.HasProperty("_TerrainTextures")) {
            EditorUtility.DisplayDialog("Soil material","Assign a material using the updated TreePlanting/Soil Vertex Data shader.","OK"); return;
        }
        Undo.RecordObject(m,"Assign soil texture palette");
        m.SetTexture("_TerrainTextures",Palette.bakedArray);
        m.SetFloat("_UseTerrainTextures",1);
        EditorUtility.SetDirty(m); AssetDatabase.SaveAssets();
    }
}
}
