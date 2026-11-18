using System;
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

        // Reference layers are available only in explicitly opted-in local
        // builds. Cache each identity independently and fall back as a pair.
        private sealed class ReferenceLayerPair { public Sprite body, head; public float neckY; }
        private static readonly Dictionary<int, ReferenceLayerPair> ReferenceLayers = new Dictionary<int, ReferenceLayerPair>();
        private static readonly HashSet<int> ReferenceLayersAttempted = new HashSet<int>();
        public static bool TryReferenceLayers(int index, out Sprite body, out Sprite head, out float neckY)
        {
            body = head = null; neckY = 0;
            if (index < 0 || index >= SkinCatalog.All.Length) return false;
            string id = SkinCatalog.Get(index).Id, name;
            if (id == "reference-wenda") { name = "wenda"; neckY = -.32f; }
            else if (id == "reference-raddy") { name = "raddy"; neckY = -.17f; }
            else return false;
            if (!ReferenceLayers.TryGetValue(index, out var layers) || layers.body == null || layers.head == null)
            {
                if (!ReferenceLayersAttempted.Add(index)) return false;
                string path = "ReferenceSkins/" + name + "_layers/";
                Texture2D b = Resources.Load<Texture2D>(path + "body");
                Texture2D h = Resources.Load<Texture2D>(path + "head");
#if UNITY_EDITOR
                if (b == null) b = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/" + path + "body.png");
                if (h == null) h = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Editor/" + path + "head.png");
#endif
                // Head and body can use different import resolutions while
                // keeping the same full-canvas aspect ratio and sprite height.
                if (b == null || h == null || b.height <= 0 || h.height <= 0 ||
                    Math.Abs((double)b.width / b.height - (double)h.width / h.height) > .003) return false;
                layers = new ReferenceLayerPair { neckY = neckY,
                    body = UnityEngine.Sprite.Create(b, new Rect(0, 0, b.width, b.height), new Vector2(.5f, .5f), b.height / 4f, 0, SpriteMeshType.FullRect),
                    head = UnityEngine.Sprite.Create(h, new Rect(0, 0, h.width, h.height), new Vector2(.5f, .5f), h.height / 4f, 0, SpriteMeshType.FullRect) };
                layers.body.name = name + " reference body"; layers.head.name = name + " reference head";
                ReferenceLayers[index] = layers;
            }
            body = layers.body; head = layers.head; neckY = layers.neckY; return true;
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
