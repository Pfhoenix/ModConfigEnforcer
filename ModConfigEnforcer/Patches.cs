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
	}
}
