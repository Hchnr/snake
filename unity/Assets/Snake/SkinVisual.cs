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
