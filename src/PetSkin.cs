namespace WheekKeys
{
    internal sealed class PetSkin
    {
        public static readonly PetSkin[] All = {
            new PetSkin("tricolor", "三花", "guinea-pig.png", "paw.png"),
            new PetSkin("californian", "白加州"),
            new PetSkin("gold-white", "黄白英短"),
            new PetSkin("teddy", "美泰"),
            new PetSkin("silver-agouti", "雕灰"),
            new PetSkin("longhair", "长毛猪"),
            Pixel("pixel-tricolor", "像素三花", "tricolor", "tricolor"),
            Pixel("pixel-black-white", "像素黑白", "black-white", "silver-agouti"),
            Pixel("pixel-golden-white", "像素黄白", "golden-white", "gold-white"),
            Pixel("pixel-buff", "像素浅金棕", "white", "gold-white"),
            Pixel("pixel-cream", "像素奶油", "cream", "californian"),
            Pixel("pixel-chocolate", "像素巧克力", "chocolate", "teddy"),
            Pixel("pixel-californian", "像素加州", "californian", "californian"),
            Pixel("pixel-longhair-gold-white", "像素长毛黄白", "longhair-template", "longhair"),
            Pixel("pixel-longhair-tricolor", "像素长毛三花", "longhair-tricolor", "longhair"),
            Pixel("pixel-longhair-black-white", "像素长毛黑白", "longhair-black-white", "longhair"),
            Pixel("pixel-longhair-buff", "像素长毛浅金棕", "longhair-buff", "longhair"),
            Pixel("pixel-longhair-cream", "像素长毛奶油", "longhair-cream", "longhair"),
            Pixel("pixel-longhair-chocolate", "像素长毛巧克力", "longhair-chocolate", "longhair"),
            Pixel("pixel-longhair-californian", "像素长毛加州", "longhair-californian", "longhair")
        };

        public readonly string Id;
        public readonly string Name;
        public readonly string BodyResource;
        public readonly string PawResource;
        public readonly string PixelCoatId;
        public readonly string NetworkId;

        private PetSkin(string id, string name, string body = null, string paw = null, string pixelCoatId = null, string networkId = null)
        {
            Id = id;
            Name = name;
            BodyResource = body ?? id + "-body.png";
            PawResource = paw ?? id + "-paw.png";
            PixelCoatId = pixelCoatId;
            NetworkId = networkId ?? id;
        }

        private static PetSkin Pixel(string id, string name, string coat, string network)
        {
            CoatArt art = CoatArt.Find(coat);
            return new PetSkin(id, name, "pixel-" + (art.BodyAsset ?? "guinea-pig.png"), "pixel-paw.png", coat, network);
        }

        public static PetSkin FromId(string id)
        {
            foreach (PetSkin skin in All)
                if (skin.Id == id) return skin;
            string replacement = null;
            switch (id)
            {
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
            if (replacement != null)
                foreach (PetSkin skin in All)
                    if (skin.Id == replacement) return skin;
            return All[0];
        }
    }
}
