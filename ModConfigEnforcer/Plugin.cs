﻿using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

/*
 * changes for this release :
 * 
 */

namespace ModConfigEnforcer
{
	[BepInPlugin("pfhoenix.modconfigenforcer", "Mod Config Enforcer", Plugin.Version)]
	public class Plugin : BaseUnityPlugin
	{
		public const string Version = "2.3";
		Harmony _Harmony;
		public static ManualLogSource Log;

		private void Awake()
		{
			Log = Logger;
			_Harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
		}

		private void OnDestroy()
		{
			if (_Harmony != null) _Harmony.UnpatchSelf();
		}
	}
}
