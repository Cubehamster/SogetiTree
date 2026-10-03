using UnityEngine;
namespace TreePlanting {
[CreateAssetMenu(fileName="SoilTexturePalette", menuName="Tree Planting/27 Soil Texture Palette")]
public sealed class SoilTexturePalette : ScriptableObject {
    public const int TextureCount = 27;
    // Serialized array ordering is R*9 + G*3 + B, levels indexed 0,1,2.
    public Texture2D[] textures = new Texture2D[TextureCount];
    public Texture2DArray bakedArray;
    public Material soilMaterial;
    public static readonly string[] TerrainNames = {
        "Desert Sand", "Damp Sand", "Wet Sand",
        "Sandy Soil", "Sandy Grassland", "Sandy Mud",
        "Dry Garden Soil", "Rich Garden Soil", "Rich Mud",
        "Barren Earth", "Damp Barren Earth", "Barren Marsh Soil",
        "Dry Scrub Soil", "Grassy Soil", "Marsh Soil",
        "Volcanic Soil", "Lush Meadow Soil", "Rich Swamp Soil",
        "Bare Rock", "Damp Rock", "Waterlogged Rock",
        "Dusty Rock", "Mossy Rock", "Algae-Covered Rock",
        "Soil-Pocketed Rock", "Overgrown Rock", "Swamp Bedrock"
    };
}
}
