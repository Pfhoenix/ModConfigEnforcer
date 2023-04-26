using BepInEx.Configuration;
using HarmonyLib;

namespace ModConfigEnforcer
{
	sealed class Patches
	{
		[HarmonyPatch(typeof(ZNet))]
		public static class ZNetPatches
		{
			[HarmonyPrefix]
			[HarmonyPatch("Awake")]
			public static void AwakePrefix(ZNet __instance)
			{
				ConfigManager.ShouldUseLocalConfig = true;
			}

			[HarmonyPrefix]
			[HarmonyPriority(int.MaxValue)]
			[HarmonyPatch("OnNewConnection")]
			public static void OnNewConnectionPrefix(ZNet __instance, ZNetPeer peer)
			{
				ConfigManager.ShouldUseLocalConfig = true;
				if (!__instance.IsDedicated() && !__instance.IsServer()) ConfigManager.RegisterRPC(peer.m_rpc);
			}

			[HarmonyPrefix]
			[HarmonyPriority(int.MaxValue)]
			[HarmonyPatch("RPC_ServerHandshake")]
			public static void RPC_ServerHandshakePrefix(ZNet __instance, ZRpc rpc)
			{
				if (!__instance.IsDedicated() && !__instance.IsServer()) return;
				ConfigManager.SendConfigsToClient(rpc);
			}
		}

		[HarmonyPatch(typeof(ConfigEntryBase))]
		public static class BepinexConfigEntryBasePatch
		{
			[HarmonyPrefix]
			[HarmonyPatch("OnSettingChanged")]
			public static bool OnSettingChangedPrefix(object __instance)
			{
				return ConfigManager.IsConfigLocked(((ConfigEntryBase)__instance).Definition.Key) ? ConfigManager.ShouldUseLocalConfig : true;
			}
		}

		[HarmonyPatch(typeof(Terminal))]
		public static class TerminalPatches
		{
			[HarmonyPostfix]
			[HarmonyPatch("InitTerminal")]
			public static void InitTerminalPostfix(Terminal __instance)
			{
				new Terminal.ConsoleCommand("mce", "shows info for Mod Config Enforcer", delegate (Terminal.ConsoleEventArgs args)
				{
					bool admin = !ZNet.instance || ZNet.instance.IsDedicated() || ZNet.instance.IsServer();
					if (!admin) admin = Player.m_localPlayer && ZNet.instance.ListContainsId(ZNet.instance.m_adminList, ZNet.instance.GetPeer(Player.m_localPlayer.m_nview.GetZDO().m_uid.userID).m_rpc.GetSocket().GetHostName());

					for (int i = 0; i < args.Args.Length; i++)
					{
						args.Args[i] = args[i].ToLower();
					}

					if (args.Length == 2)
					{
						if (args[1] == "list")
						{
							// display registered mods (and whether auto-discovered or not)
							foreach (var mc in ConfigManager.GetRegisteredModConfigs())
							{
								args.Context.AddString(".. " + mc.Name + " (" + mc.GetRegistrationType() + ")");
							}
						}
						else if (args[1] == "reload")
						{
							if (admin) args.Context.AddString(".. missing mod registration name");
							else args.Context.AddString("<color=orange>mce reload</color> is not available on clients in multiplayer.");
						}
						else args.Context.AddString(".. unknown command option '" + args[1] + "'");
					}
					else if (args.Length > 2)
					{
						if (args[1] == "reload" && !admin)
						{
							args.Context.AddString("<color=orange>mce reload</color> is not available on clients in multiplayer.");
							return;
						}

						if (args[1] == "list" || args[1] == "reload")
						{
							string modname = "";
							for (int i = 2; i < args.Length; i++)
								modname += i > 2 ? " " + args[i] : args[i];
							var mc = ConfigManager.GetRegisteredModConfig(modname, true);
							if (mc == null) args.Context.AddString(".. mod named '" + modname + "' not found!");
							else if (args[1] == "list")
							{
								foreach (var v in mc.Variables)
								{
									string vn = v.GetType().Name;
									vn = vn.Remove(vn.IndexOf('`'));
									args.Context.AddString(".. " + v.GetName() + " :" + (v.LocalOnly() ? " localOnly " : " ") + vn + " : " + v.GetValue());
								}
							}
							else
							{
								args.Context.AddString(".. reloading config for " + modname);
								mc.Config.Reload();
							}
						}
						else args.Context.AddString(".. unknown command option '" + args[1] + "'");
					}
					else
					{
						args.Context.AddString("<color=orange>mce</color> command supports the following options :");
						args.Context.AddString("<color=yellow>list</color> - displays a list of each mod registered with MCE and their registration method");
						args.Context.AddString("<color=yellow>list <mod registration name></color> - displays a list of all config options registered for the mod");
						if (admin)
							args.Context.AddString("<color=yellow>reload <mod registration name></color> - reloads the config options from file for the mod (on servers, this will also send config updates to all clients)");
					}
				});
			}
		}
	}
}
