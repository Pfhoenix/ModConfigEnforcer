using System;
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
		public const string Version = "3.0";
		Harmony _Harmony;
		public static ManualLogSource Log;

		private void Awake()
		{
			Log = Logger;
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
				}
			}
		}

		private void OnDestroy()
		{
			ConfigManager.ClearModConfigs();
			if (_Harmony != null) _Harmony.UnpatchSelf();
		}
	}
}
