using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ModConfigEnforcer
{
	public interface IConfigVariable
	{
		string GetName();
		bool LocalOnly();
		object GetValue();
		void SetValue(object o);
		Type GetValueType();
		/// <summary>
		/// Serialize is called when an IConfigVariable implementation is wrapping a data type that the normal serialization process doesn't know how to handle
		/// </summary>
		/// <param name="zpg">The ZPackage object that will be sent over the network</param>
		void Serialize(ZPackage zpg);
		/// <summary>
		/// Deserialize is called when an IConfigVariable implementation is wrapping a data type that the normal deserialization process doesn't know how to handle
		/// </summary>
		/// <param name="zpg">The ZPackage object received by the client</param>
		/// <returns>Whether or not the deserialization was successful</returns>
		bool Deserialize(ZPackage zpg);
	}

	public class ConfigVariable<T> : IConfigVariable
	{
		ConfigEntry<T> _ConfigFileEntry;
		T _LocalValue;
		bool _LocalOnly;

		public string Key => _ConfigFileEntry.Definition.Key;

		public ConfigVariable(ConfigFile config, string section, string key, T defaultValue, string description, bool localOnly)
		{
			_ConfigFileEntry = config.Bind<T>(section, key, defaultValue, description);
			_LocalValue = _ConfigFileEntry.Value;
			_LocalOnly = localOnly;
		}

		public ConfigVariable(ConfigFile config, string section, string key, T defaultValue, ConfigDescription description, bool localOnly)
		{
			_ConfigFileEntry = config.Bind<T>(section, key, defaultValue, description);
			_LocalValue = _ConfigFileEntry.Value;
			_LocalOnly = localOnly;
		}

		public T Value => _LocalOnly ? _ConfigFileEntry.Value : (ConfigManager.ShouldUseLocalConfig ? _ConfigFileEntry.Value : _LocalValue);

		public string GetName()
		{
			return Key;
		}

		public bool LocalOnly()
		{
			return _LocalOnly;
		}

		public Type GetValueType()
		{
			return typeof(T);
		}

		public object GetValue()
		{
			return Value;
		}

		public void SetValue(object o)
		{
			T t = (T)o;
			if (ConfigManager.ShouldUseLocalConfig) _ConfigFileEntry.Value = t;
			else _LocalValue = t;
			Plugin.Log.LogInfo("Setting " + Key + " to " + t.ToString());
		}

		public void Serialize(ZPackage zpg)
		{
			object v = GetValue();
			zpg.FillZPackage(GetValueType().IsEnum ? (int)v : v);
		}

		public bool Deserialize(ZPackage zpg)
		{
			return false;
		}
	}

	public static class ConfigManager
	{
		const string ConfigRPCName = "SetConfigValues";

		public delegate void ServerConfigReceivedDelegate();
		public static event ServerConfigReceivedDelegate ServerConfigReceived;

		class ModConfig
		{
			public string Name;
			public ConfigFile Config;
			public List<IConfigVariable> Variables = new List<IConfigVariable>();
		}

		public static bool ShouldUseLocalConfig = true;

		static readonly List<string> Mods = new List<string>();
		static readonly Dictionary<string, ModConfig> ModConfigs = new Dictionary<string, ModConfig>();

		public static void RegisterRPC(ZRoutedRpc zrpc)
		{
			zrpc.Register(ConfigRPCName, new Action<long, ZPackage>(SetConfigValues));
		}

		public static void RegisterMod(string modName, ConfigFile configFile)
		{
			if (ModConfigs.ContainsKey(modName)) return;
			Mods.Add(modName);
			ModConfigs[modName] = new ModConfig { Name = modName, Config = configFile };
		}

		public static ConfigVariable<T> RegisterModConfigVariable<T>(string modName, string varName, T defaultValue, string configSection, string configDescription, bool localOnly)
		{
			if (!ModConfigs.TryGetValue(modName, out ModConfig mc)) return null;
			var cv = new ConfigVariable<T>(mc.Config, configSection, varName, defaultValue, configDescription, localOnly);
			mc.Variables.Add(cv);
			return cv;
		}

		public static bool RegisterModConfigVariable(string modName, IConfigVariable cv)
		{
			if (!ModConfigs.TryGetValue(modName, out ModConfig mc)) return false;
			mc.Variables.Add(cv);
			return true;
		}

		static void ReadVariable(this ZPackage zp, IConfigVariable cv)
		{
			Type t = cv.GetValueType();
			if (t == typeof(int)) cv.SetValue(zp.ReadInt());
			else if (t == typeof(uint)) cv.SetValue(zp.ReadUInt());
			else if (t == typeof(bool)) cv.SetValue(zp.ReadBool());
			else if (t == typeof(byte)) cv.SetValue(zp.ReadByte());
			else if (t == typeof(byte[])) cv.SetValue(zp.ReadByteArray());
			else if (t == typeof(char)) cv.SetValue(zp.ReadChar());
			else if (t == typeof(sbyte)) cv.SetValue(zp.ReadSByte());
			else if (t == typeof(long)) cv.SetValue(zp.ReadLong());
			else if (t == typeof(ulong)) cv.SetValue(zp.ReadULong());
			else if (t == typeof(float)) cv.SetValue(zp.ReadSingle());
			else if (t == typeof(double)) cv.SetValue(zp.ReadDouble());
			else if (t == typeof(string)) cv.SetValue(zp.ReadString());
			else if (t == typeof(ZPackage)) cv.SetValue(zp.ReadPackage());
			else if (t == typeof(List<string>))
			{
				int num = zp.ReadInt();
				List<string> list = new List<string>(num);
				for (int j = 0; j < num; j++)
				{
					list.Add(zp.ReadString());
				}
				cv.SetValue(list);
			}
			else if (t == typeof(Vector3)) cv.SetValue(new Vector3(zp.ReadSingle(), zp.ReadSingle(), zp.ReadSingle()));
			else if (t == typeof(Quaternion)) cv.SetValue(new Quaternion(zp.ReadSingle(), zp.ReadSingle(), zp.ReadSingle(), zp.ReadSingle()));
			else if (t == typeof(ZDOID)) cv.SetValue(zp.ReadZDOID());
			else if (t == typeof(HitData))
			{
				HitData hd = new HitData();
				hd.Deserialize(ref zp);
				cv.SetValue(hd);
			}
			else if (t.IsEnum) cv.SetValue(zp.ReadInt());
			else if (!cv.Deserialize(zp)) Plugin.Log.LogError("Unable to deserialize data for " + cv.ToString());
		}

		public static void FillZPackage(this ZPackage zp, params object[] ps)
		{
			ZRpc.Serialize(ps, ref zp);
		}

		public static void SendConfigToClient(long peerID)
		{
			if (!ZNet.instance.IsDedicated() && !ZNet.instance.IsServer()) return;

			ZPackage zpg = new ZPackage();
			foreach (string m in Mods)
			{
				if (!ModConfigs.TryGetValue(m, out var modconfig)) continue;
				foreach (var mcv in modconfig.Variables)
				{
					if (mcv.LocalOnly()) continue;
					mcv.Serialize(zpg);
				}
			}
			zpg.SetPos(0);
			ZNet.instance.m_routedRpc.InvokeRoutedRPC(peerID, ConfigRPCName, zpg);
		}

		static void SetConfigValues(long sender, ZPackage zpg)
		{
			if (ZNet.instance.IsDedicated() && ZNet.instance.IsServer()) return;

			foreach (string m in Mods)
			{
				if (!ModConfigs.TryGetValue(m, out var modconfig)) continue;
				Plugin.Log.LogInfo("Setting config variables for " + m);
				foreach (var mcv in modconfig.Variables)
				{
					if (mcv.LocalOnly()) continue;
					zpg.ReadVariable(mcv);
				}
			}

			ShouldUseLocalConfig = false;

			ServerConfigReceived?.Invoke();
		}
	}
}
