using System.ComponentModel;
using Terraria.ModLoader;
using Terraria.ModLoader.Config;

#nullable enable

namespace TyrantBuildTools.Config
{
    public class PackagingChangesConfig : ModConfig
    {
        internal static PackagingChangesConfig Instance => ModContent.GetInstance<PackagingChangesConfig>();

        public override ConfigScope Mode => ConfigScope.ClientSide;

        [DefaultValue(true)]
        public bool SkipImageCompression { get; set; }

        [DefaultValue(true)]
        public bool FastCompression { get; set; }


        [DefaultValue(false)]
        public bool AsyncPackaging { get; set; }
    }
}
