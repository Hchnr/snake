using System.Collections.Generic;
using SnakeClub.Core;
using UnityEngine;

namespace SnakeClub
{
    // Original characters are player resources. Unlicensed visual references
    // are Editor-only unless explicitly staged for an internal review build.
    public static class SkinVisual
    {
        private static readonly Dictionary<int, Texture2D> Textures = new Dictionary<int, Texture2D>();
        private static readonly Dictionary<int, Sprite> Sprites = new Dictionary<int, Sprite>();

        public static bool ReferenceAvailable => Texture(SkinCatalog.OwnedSkinCount) != null;

        public static Texture2D Texture(int index)
        {
            if (index < 0 || index >= SkinCatalog.All.Length) return null;
            if (Textures.TryGetValue(index, out var texture)) return texture;
            string id = SkinCatalog.Get(index).Id;
            bool reference = SkinCatalog.Get(index).ReferencePreview;
            string name = reference ? id.Substring("reference-".Length) : id;
            texture = Resources.Load<Texture2D>((reference ? "ReferenceSkins/" : "Characters/") + name);
#if UNITY_EDITOR
            if (reference && texture == null)
                texture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/ReferenceSkins/" + name + ".png");
#endif
            if (texture != null) Textures[index] = texture;
            return texture;
        }

        // These optional layers are only staged in explicitly opted-in local
        // reference builds. Both are required; otherwise use the complete sprite.
        private static Sprite referenceHead, referenceBody;
        private static bool referenceLayersAttempted;
        public static bool TryWendaLayers(int index, out Sprite body, out Sprite head)
        {
            body = head = null;
            if (index < 0 || index >= SkinCatalog.All.Length || SkinCatalog.Get(index).Id != "reference-wenda") return false;
            if (referenceBody == null || referenceHead == null)
            {
                // The resource set is fixed for a player build; avoid repeating
                // failed lookups every frame when the optional pair is absent.
                if (referenceLayersAttempted) return false;
                referenceLayersAttempted = true;
                Texture2D b = Resources.Load<Texture2D>("ReferenceSkins/wenda_layers/body");
                Texture2D h = Resources.Load<Texture2D>("ReferenceSkins/wenda_layers/head");
#if UNITY_EDITOR
                if (b == null) b = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/ReferenceSkins/wenda_layers/body.png");
                if (h == null) h = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/ReferenceSkins/wenda_layers/head.png");
#endif
                if (b == null || h == null || b.width != h.width || b.height != h.height) return false;
                referenceBody = UnityEngine.Sprite.Create(b, new Rect(0, 0, b.width, b.height), new Vector2(.5f, .5f), b.height / 4f, 0, SpriteMeshType.FullRect);
                referenceHead = UnityEngine.Sprite.Create(h, new Rect(0, 0, h.width, h.height), new Vector2(.5f, .5f), h.height / 4f, 0, SpriteMeshType.FullRect);
                referenceBody.name = "Wenda reference body"; referenceHead.name = "Wenda reference head";
            }
            body = referenceBody; head = referenceHead; return true;
        }

        public static int ResolveIndex(int index) => SkinCatalog.ResolveVisual(index, i => Texture(i) != null);
        public static Sprite DisplaySprite(int index)
        {
            int resolved = ResolveIndex(index);
            return resolved < 0 ? null : Sprite(resolved);
        }

        public static Sprite Sprite(int index)
        {
            if (Sprites.TryGetValue(index, out var sprite) && sprite != null) return sprite;
            var texture = Texture(index);
            if (texture == null) return null;
            sprite = UnityEngine.Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(.5f, .5f), texture.height / 4f, 0, SpriteMeshType.FullRect);
            sprite.name = SkinCatalog.Get(index).Id + " visual reference";
            Sprites[index] = sprite;
            return sprite;
        }
    }
}
