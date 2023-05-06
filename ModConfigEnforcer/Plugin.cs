using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace ModConfigEnforcer
{
	[BepInPlugin("pfhoenix.modconfigenforcer", "Mod Config Enforcer", Plugin.Version)]
	public class Plugin : BaseUnityPlugin
	{
		public const string Version = "4.0.1";
		Harmony _Harmony;
		public static ManualLogSource Log;
		public static Plugin instance;

		public static ConfigVariable<bool> OptimizeNetworking;

		private void Awake()
		{
			instance = this;

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

			ConfigManager.RegisterMod("_MCE_", Config);
			OptimizeNetworking = ConfigManager.RegisterModConfigVariable("_MCE_", "Optimize Networking", true, "Networking", "Optimize Valheim's networking subsystem", true);

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
						if (field.FieldType.IsSubclassOf(typeof(ConfigEntryBase)) && !field.IsNotSerialized)
						{
							if (modName == null)
							{
								modName = pi.Metadata.Name;
								ConfigManager.RegisterMod(modName, pi.Instance.Config, scr, null);
							}
							var tcmmi = cmmi.MakeGenericMethod(field.FieldType.GetGenericArguments()[0]);
							tcmmi.Invoke(null, new object[] { modName, field.GetValue(automatedConfigDiscovery) });
							Log.LogDebug("... bound " + field.Name);
							bound++;
						}
						else if (field.FieldType == typeof(string) && field.Name.StartsWith("File_"))
						{
							string varname = field.Name.Substring(5);
							var fcm = methods.FirstOrDefault(m => string.Compare(m.Name, "FileChanged", true) == 0);
							if (fcm != null)
							{
								if (modName == null)
								{
									modName = pi.Metadata.Name;
									ConfigManager.RegisterMod(modName, pi.Instance.Config, scr, null);
								}
								ConfigManager.RegisterModFileWatcher(modName, (string)field.GetValue(automatedConfigDiscovery), field.IsNotSerialized, (Action<string, ZPackage>)Delegate.CreateDelegate(typeof(Action<string, ZPackage>), automatedConfigDiscovery, fcm));
								Log.LogDebug("... bound " + field.Name);
								bound++;
							}
						}
					}

					if (bound == 0) Log.LogWarning("... no configuration found to enforce!");
					else ConfigManager.SortModVariables(modName);
				}
			}
		}

		/*class ZRpcSendInfo
		{
			public ZRpc Peer;
			public string RPCName;
			public ZPackage Data;
		}*/

		//Queue<ZRpcSendInfo>

		IEnumerator SendDataViaZRpc(ZRpc peer, string rpc, ZPackage[] zpgs)
		{
			for (int i = 0; i < zpgs.Length; i++)
			{
				peer.GetSocket().Flush();
				peer.Invoke(rpc, zpgs[i]);
				yield return new WaitForSecondsRealtime(1f);
			}
		}

		public void SendDataToZRpc(ZRpc peer, string rpc, ZPackage[] data)
		{
			StartCoroutine(SendDataViaZRpc(peer, rpc, data));
		}

		/*void Update()
		{

		}*/

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
