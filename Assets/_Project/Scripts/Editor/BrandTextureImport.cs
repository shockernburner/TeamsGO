using UnityEditor;

namespace ProjectFossil.EditorTools
{
    // The studio and game logos (Art/Brand) are drawn on screen as they are: full size, no mipmaps (they'd blur
    // the edges), clamped, uncompressed so the thin amber rule keeps its colour.
    public class BrandTextureImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/_Project/Art/Brand/")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType         = TextureImporterType.Default;
            t.alphaIsTransparency = true;
            t.mipmapEnabled       = false;
            t.npotScale           = TextureImporterNPOTScale.None;
            t.wrapMode            = UnityEngine.TextureWrapMode.Clamp;
            t.filterMode          = UnityEngine.FilterMode.Bilinear;
            t.textureCompression  = TextureImporterCompression.Uncompressed;
            t.maxTextureSize      = 4096;
        }
    }
}
