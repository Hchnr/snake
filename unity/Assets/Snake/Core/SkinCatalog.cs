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
            new SkinDefinition("reference-raddy", "Raddy", "角色造型预览", 0, true)
        };

        public static SkinDefinition Get(int index) => All[Math.Max(0, Math.Min(All.Length - 1, index))];
        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return i;
            return -1;
        }
    }
}
