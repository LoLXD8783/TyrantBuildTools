using Terraria.ModLoader;
using Terraria.ModLoader.Config;

namespace TyrantBuildTools.Config
{
    class ReloadNRejoinConfig : ModConfig
    {
        internal static ReloadNRejoinConfig Instance => ModContent.GetInstance<ReloadNRejoinConfig>();
        public override ConfigScope Mode => ConfigScope.ClientSide;

        public bool EnableReloadNRejoin { get; set; }
        public bool ReloadNRejoinExitNoSaveByDefault { get; set; }

        public string TargetMod { get; set; }
    }

}
