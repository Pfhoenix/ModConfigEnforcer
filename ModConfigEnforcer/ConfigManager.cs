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
		/// <summary>
		/// This is being deprecated
		/// </summary>
		public static event ServerConfigReceivedDelegate ServerConfigReceived;

		class ModConfig
		{
			public string Name;
			public ConfigFile Config;
			public List<IConfigVariable> Variables = new List<IConfigVariable>();
			public ServerConfigReceivedDelegate ServerConfigReceived;

			public void Serialize(ZPackage zpg)
			{
				bool serialized = false;
				foreach (var mcv in Variables)
				{
					if (mcv.LocalOnly()) continue;
					if (!serialized)
					{
						serialized = true;
						zpg.Write(Name);
					}
					mcv.Serialize(zpg);
				}
			}

			public void Deserialize(ZPackage zpg)
			{
				foreach (var mcv in Variables)
				{
					if (mcv.LocalOnly()) continue;
					zpg.ReadVariable(mcv);
				}
			}
		}

		public static bool ShouldUseLocalConfig = true;

		static readonly List<string> Mods = new List<string>();
		static readonly Dictionary<string, ModConfig> ModConfigs = new Dictionary<string, ModConfig>();

		public static void RegisterRPC(ZRpc zrpc)
		{
			zrpc.Register<ZPackage>(ConfigRPCName, SetConfigValues);
		}

		public static void RegisterMod(string modName, ConfigFile configFile, ServerConfigReceivedDelegate scrd = null)
		{
			if (ModConfigs.ContainsKey(modName)) return;
			Mods.Add(modName);
			ModConfigs[modName] = new ModConfig { Name = modName, Config = configFile, ServerConfigReceived = scrd };
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

		static bool SerializeMod(string modname, ZPackage zpg)
		{
			if (!ModConfigs.TryGetValue(modname, out var modconfig)) return false;
			modconfig.Serialize(zpg);
			return true;
		}

		public static void SendConfigToClient(string modname, long peerID = 0L)
		{
			if (!ZNet.instance.IsDedicated() && !ZNet.instance.IsServer()) return;

			ZPackage zpg = new ZPackage();
			if (!SerializeMod(modname, zpg)) return;
			zpg.SetPos(0);
			ZNet.instance.m_routedRpc.InvokeRoutedRPC(peerID, ConfigRPCName, zpg);
		}

		public static void SendConfigsToClient(ZRpc rpc)
		{
			if (!ZNet.instance.IsDedicated() && !ZNet.instance.IsServer()) return;

			ZPackage zpg = new ZPackage();
			foreach (string m in Mods)
			{
				SerializeMod(m, zpg);
			}
			zpg.SetPos(0);
			rpc.Invoke(ConfigRPCName, zpg);
		}

		static void SetConfigValues(ZRpc rpc, ZPackage zpg)
		{
			Plugin.Log.LogInfo("Client received SetConfigValues");

			if (ZNet.instance.IsDedicated() || ZNet.instance.IsServer())
			{
				Plugin.Log.LogWarning("Server should not be sent config values!");
				return;
			}

			string m = zpg.ReadString();
			Dictionary<string, ModConfig> mods = new Dictionary<string, ModConfig>();
			while (!string.IsNullOrWhiteSpace(m))
			{
				if (!ModConfigs.TryGetValue(m, out var modconfig))
				{
					Plugin.Log.LogError("Could not find registered mod " + m);
					return;
				}
				modconfig.Deserialize(zpg);
				mods[m] = modconfig;
				Plugin.Log.LogDebug("Client updated with settings for mod " + m);
				if (zpg.GetPos() < zpg.Size()) m = zpg.ReadString();
				else m = null;
			}

			ShouldUseLocalConfig = false;

			foreach (var mod in mods.Values)
				mod.ServerConfigReceived?.Invoke();

			// this is to support mods that haven't switched to the new method yet
			ServerConfigReceived?.Invoke();
		}
	}
}
