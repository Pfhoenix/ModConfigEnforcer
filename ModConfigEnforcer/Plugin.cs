using System;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace ModConfigEnforcer
{
	[BepInPlugin("pfhoenix.modconfigenforcer", "Mod Config Enforcer", Plugin.Version)]
	public class Plugin : BaseUnityPlugin
	{
		public const string Version = "4.0.0";
		Harmony _Harmony;
		public static ManualLogSource Log;

		private void Awake()
		{
			Log = Logger;

			var assembly = GetType().Assembly;
			string[] dlls = { "libzstd", "ZstdNet" };
			foreach (var dll in dlls)
			{
				var s = assembly.GetManifestResourceStream(assembly.GetName().Name + "." + dll + ".dll");
				if (s == null) Log.LogError("Failed to load " + dll + ".dll from resource stream!");
				else
				{
					var path = Paths.PluginPath + "\\" + dll + ".dll";
					if (!File.Exists(path))
					{
						using (var destinationStream = new FileStream(path, FileMode.OpenOrCreate))
						{
							s.CopyTo(destinationStream);
						}
					}
				}
			}

			_Harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
		}

		void Start()
		{
			var cmmi = typeof(ConfigManager).GetMethod("RegisterAutomatedModConfigVariable", BindingFlags.Static | BindingFlags.Public);
			foreach (var pi in Chainloader.PluginInfos.Values)
			{
				if (pi.Instance == this) continue;
				if (!pi.Instance || !pi.Instance.isActiveAndEnabled) continue;

				Type t = pi.Instance.GetType();
				var fields = t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				object automatedConfigDiscovery = null;
				foreach (var field in fields)
				{
					if (string.Compare(field.Name, "AutomatedConfigDiscovery", true) == 0)
					{
						automatedConfigDiscovery = field.GetValue(pi.Instance);
						break;
					}
				}

				if (automatedConfigDiscovery != null)
				{
					Log.LogDebug("... searching for configuration for " + pi.Metadata.Name + " version " + pi.Metadata.Version + " ...");

					t = automatedConfigDiscovery.GetType();
					// search for ServerConfigReceived and ConfigReloaded methods first
					Action scr = null;
					Action cr = null;
					var methods = t.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
					foreach (var method in methods)
					{
						if (string.Compare(method.Name, "ServerConfigReceived", true) == 0)
						{
							scr = (Action)Delegate.CreateDelegate(typeof(Action), automatedConfigDiscovery, method);
							break;
						}

						if (string.Compare(method.Name, "ConfigReloaded", true) == 0)
						{
							cr = (Action)Delegate.CreateDelegate(typeof(Action), automatedConfigDiscovery, method);
							break;
						}
					}

					string modName = null;
					fields = t.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
					int bound = 0;
					foreach (var field in fields)
					{
						if (field.IsNotSerialized) continue;

						if (field.FieldType.IsSubclassOf(typeof(ConfigEntryBase)))
						{
							if (modName == null)
							{
								modName = pi.Metadata.Name;
								ConfigManager.RegisterMod(modName, pi.Instance.Config, scr, null);
							}
							var tcmmi = cmmi.MakeGenericMethod(field.FieldType.GetGenericArguments()[0]);
							tcmmi.Invoke(null, new object[] { modName, field.GetValue(pi.Instance) });
							Log.LogDebug("... bound " + field.Name);
							bound++;
						}
					}

					if (bound == 0) Log.LogWarning("... no configuration found to enforce!");
					else ConfigManager.SortModVariables(modName);
				}
			}
		}

		public static bool IsAdmin(Player player)
		{
			// at the main menu, so being admin makes sense
			if (!ZNet.instance) return true;
			if (!player) return false;
			if (ZNet.instance.m_adminList == null) return false;
			return ZNet.instance.ListContainsId(ZNet.instance.m_adminList, ZNet.instance.GetPeer(player.m_nview.GetZDO().m_uid.userID).m_rpc.GetSocket().GetHostName());
		}

		private void OnDestroy()
		{
			ConfigManager.ClearModConfigs();
			if (_Harmony != null) _Harmony.UnpatchSelf();
		}
	}
}
