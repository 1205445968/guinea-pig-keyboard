using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WheekKeys
{
    internal sealed class PetSkin
    {
        public static readonly PetSkin[] All = {
            new PetSkin("pixel-tricolor", "像素三花", "tricolor", "tricolor"),
            new PetSkin("pixel-black-white", "像素黑白", "black-white", "silver-agouti"),
            new PetSkin("pixel-golden-white", "像素黄白", "golden-white", "gold-white"),
            new PetSkin("pixel-buff", "像素浅金棕", "white", "gold-white"),
            new PetSkin("pixel-cream", "像素奶油", "cream", "californian"),
            new PetSkin("pixel-chocolate", "像素巧克力", "chocolate", "teddy"),
            new PetSkin("pixel-californian", "像素加州", "californian", "californian"),
            new PetSkin("pixel-longhair-gold-white", "像素长毛黄白", "longhair-template", "longhair"),
            new PetSkin("pixel-longhair-tricolor", "像素长毛三花", "longhair-tricolor", "longhair"),
            new PetSkin("pixel-longhair-black-white", "像素长毛黑白", "longhair-black-white", "longhair"),
            new PetSkin("pixel-longhair-buff", "像素长毛浅金棕", "longhair-buff", "longhair"),
            new PetSkin("pixel-longhair-cream", "像素长毛奶油", "longhair-cream", "longhair"),
            new PetSkin("pixel-longhair-chocolate", "像素长毛巧克力", "longhair-chocolate", "longhair"),
            new PetSkin("pixel-longhair-californian", "像素长毛加州", "longhair-californian", "longhair"),
            Drawn("tricolor", "手绘三花"),
            Drawn("californian", "手绘白加州"),
            Drawn("gold-white", "手绘黄白英短"),
            Drawn("teddy", "手绘美泰"),
            Drawn("silver-agouti", "手绘雕灰"),
            Drawn("longhair", "手绘长毛猪")
        };

        private static readonly SortedDictionary<string, PetSkin> custom = new SortedDictionary<string, PetSkin>(StringComparer.Ordinal);
        private static volatile IReadOnlyList<PetSkin> available = Array.AsReadOnly(All);
        public static IReadOnlyList<PetSkin> Available { get { return available; } }

        public readonly string Id;
        public readonly string Name;
        public readonly string PixelCoatId;
        public readonly string NetworkId;
        public bool IsDrawn { get { return PixelCoatId == null; } }
        public bool IsCustom { get { return BaseSkin != null; } }
        public PetSkin BaseSkin { get; private set; }
        public BitmapImage CustomBody { get; internal set; }
        public BitmapImage CustomPaw { get; internal set; }
        public BitmapImage CustomEyes { get; internal set; }
        public BitmapImage CustomMouth { get; internal set; }
        public Color? CustomFurColor { get; internal set; }
        public Color? CustomInkColor { get; internal set; }
        public Color? CustomEyeColor { get; internal set; }
        public string Author { get; internal set; }

        private PetSkin(string id, string name, string pixelCoatId, string networkId)
        {
            Id = id;
            Name = name;
            PixelCoatId = pixelCoatId;
            NetworkId = networkId;
        }

        private static PetSkin Drawn(string coatId, string name)
        {
            return new PetSkin("drawn-" + coatId, name, null, coatId);
        }

        internal static PetSkin CreateCustom(string id, string name, bool drawn, string baseCoat)
        {
            PetSkin basis = Find(baseCoat);
            if (basis == null || basis.IsCustom) basis = All[0];
            if (basis.IsDrawn != drawn)
                basis = Find(drawn ? "drawn-" + basis.NetworkId : basis.NetworkId);
            return new PetSkin("wheek-" + id, name, basis.PixelCoatId, basis.NetworkId) { BaseSkin = basis };
        }

        internal static void Register(PetSkin skin)
        {
            lock (custom)
            {
                custom[skin.Id] = skin;
                List<PetSkin> skins = new List<PetSkin>(All);
                skins.AddRange(custom.Values);
                available = skins.AsReadOnly();
            }
        }

        public static PetSkin FromId(string id) { return Find(id) ?? All[0]; }

        public static PetSkin Find(string id)
        {
            string replacement = null;
            switch (id)
            {
                case "tricolor": replacement = "pixel-tricolor"; break;
                case "californian": replacement = "pixel-californian"; break;
                case "gold-white": replacement = "pixel-golden-white"; break;
                case "teddy": replacement = "pixel-chocolate"; break;
                case "silver-agouti": replacement = "pixel-black-white"; break;
                case "longhair": replacement = "pixel-longhair-gold-white"; break;
                case "black-white": replacement = "pixel-black-white"; break;
                case "golden-white": replacement = "pixel-golden-white"; break;
                case "white": replacement = "pixel-buff"; break;
                case "cream": replacement = "pixel-cream"; break;
                case "chocolate": replacement = "pixel-chocolate"; break;
                case "longhair-template": replacement = "pixel-longhair-gold-white"; break;
                case "longhair-tricolor": replacement = "pixel-longhair-tricolor"; break;
                case "longhair-black-white": replacement = "pixel-longhair-black-white"; break;
                case "longhair-buff": replacement = "pixel-longhair-buff"; break;
                case "longhair-cream": replacement = "pixel-longhair-cream"; break;
                case "longhair-chocolate": replacement = "pixel-longhair-chocolate"; break;
                case "longhair-californian": replacement = "pixel-longhair-californian"; break;
            }
            foreach (PetSkin skin in Available)
                if (skin.Id == (replacement ?? id)) return skin;
            return null;
        }
    }
}
