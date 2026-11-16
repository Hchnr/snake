using System;
using UnityEditor;
using UnityEngine;

namespace SnakeClub.Editor
{
    // Preserve the character's proportions and transparent edges. Unity's
    // default NPOT resize can otherwise visibly distort a portrait sprite.
    public sealed class CharacterTextureImport : AssetPostprocessor
    {
        public override uint GetVersion() => 1;

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Resources/Characters/", StringComparison.OrdinalIgnoreCase) &&
                !assetPath.StartsWith("Assets/Editor/ReferenceSkins/", StringComparison.OrdinalIgnoreCase) &&
                !assetPath.StartsWith("Assets/Resources/ReferenceSkins/", StringComparison.OrdinalIgnoreCase)) return;
            var importer = (TextureImporter)assetImporter;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 1024;
        }
    }
}
