using UnityEditor;
using UnityEngine;
namespace TreePlanting.Editor {
public static class SoilGeneratedTextureSetup {
    [MenuItem("Tools/Tree Planting/Create Palette From Generated Textures")]
    public static void CreatePalette() {
        const string folder = "Assets/SoilVertexPainting/GeneratedTextures";
        string[] paths = new string[27];
        for (int i=0; i<27; i++) {
            string slug=SoilTexturePalette.TerrainNames[i].ToLowerInvariant().Replace("-","_").Replace(" ","_");
            paths[i] = folder+"/"+i.ToString("00")+"_"+slug+".png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]) == null) {
                Debug.LogError("Missing soil texture: "+paths[i]+". Copy the texture package's Assets folder into your project first."); return;
            }
        }
        try {
            for (int i=0; i<27; i++) {
                EditorUtility.DisplayProgressBar("Configure generated textures",SoilTexturePalette.TerrainNames[i],i/27f);
                TextureImporter importer=AssetImporter.GetAtPath(paths[i]) as TextureImporter;
                if (importer == null) throw new System.InvalidOperationException("Missing texture importer: "+paths[i]);
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.isReadable = true;
                importer.crunchedCompression = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 512;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.SaveAndReimport();
            }
            var palette=ScriptableObject.CreateInstance<SoilTexturePalette>();
            for (int i=0; i<27; i++) palette.textures[i]=AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
            palette.soilMaterial=AssetDatabase.LoadAssetAtPath<Material>("Assets/SoilVertexPainting/Soil.mat");
            string path=AssetDatabase.GenerateUniqueAssetPath("Assets/SoilVertexPainting/GeneratedSoilPalette.asset");
            AssetDatabase.CreateAsset(palette,path); AssetDatabase.SaveAssets();
            Selection.activeObject=palette; EditorGUIUtility.PingObject(palette);
            Debug.Log("Generated soil palette ready. Assign your soil material and click Build / Rebuild 27-Texture Array.",palette);
        } finally { EditorUtility.ClearProgressBar(); }
    }
}
}
