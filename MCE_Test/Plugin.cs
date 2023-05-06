using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace MCE_Test
{
	[BepInPlugin("pfhoenix.mce_test", Plugin.ModName, Plugin.Version)]
	[BepInDependency("pfhoenix.modconfigenforcer", BepInDependency.DependencyFlags.HardDependency)]
	public class Plugin : BaseUnityPlugin
	{
		public const string Version = "6.6.6.5";
		public const string ModName = "MCE Test Mod";
		Harmony _Harmony;
		public static ManualLogSource Log;

		public object AutomatedConfigDiscovery;
		public ConfigEntry<int> TestInt;
		public string File_TestFile;

		private void Awake()
		{
#if DEBUG
			Log = Logger;
#else
			Log = new ManualLogSource(null);
#endif
			_Harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);

			AutomatedConfigDiscovery = this;
			File_TestFile = Path.Combine(Path.GetDirectoryName(Info.Location), "test.png");
			TestInt = Config.Bind<int>("test", "TestInt", 12);
		}

		private void OnDestroy()
		{
			if (_Harmony != null) _Harmony.UnpatchSelf();
		}

		void ServerConfigReceived()
		{
			Log.LogInfo("MCE Test received server config! TestInt set to " + TestInt.Value);
		}

		void FileChanged(string file, ZPackage data)
		{
			File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(File_TestFile), "received.png"), data.GetArray());
		}
	}
}
