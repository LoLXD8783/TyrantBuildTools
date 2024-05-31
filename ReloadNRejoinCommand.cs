using Microsoft.Xna.Framework;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Terraria;
using Terraria.GameContent.UI.Elements;
using Terraria.GameContent.UI.States;
using Terraria.ID;
using Terraria.IO;
using Terraria.ModLoader;
using Terraria.UI;
using TyrantBuildTools.Config;
using ICommandCaller = Terraria.ModLoader.CommandCaller;

// ANDRE STINKS

namespace TyrantBuildTools
{
    internal class ReloadNRejoinCommand : ModCommand
    {
        public override string Command => "rnr";

        public override string Usage => "/rnr (nosave|save)? "; // (onexit)?

        public override CommandType Type => CommandType.Chat;

        public override void Action(ICommandCaller caller, string input, string[] args)
        {
            if (Main.netMode != NetmodeID.SinglePlayer)
            {
                caller.Reply("This command is only available in singleplayer (and if the config is enabled).");
                return;
            }
            /*if (!DebugConfig.Instance.EnableDebugMode)
            {
                caller.Reply("To use reloadnrejoin debug mode must be enabled");
                return;
            }*/
            if (!ReloadNRejoinConfig.Instance.EnableReloadNRejoin)
            {
                caller.Reply("To use reloadnrejoin you must enable it in the mod config first"); // and reload the mod
                return;
            }
            /*if (!Pe.loaded)
            {
                caller.Reply("The mod config has reloadnrejoin enabled but it requires a mod reload");
                return;
            }*/
            string? targetMod = ReloadNRejoinConfig.Instance.TargetMod;
            if (!Pe.CheckExistingModName(targetMod))
            {
                caller.Reply($"The specified mod in config '{targetMod}' does not exist or could not be found in the ModSources folder, please specify in Reload N Rejoin config the mod internal name in the 'Reload N Rejoin' mod config.");
                return;
            }
            bool save = ReloadNRejoinConfig.Instance.ReloadNRejoinExitNoSaveByDefault;
            if (args is { Length: > 0 })
            {
                if (args[0] is "help" or "/?" or "/help" or "h" or "?")
                {
                    caller.Reply("Usage:  " + Usage);
                }
                if (args[0] == "nosave")
                    save = false;
                else if (args[0] == "save")
                    save = true;
            }
            if (!save)
            {
                Main.gameMenu = true;
                Pe.SetRestart(Main.LocalPlayer.name, Main.ActiveWorldFileData.Name, true);
            }
            else
            {
                string playerName = Main.LocalPlayer.name;
                string worldName = Main.ActiveWorldFileData.Name;
                WorldGen.SaveAndQuit(() =>
                {
                    Pe.SetRestart(playerName, worldName, true);
                });
            }
        }
    }

    class Pe : ModSystem
    {
        private static string CharacterName, WorldName;
        public static void SetRestart(string characterName, string worldName, bool isreload)
        {
            //CharacterName = characterName;
            //WorldName = worldName;
            //enterCharacterSelectMenu = true;
            reloadModSources = isreload;
            reloadModOnFound = isreload;
            Environment.SetEnvironmentVariable("TBT_REJOINPLAYER", characterName);
            Environment.SetEnvironmentVariable("TBT_REJOINWORLD", worldName);
        }
        //internal static bool loaded = false;
        public override void Load()
        {
            //if (ReloadNRejoinConfig.Instance.EnableReloadNRejoin)
            {
                MonoModHooks.Add(typeof(ModContent).Assembly.GetType("Terraria.ModLoader.Core.ModOrganizer")!.GetMethod("SaveLastLaunchedMods", BindingFlags.NonPublic | BindingFlags.Static)!, (Action orig) =>
                {
                    orig();
                    if (Environment.GetEnvironmentVariable("TBT_REJOINACTIVE") is "1")
                    {
                        CharacterName = Environment.GetEnvironmentVariable("TBT_REJOINPLAYER");
                        WorldName = Environment.GetEnvironmentVariable("TBT_REJOINWORLD");
                        enterCharacterSelectMenu = true;
                        Environment.SetEnvironmentVariable("TBT_REJOINACTIVE", null);
                    }
                    //enterCharacterSelectMenu = true;
                });
                MonoModHooks.Modify(typeof(ModContent).Assembly.GetType("Terraria.ModLoader.UI.UIModSources").GetMethod("Populate", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public), IL_UIModSources_Populate_TriggerSomethingAfterModFolderPopulation);
                Terraria.IL_Main.DrawMenu += IL_Main_DrawMenu; ;
                Terraria.On_Main.DrawMenu += Main_DrawMenu;
                //loaded = true;
            }
        }

        // internal static string modsourcesPathOverride;
        static bool reloadModOnFound;

        internal static bool CheckExistingModName(string name)
        {
            string possibleModFolder = Path.Join(Main.SavePath, "ModSources", name);
            return !string.IsNullOrWhiteSpace(name) && Directory.Exists(possibleModFolder);
        }

        static void OnFinishLoadingModSources()
        {
            string targetMod = ReloadNRejoinConfig.Instance?.TargetMod;
            //if (Environment.GetEnvironmentVariable("TBT_REJOINACTIVE") is not "1")
            if(!reloadModOnFound)
            {
                return;
            }
            if (!CheckExistingModName(targetMod))
            {
                Environment.SetEnvironmentVariable("TBT_REJOINACTIVE", null);
                Environment.SetEnvironmentVariable("TBT_REJOINPLAYER", null);
                Environment.SetEnvironmentVariable("TBT_REJOINWORLD", null);
                ModContent.GetInstance<TyrantBuildTools>().Logger.Error($"RNR: Could not find the mod '{targetMod}' for reloading.");
                return;
            }
            Console.WriteLine("RNR: Finished loading mod sources");
            const BindingFlags instanceflags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            object ui = typeof(Main).Assembly.GetType("Terraria.ModLoader.UI.Interface").GetField("modSources", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

            IList items = GetFieldValue(ui, "_items") as IList;
            UIPanel entry = items.Cast<object>().FirstOrDefault(t => GetFieldValue(t, "modName").Equals(targetMod)) as UIPanel;
            if (entry == null)
            {
                ModContent.GetInstance<TyrantBuildTools>().Logger.Debug("RNR: Mod was not found within the mod list?");
                return;
            }

            entry.GetType().GetMethod("BuildAndReload", instanceflags).Invoke(entry, new object[] { null, null });

            Environment.SetEnvironmentVariable("TBT_REJOINACTIVE", "1");

            static object GetFieldValue(object obj, string fieldName) => obj.GetType().GetField(fieldName, instanceflags).GetValue(obj);
        }

        private static void IL_UIModSources_Populate_TriggerSomethingAfterModFolderPopulation(ILContext il)
        {
            ILCursor c = new(il);
            c.GotoNext(MoveType.Before, t => t.MatchPop());
            c.Emit(OpCodes.Dup);
            c.EmitDelegate<Action<Task>>((Task task) => task.ContinueWith((t) => Task.Run(OnFinishLoadingModSources)));
        }

        private static void IL_Main_DrawMenu(MonoMod.Cil.ILContext il)
        {
            ILCursor c = new(il);
            if (c.TryGotoNext(i => i.MatchCall("Terraria.ModLoader.UI.Interface", "ModLoaderMenus")))
            {
                //c.Emit(OpCodes.Ldsflda, typeof(Main).GetField(nameof(Main.menuMode)));
                c.EmitDelegate(() =>
                {
                    if (reloadModSources)
                    {
                        Main.menuMode = 10001;
                        reloadModSources = false;
                        //Environment.SetEnvironmentVariable("TBT_REJOINACTIVE", "1", EnvironmentVariableTarget.Process);
                    }
                });
            }
            else
            {
                ModContent.GetInstance<TyrantBuildTools>().Logger.Debug("IL for changing menu mode failed");
            }
        }

        static bool enterCharacterSelectMenu;
        static bool reloadModSources;
        private void Main_DrawMenu(Terraria.On_Main.orig_DrawMenu orig, Main self, GameTime gameTime)
        {
            orig(self, gameTime);
            if (reloadModSources)
            {
                bool was10001before = Main.menuMode == 10001;
                Main.menuMode = 10001;

                if (was10001before)
                {
                }
                reloadModSources = false;

            }

            if (enterCharacterSelectMenu)
            {
                enterCharacterSelectMenu = false;
                Environment.SetEnvironmentVariable("TBT_REJOINACTIVE", null);
                Environment.SetEnvironmentVariable("TBT_REJOINPLAYER", null);
                Environment.SetEnvironmentVariable("TBT_REJOINWORLD", null);
                Main.OpenCharacterSelectUI();

                var player = Main.PlayerList.FirstOrDefault(d => d.Name == CharacterName); // put player name here
                if (player == null)
                {
                    Mod.Logger.Error("RNR: Player could not be found?");
                    return;
                }
                Main.SelectPlayer(player);

                Main.OpenWorldSelectUI();
                UIWorldSelect worldSelect = (UIWorldSelect)typeof(Main).GetField("_worldSelectMenu", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)!.GetValue(null!)!;
                UIList uiList = (UIList)typeof(UIWorldSelect).GetField("_worldList", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.GetValue(worldSelect)!;
                var item = uiList._items.OfType<UIWorldListItem>().FirstOrDefault(d =>
                {
                    return ((WorldFileData)typeof(UIWorldListItem).GetField("_data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(d)!).Name == WorldName; // put world name here
                });
                if (item == null)
                {
                    Mod.Logger.Error("RNR: World could not be found?");
                    return;
                }
                typeof(UIWorldListItem).GetMethod("PlayGame", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.Invoke(item, new object[]
                {
                    new UIMouseEvent(item, item.GetDimensions().Position()), item
                });


            }
        }
    }

    //abstract class ReloadNRejoinSystem : ModSystem
    //{
    //    const BindingFlags finstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    //    const BindingFlags fstatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    //    #region For adding a delegate to OnSuccessfulLoad
    //    delegate ref Action ActionRefFunc();
    //    private static ActionRefFunc OnSuccessfulLoadFieldRef;
    //    static ReloadNRejoinSystem()
    //    {
    //        FieldInfo ModLoader_OnSuccesfulLoad = typeof(ModLoader).GetField("OnSuccessfulLoad", fstatic);
    //        System.Reflection.Emit.DynamicMethod method = new("ModLoader_OnSuccesfulLoad_RefGetter", typeof(Action).MakeByRefType(), null);
    //        var il = method.GetILGenerator();
    //        il.Emit(System.Reflection.Emit.OpCodes.Ldsflda, ModLoader_OnSuccesfulLoad);
    //        il.Emit(System.Reflection.Emit.OpCodes.Ret);
    //        OnSuccessfulLoadFieldRef = method.CreateDelegate<ActionRefFunc>();
    //    }
    //    internal static event Action OnSuccessfulLoad
    //    {
    //        add
    //        {
    //            // combines the current delegate with another action
    //            // if it were to be suddenly changed by another thread while still combining, try again
    //            ref Action actionRef = ref OnSuccessfulLoadFieldRef();
    //            Action action, newAction;
    //            do
    //            {
    //                action = actionRef;
    //                newAction = (Action)Delegate.Combine(action, value);
    //            }
    //            while (!ReferenceEquals(Interlocked.CompareExchange(ref actionRef, newAction, action), action));
    //        }
    //        remove
    //        {
    //            ref Action actionRef = ref OnSuccessfulLoadFieldRef();
    //            Action action, newAction;
    //            do
    //            {
    //                action = actionRef;
    //                newAction = (Action)Delegate.Remove(action, value);
    //            }
    //            while (!ReferenceEquals(Interlocked.CompareExchange(ref actionRef, newAction, action), action));
    //        }
    //    }
    //    //static event Action OnSuccessfulLoad;
    //    #endregion // For adding a delegate to OnSuccessfulLoad

    //    private static object GetFieldValue(object obj, string fieldName) => (obj ?? throw new ArgumentNullException(nameof(obj))).GetType().GetField(fieldName, finstance).GetValue(obj);
    //    private static T GetFieldValue<T>(object obj, string fieldName) => (T)(obj ?? throw new ArgumentNullException(nameof(obj))).GetType().GetField(fieldName, finstance).GetValue(obj);

    //    private static Func<WorldFileData, bool> UIWorldSelect_CanWorldBePlayed = typeof(UIWorldSelect).GetMethod("CanWorldBePlayed", fstatic).CreateDelegate<Func<WorldFileData, bool>>();
    //    private static Action OnSuccessfulLoad_OpenWorld = () =>
    //    {
    //        string playerName = Environment.GetEnvironmentVariable("TBT_RNRPLAYER");
    //        string worldName = Environment.GetEnvironmentVariable("TBT_RNRWORLD");
    //        string active = Environment.GetEnvironmentVariable("TBT_RNRACTIVE");

    //        CancelRNR();
    //        OnSuccessfulLoad -= OnSuccessfulLoad_OpenWorld;

    //        var log = TyrantBuildTools.Instance.Logger;

    //        if (active is not "1")
    //        {
    //            goto failure;
    //        }

    //        if (string.IsNullOrEmpty(playerName) || string.IsNullOrEmpty(worldName))
    //        {
    //            //log.Info("RNR player or world is null");
    //            goto failure;
    //        }

    //        WorldGen.clearWorld();
    //        Main.LoadPlayers();
    //        if (Main.PlayerList.FirstOrDefault(player => player.Name == playerName) is not PlayerFileData targetPlayer)
    //        {
    //            log.Warn($"RNR player {playerName} could not be found, skipping RNR.");
    //            return;
    //        }
    //        targetPlayer.SetAsActive();

    //        Main.LoadWorlds();
    //        if (Main.WorldList.FirstOrDefault(world => world.Name == worldName) is not WorldFileData targetWorld)
    //        {
    //            log.Warn($"RNR: World {worldName} could not be found, skipping RNR.");
    //            goto failure;
    //        }
    //        if (!UIWorldSelect_CanWorldBePlayed(targetWorld))
    //        {
    //            log.Warn($"RNR: World {worldName} cannot be loaded, skipping RNR.");
    //            goto failure;
    //        }
    //        targetWorld.SetAsActive();

    //        WorldGen.playWorld();
    //        Main.menuMode = MenuID.Status;
    //        Main.MenuUI.SetState(null);
    //        return;

    //    failure:
    //        Main.menuMode = 0;
    //        return;
    //    };

    //    static void OnEnterModSourcesMenu()
    //    {
    //        if (!ReloadNRejoinConfig.Instance.EnableReloadNRejoin || Environment.GetEnvironmentVariable("TBT_RNRACTIVE") is not "0")
    //        {
    //            CancelRNR();
    //            return;
    //        }
    //        Environment.SetEnvironmentVariable("TBT_RNRACTIVE", null);
    //        Console.WriteLine("RNR: Finished loading mod sources");

    //        string targetModName = ReloadNRejoinConfig.Instance.TargetMod;
    //        object ui = typeof(ModLoader).Assembly.GetType("Terraria.ModLoader.UI.Interface").GetField("modSources", fstatic).GetValue(null);

    //        IList items = GetFieldValue<IList>(ui, "_items");
    //        UIPanel entry = items.OfType<UIPanel>().FirstOrDefault(t => GetFieldValue(t, "modName").Equals(targetModName));
    //        if (entry == null)
    //        {
    //            TyrantBuildTools.Instance.Logger.Debug("RNR: Mod was not found within the mod list?");
    //            return;
    //        }

    //        entry.GetType().GetMethod("BuildAndReload", finstance).Invoke(entry, new object[] { null, null });
    //        Environment.SetEnvironmentVariable("TBT_RNRACTIVE", "1");
    //    }
    //    static void CancelRNR()
    //    {
    //        Environment.SetEnvironmentVariable("TBT_RNRPLAYER", null);
    //        Environment.SetEnvironmentVariable("TBT_RNRWORLD", null);
    //        Environment.SetEnvironmentVariable("TBT_RNRACTIVE", null);
    //    }

    //    public static void SetRestart(string targetPlayerName, string targetWorldName, bool isReload, bool? exitWorldNoSave = null)
    //    {
    //        Environment.SetEnvironmentVariable("TBT_RNRPLAYER", targetPlayerName);
    //        Environment.SetEnvironmentVariable("TBT_RNRWORLD", targetWorldName);
    //        Environment.SetEnvironmentVariable("TBT_RNRACTIVE", "0");
    //        Main.menuMode = 888;
    //        UIState modSourcesState = (UIState)typeof(ModLoader).Assembly.GetType("Terraria.ModLoader.UI.Interface", true).GetField("modSources", fstatic).GetValue(null);
    //        Main.MenuUI.SetState(modSourcesState);
    //    }

    //    private static void IL_UIModSources_Populate_TriggerSomethingAfterModFolderPopulation(ILContext il)
    //    {
    //        ILCursor c = new(il);
    //        c.GotoNext(MoveType.Before, t => t.MatchPop());
    //        c.Emit(OpCodes.Dup);
    //        c.EmitDelegate<Action<Task>>((Task task) => task.ContinueWith((t) => Task.Run(OnEnterModSourcesMenu)));
    //    }
    //    /*private static void IL_ModLoader_InvokeTryEnterRNR(ILContext il)
    //    {
    //        ILCursor c = new(il);
    //        FieldInfo field = typeof(ModLoader).GetField("OnSuccessfulLoad", fstatic);
    //        c.GotoNext(MoveType.Before, i => i.MatchLdsfld(field));
    //        c.EmitDelegate(() =>
    //        {
    //            OnSuccessfulLoad?.Invoke();
    //        });
    //    }*/
    //    //private static object InvokeMethod(object obj, object[] args) => (obj ?? throw new ArgumentNullException(nameof(obj))).GetType().GetMethod(fieldName, finstance).GetValue(obj);
    //    ILHook ILPopulate, ILLoad;
    //    public override void Load()
    //    {
    //        base.Load();
    //        //Task.Run(() =>
    //        {
    //            if (Environment.GetEnvironmentVariable("TBT_RNRACTIVE") is "1")
    //                OnSuccessfulLoad += OnSuccessfulLoad_OpenWorld;
    //            var tmlAssembly = typeof(ModLoader).Assembly;
    //            Type uiModSourcesType = tmlAssembly.GetType("Terraria.ModLoader.UI.UIModSources");
    //            ILPopulate = new(uiModSourcesType.GetMethod("Populate", finstance), IL_UIModSources_Populate_TriggerSomethingAfterModFolderPopulation, true);
    //            //ILLoad = new(typeof(ModLoader).GetMethod("Load", fstatic), IL_ModLoader_InvokeTryEnterRNR, true);

    //        }//);
    //    }
    //    public override void Unload()
    //    {
    //        OnSuccessfulLoad -= OnSuccessfulLoad_OpenWorld;
    //        ILPopulate?.Dispose();
    //        ILLoad?.Dispose();
    //        base.Unload();
    //    }

    //}

}
