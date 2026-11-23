namespace WheekKeys
{
    internal sealed class PetSkin
    {
        public static readonly PetSkin[] All = {
            new PetSkin("tricolor", "三花", "guinea-pig.png", "paw.png"),
            new PetSkin("californian", "白加州"),
            new PetSkin("gold-white", "黄白英短"),
            new PetSkin("teddy", "美泰"),
            new PetSkin("silver-agouti", "雕灰")
        };

        public readonly string Id;
        public readonly string Name;
        public readonly string BodyResource;
        public readonly string PawResource;

        private PetSkin(string id, string name, string body = null, string paw = null)
        {
            Id = id;
            Name = name;
            BodyResource = body ?? id + "-body.png";
            PawResource = paw ?? id + "-paw.png";
        }

        public static PetSkin FromId(string id)
        {
            foreach (PetSkin skin in All)
                if (skin.Id == id) return skin;
            return All[0];
        }
    }
}
