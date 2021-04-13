using System;
using HarmonyLib;
using UnityEngine;

namespace ModConfigEnforcer
{
	sealed class Patches
	{
		[HarmonyPatch(typeof(ZNet))]
		[HarmonyPriority(int.MaxValue)]
		public static class ZNetPatches
		{
			[HarmonyPostfix]
			[HarmonyPatch("Awake")]
			public static void AwakePrefix(ZNet __instance)
			{
				ConfigManager.ShouldUseLocalConfig = true;// __instance.IsDedicated() || __instance.IsServer();
				Plugin.Log.LogInfo("ConfigManager.ShouldUseLocalConfig = " + ConfigManager.ShouldUseLocalConfig);
				ConfigManager.RegisterRPC(__instance.m_routedRpc);
			}

			[HarmonyPostfix]
			[HarmonyPatch("RPC_CharacterID")]
			public static void RPC_CharacterIDPostfix(ZNet __instance, ZRpc rpc, ZDOID characterID)
			{
				if (!__instance.IsDedicated() && !__instance.IsServer()) return;
				ConfigManager.SendConfigToClient(__instance.GetPeer(rpc).m_uid);
			}
		}
	}
}
