using System.IO;
using UnityEditor;
using UnityEngine;

namespace AshenSol.EditorTools
{
    /// <summary>Consistent import settings for generated sprites and audio.</summary>
    public class SpriteImportPostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.Contains("/_Game/Resources/Sprites/")) return;
            var imp = (TextureImporter)assetImporter;
            var name = Path.GetFileNameWithoutExtension(path);
            bool tileable = name.StartsWith("tile_") || name.StartsWith("bg_") || name.StartsWith("prop_wall") || name.StartsWith("prop_lattice") || name.StartsWith("prop_chain");

            imp.textureType = TextureImporterType.Sprite;
            imp.spriteImportMode = SpriteImportMode.Single;
            imp.spritePixelsPerUnit = 100;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;
            imp.mipmapEnabled = false;
            imp.alphaIsTransparency = true;
            imp.maxTextureSize = 2048;
            imp.wrapMode = tileable ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            imp.sRGBTexture = true;

            var settings = new TextureImporterSettings();
            imp.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteGenerateFallbackPhysicsShape = false;
            settings.spriteExtrude = 0;
            imp.SetTextureSettings(settings);
        }
    }

    public class AudioImportPostprocessor : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.Contains("/_Game/Resources/Audio/")) return;
            var imp = (AudioImporter)assetImporter;
            bool music = path.Contains("/Music/");
            var s = imp.defaultSampleSettings;
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? 0.6f : 0.7f;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            imp.defaultSampleSettings = s;
            imp.forceToMono = false;
            imp.loadInBackground = music;
        }
    }
}
