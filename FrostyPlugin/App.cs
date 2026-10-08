using Frosty.Core.Interfaces;
using FrostySdk;
using FrostySdk.Interfaces;
using FrostySdk.Managers;
using System;
using System.Collections.Generic;
using System.Windows;

namespace Frosty.Core
{
    public sealed class App
    {
        public static AssetManager AssetManager;
        public static ResourceManager ResourceManager;
        public static FileSystem FileSystem;
        public static PluginManager PluginManager;
        public static EbxAssetEntry SelectedAsset;
        public static string SelectedProfile;
        public static string SelectedPack;
        public static ILogger Logger;
        public static HashSet<int> WhitelistedBundles = new HashSet<int>();

        public static readonly int Version = 1;

        // A host that keeps its own settings sets this before Config.Load(), so its
        // manager_config.json never collides with Frosty Mod Manager's. Null keeps
        // Frosty's usual %LocalAppData%\Frosty.
        public static string SettingsPathOverride;

        public static string ProfileSettingsPath => GlobalSettingsPath + "/" + ProfilesLibrary.ProfileName;
        public static string GlobalSettingsPath => SettingsPathOverride ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/Frosty";

        public static IEditorWindow EditorWindow => Application.Current.MainWindow as IEditorWindow;
    }
}
