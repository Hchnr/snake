#nullable disable
using System;

namespace SnakeClub.Core
{
    // Stable IDs keep existing inventory indexes intact while allowing the
    // visual representation of each skin to evolve independently.
    public sealed class SkinDefinition
    {
        public readonly string Id, Name, Description;
        public readonly int Price;
        public readonly bool ReferencePreview;

        public SkinDefinition(string id, string name, string description, int price, bool referencePreview = false)
        {
            Id = id; Name = name; Description = description;
            Price = price; ReferencePreview = referencePreview;
        }
    }

    public static class SkinCatalog
    {
        public const int OwnedSkinCount = 4;
        public static readonly SkinDefinition[] All = {
            new SkinDefinition("lanlight", "岚光", "转向速度 +1%", 0),
            new SkinDefinition("ember", "曜焰", "加速消耗 -1%", 500),
            new SkinDefinition("stardust", "星砂", "拾取范围 +1%", 900),
            new SkinDefinition("crystal", "萤晶", "探洞金币 +1%", 1500),
            new SkinDefinition("reference-wenda", "Wenda", "角色造型预览", 0, true),
            new SkinDefinition("reference-raddy", "Raddy", "角色造型预览", 0, true),
            new SkinDefinition("reference-oren", "Oren", "角色造型预览", 0, true),
            new SkinDefinition("reference-clukr", "Clukr", "角色造型预览", 0, true),
            new SkinDefinition("reference-fun-bot", "Fun Bot", "角色造型预览", 0, true),
            new SkinDefinition("reference-vineria", "Vineria", "角色造型预览", 0, true),
            new SkinDefinition("reference-gray", "Gray", "角色造型预览", 0, true),
            new SkinDefinition("reference-pinki", "Pinki", "角色造型预览", 0, true),
            new SkinDefinition("reference-brud", "Brud", "角色造型预览", 0, true),
            new SkinDefinition("reference-garnold", "Garnold", "角色造型预览", 0, true),
            new SkinDefinition("reference-owakcx", "OWAKCX", "角色造型预览", 0, true),
            new SkinDefinition("reference-sky", "Sky", "角色造型预览", 0, true),
            new SkinDefinition("reference-mr-sun", "Mr. Sun", "角色造型预览", 0, true),
            new SkinDefinition("reference-durple", "Durple", "角色造型预览", 0, true),
            new SkinDefinition("reference-mr-tree", "Mr. Tree", "角色造型预览", 0, true),
            new SkinDefinition("reference-simon", "Simon", "角色造型预览", 0, true),
            new SkinDefinition("reference-tunner", "Tunner", "角色造型预览", 0, true),
            new SkinDefinition("reference-mr-fun-computer", "Mr. Fun Computer", "角色造型预览", 0, true),
            new SkinDefinition("reference-jevin", "Jevin", "角色造型预览", 0, true),
            new SkinDefinition("reference-black", "Black", "角色造型预览", 0, true)
        };

        public static SkinDefinition Get(int index) => All[Math.Max(0, Math.Min(All.Length - 1, index))];
        // Visual resolution never changes the persistent equipped/owned ID.
        public static int ResolveVisual(int requested, Func<int, bool> available)
        {
            if (available == null) return -1;
            if (requested >= 0 && requested < All.Length && available(requested)) return requested;
            return available(0) ? 0 : -1;
        }
        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return i;
            return -1;
        }
    }
}
