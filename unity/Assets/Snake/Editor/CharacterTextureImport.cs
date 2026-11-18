using System;
using UnityEditor;
using UnityEngine;

namespace SnakeClub.Editor
{
    // Preserve the character's proportions and transparent edges. Unity's
    // default NPOT resize can otherwise visibly distort a portrait sprite.
    public sealed class CharacterTextureImport : AssetPostprocessor
    {
        public override uint GetVersion() => 4;

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
            bool referenceLayer = (assetPath.StartsWith("Assets/Editor/ReferenceSkins/", StringComparison.OrdinalIgnoreCase) ||
                assetPath.StartsWith("Assets/Resources/ReferenceSkins/", StringComparison.OrdinalIgnoreCase)) &&
                (assetPath.IndexOf("/wenda_layers/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 assetPath.IndexOf("/raddy_layers/", StringComparison.OrdinalIgnoreCase) >= 0);
            bool raddyBody = referenceLayer && assetPath.EndsWith("/raddy_layers/body.png", StringComparison.OrdinalIgnoreCase);
            importer.maxTextureSize = raddyBody ? 512 : 1024;
        }
    }
}
