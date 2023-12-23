using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Terraria.ModLoader;
using Terraria.ModLoader.Core;
using Ionic.Zlib;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using TyrantBuildTools.Config;

#nullable enable

namespace TyrantBuildTools
{
    internal class PackagingChanges : ModSystem
    {
        Hook? hook, shouldCompressHook, convertHook;
        ILHook? addFileIL, asyncModBuild;
        const BindingFlags finstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        const BindingFlags fstatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        const BindingFlags fany = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        public override void Load()
        {
            base.Load();
            // runs async to not block loading
            Task.Run(() =>
            {
                //typeof(ModLoader).Assembly.GetType("Terraria.ModLoader.Core")
                shouldCompressHook = new(typeof(TmodFile).GetMethod("ShouldCompress", fstatic)!, (Func<string, bool> orig, string fileName)
                    => (!PackagingChangesConfig.Instance.SkipImageCompression || !fileName.EndsWith(".png")) && orig(fileName)
                    , true);
                //hook = new(typeof(TmodFile).GetMethod("AddFile", finstance)!, (Action<TmodFile, string, byte[]> orig, TmodFile self, string fileName, byte[] data) =>
                //orig(self, fileName, data)
                //, true);
                convertHook = new(typeof(ModLoader).Assembly.GetType("Terraria.ModLoader.Core.ContentConverters", true)!.GetMethod("Convert", fstatic)!,
                    static (Convert_orig orig, ref string resourceName, FileStream src, MemoryStream dst) =>
                    {
                        if (PackagingChangesConfig.Instance.SkipImageCompression && Path.GetExtension(resourceName.AsSpan()).Equals(".png", StringComparison.InvariantCulture))
                        {
                            return false;
                        }
                        return orig(ref resourceName, src, dst);
                    }, true);

                addFileIL = new(typeof(TmodFile).GetMethod("AddFile", finstance)!, static il =>
                {
                    ILCursor c = new(il);
                    // new DeflateStream(CompressionMode.)
                    ConstructorInfo targetDeflateStreamCtor = typeof(DeflateStream).GetConstructor(new Type[] { typeof(Stream), typeof(CompressionMode) })!;
                    ConstructorInfo deflateStreamCtor = typeof(DeflateStream).GetConstructor(new Type[] { typeof(Stream), typeof(CompressionMode), typeof(CompressionLevel) })!;
                    if (c.TryGotoNext(MoveType.Before, i => i.MatchNewobj(targetDeflateStreamCtor)))
                    {
                        c.Instrs[c.Index].Operand = c.Module.ImportReference(deflateStreamCtor);
                        c.EmitDelegate(() => PackagingChangesConfig.Instance?.FastCompression is true ? (int)CompressionLevel.BestSpeed : (int)CompressionLevel.Default);
                        //c.Emit(OpCodes.Ldc_I4, (int)CompressionLevel.BestSpeed);
                    }
                    else
                    {
                        TyrantBuildTools.Instance.Logger.Warn("Fast packaging patch failed, could not find constructor overload for DeflateStream");
                    }
                }, true);

                Type modCompile = typeof(ModLoader).Assembly.GetType("Terraria.ModLoader.Core.ModCompile", true)!;
                Type buildingMod = modCompile.GetNestedType("BuildingMod", fany)!;
                if (PackagingChangesConfig.Instance?.AsyncPackaging == true)
                {
                    asyncModBuild = new(modCompile.GetMethod("Build", finstance, new Type[] { buildingMod })!, static il =>
                    {
                        ILCursor c = new(il);
                        Type modCompileType = typeof(ModLoader).Assembly.GetType("Terraria.ModLoader.Core.ModCompile", true)!;
                        Type buildingMod = modCompileType.GetNestedType("BuildingMod", fany)!;
                        MethodInfo? buildMod = modCompileType.GetMethod("BuildMod", finstance, new Type[] { buildingMod, typeof(byte[]).MakeByRefType(), typeof(byte[]).MakeByRefType() });
                        MethodInfo? packageMod = modCompileType.GetMethod("PackageMod", finstance);
                        if (IsNull(buildMod) || IsNull(packageMod))
                        {
                            return;
                        }
                        bool IsNull([NotNullWhen(false)] MethodInfo? method, [CallerArgumentExpression(nameof(method))] string paramName = "")
                        {
                            if (method == null)
                            {
                                TyrantBuildTools.Instance.Logger.Warn($"Alternative packaging patch failed, {paramName} '{method}' does not exist.");
                                return true;
                            }
                            return false;
                        }

                        if (c.TryGotoNext(MoveType.Before, i => i.MatchCall(buildMod)))
                        {
                            ILCursor cBefore = c.Clone();
                            if (c.TryGotoNext(MoveType.Before, i => i.MatchCallOrCallvirt(packageMod)))
                            {
                                cBefore.Emit(OpCodes.Ldarg_0);
                                cBefore.Emit(OpCodes.Ldarg_1);
                                cBefore.EmitDelegate(static (object modcompile, object mod) =>
                                {
                                    return Task.Run(() =>
                                    {
                                        modcompile.GetType().GetMethod("PackageMod", finstance)!.Invoke(modcompile, new object[] { mod });
                                    });
                                });

                                VariableDefinition task = new(c.Module.ImportReference(typeof(Task)));
                                c.Body.Variables.Add(task);
                                cBefore.Emit(OpCodes.Stloc, task);

                                c.Remove();
                                c.Emit(OpCodes.Ldloc, task);
                                c.EmitDelegate(static (object modCompile, object mod, Task task) =>
                                {
                                    if (!task.IsCompleted)
                                    {
                                        task.ConfigureAwait(false).GetAwaiter().GetResult();
                                        Console.WriteLine("Watiting packaging task");
                                    }
                                });

                            }
                        }
                    }, true);
                }
            });


        }
        /*private static Task<object> PackageModAsync(object mod, out byte[] code, out byte[] pdb, object modcompile)
        {
            code = Array.Empty<byte>();
            pdb = Array.Empty<byte>();
            return Task.Run(() =>
            {
                Type modCompileType = typeof(ModLoader).Assembly.GetType("Terraria.ModLoader.Core.ModCompile", true)!;
                object[] args = new object[] { mod, Array.Empty<byte>(), Array.Empty<byte>() };
                modCompileType.GetMethod("BuildMod", fany)!.Invoke(modcompile, args);
                return (object)Tuple.Create((byte[])args[1], (byte)args[2]);
            });
        }*/
        delegate bool Convert_orig(ref string resourceName, FileStream src, MemoryStream dst);
        delegate bool BuildMod_orig(object modCompile, ref string resourceName, FileStream src, MemoryStream dst);
        public override void Unload()
        {
            base.Unload();
            Dispose(ref hook);
            Dispose(ref shouldCompressHook);
            Dispose(ref convertHook);
            Dispose(ref addFileIL);
            Dispose(ref asyncModBuild);
        }
        private void Dispose<T>(ref T? value) where T : class, IDisposable
        {
            if (value != null)
                value.Dispose();
            value = null;
        }
    }
}
