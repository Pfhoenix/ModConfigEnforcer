using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;
using ZstdNet;

namespace ModConfigEnforcer
{
	public interface IConfigVariable
	{
		string GetName();
		bool LocalOnly();
		object GetValue();
		void SetValue(object o);
		Type GetValueType();
		bool ValueChanged { get ; set; }

		/// <summary>
		/// Serialize is called when an IConfigVariable implementation needs to serialize its data. This is always called.
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

	// need a new class, AutomatedConfigWrapper<T> : IConfigVariable
	// it just reads/writes to/from the passed in ConfigEntry<T>
	public class AutomatedConfigWrapper<T> : IConfigVariable
	{
		ConfigEntry<T> _ConfigFileEntry;
		FieldInfo _TypedValueFI;
		T _LastValidValue;

		public bool ValueChanged { get; set; }

		public AutomatedConfigWrapper(ConfigEntry<T> configEntry)
		{
			_ConfigFileEntry = configEntry;
			_ConfigFileEntry.SettingChanged += SettingChanged;
			_TypedValueFI = configEntry.GetType().GetField("_typedValue", BindingFlags.Instance | BindingFlags.NonPublic);
		}

		// this is an attempt to catch the configentry value being changed by the client
		void SettingChanged(object sender, EventArgs e)
		{
			if (!_LastValidValue.Equals(_ConfigFileEntry.Value))
			{
				if (!ConfigManager.ShouldUseLocalConfig)
				{
					SetValue(_LastValidValue);
				}
				else _LastValidValue = (T)GetValue();
			}
		}

		public string GetName() => _ConfigFileEntry.Definition.Key;
		public bool LocalOnly() => false;
		public Type GetValueType() => typeof(T);
		public object GetValue() => ConfigManager.ShouldUseLocalConfig ? _ConfigFileEntry.Value : _LastValidValue;
		
		public void SetValue(object o)
		{
			_LastValidValue = (T)o;
			if (ConfigManager.ShouldUseLocalConfig)
			{
				ValueChanged = !_ConfigFileEntry.Value.Equals(_LastValidValue);
				_ConfigFileEntry.Value = _LastValidValue;
			}
			else _TypedValueFI.SetValue(_ConfigFileEntry, o);
		}

		public void Serialize(ZPackage zpg)
		{
			object v = GetValue();
			zpg.FillZPackage(GetValueType().IsEnum ? (int)v : v);
			ValueChanged = false;
		}

		public bool Deserialize(ZPackage zpg) => false;
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

		public ConfigVariable(ConfigEntry<T> configFileEntry)
		{
			_ConfigFileEntry = configFileEntry;
			_LocalValue = _ConfigFileEntry.Value;
			_LocalOnly = false;
		}

		public T Value => _LocalOnly ? _ConfigFileEntry.Value : (ConfigManager.ShouldUseLocalConfig ? _ConfigFileEntry.Value : _LocalValue);

		public bool ValueChanged { get; set; }

		public string GetName() => Key;
		public bool LocalOnly() => _LocalOnly;
		public Type GetValueType() => typeof(T);
		public object GetValue() => Value;

		public void SetValue(object o)
		{
			T t = (T)o;
			if (ConfigManager.ShouldUseLocalConfig)
			{
				ValueChanged = !_ConfigFileEntry.Value.Equals(t);
				_ConfigFileEntry.Value = t;
			}
			else _LocalValue = t;
		}

		public void Serialize(ZPackage zpg)
		{
			object v = GetValue();
			zpg.FillZPackage(GetValueType().IsEnum ? (int)v : v);
			ValueChanged = false;
		}

		public bool Deserialize(ZPackage zpg) => false;
	}

	public class ClientVariable<T> : IConfigVariable
	{
		T _Value;

		string Name;
		public T Value => _Value;

		public bool ValueChanged
		{
			get { return false; }
			set { }
		}

		public ClientVariable(string name, T value)
		{
			Name = name;
			_Value = value;
		}

		public string GetName() => Name;
		public object GetValue() => Value;
		public Type GetValueType() => typeof(T);

		public void SetValue(object o)
		{
			_Value = (T)o;
		}

		public bool LocalOnly() => true;
		public void Serialize(ZPackage zpg) { }
		public bool Deserialize(ZPackage zpg) => false;
	}

	public static class ConfigManager
	{
		class PackageTrackerInfo
		{
			ZPackage[] Packages;
			public int Received { get; private set; }
			public int Total => Packages?.Length ?? 0;

			// this assumes that the first ulong in zpg has been read already to know that it belongs in this PTI
			public bool Add(ZPackage zpg)
			{
				int order = zpg.ReadInt();
				int total = zpg.ReadInt();

				Plugin.Log.LogDebug("Client received package " + order + " of " + total);

				if (Packages == null)
					Packages = new ZPackage[total];

				if (Packages[order - 1] == null)
				{
					Packages[order - 1] = zpg;
					return ++Received == total;
				}
				else return false;
			}

			public ZPackage GetPackage()
			{
				if (Received != Total) return null;

				using (MemoryStream ms = new MemoryStream())
				{
					for (int i = 0; i < Packages.Length; i++)
					{
						byte[] data = Packages[i].ReadByteArray();
						ms.Write(data, 0, data.Length);
						Packages[i] = null;
					}

					Packages = null;

					ms.Flush();
					return new ZPackage(ms.ToArray());
				}
			}
		}

		static Dictionary<ulong, PackageTrackerInfo> PackageTracking = new Dictionary<ulong, PackageTrackerInfo>();

		const string ConfigRPCName = "ClientReceiveConfigData";

		static ulong LastPackageID;

		public static event Action<string> UnknownModConfigReceived;

		public class ModConfig
		{
			public string Name;
			public ConfigFile Config;
			public List<IConfigVariable> Variables = new List<IConfigVariable>();
			public Action ServerConfigReceived;
			public Action ConfigReloaded;

			public string GetRegistrationType()
			{
				bool manual = false;
				bool auto = false;
				foreach (var v in Variables)
				{
					if (v.GetType() == typeof(ConfigVariable<>)) manual = true;
					else if (v.GetType() == typeof(AutomatedConfigWrapper<>)) auto = true;
				}

				if (manual && auto) return "manual and automated discovery";
				else if (manual) return "manual";
				else return "automated discovery";
			}

			public void SortVariables()
			{
				Variables.Sort((a, b) => string.Compare(a.GetName(), b.GetName()));
			}

			public ZPackage Serialize(bool changesOnly = false)
			{
				ZPackage zpg = null;
				bool serialized = false;
				for (int i = 0; i < Variables.Count; i++)
				{
					if (Variables[i].LocalOnly()) continue;
					if (!changesOnly || Variables[i].ValueChanged)
					{
						if (!serialized)
						{
							zpg = new ZPackage();
							serialized = true;
							zpg.Write(Name);
						}
						zpg.Write(i);
						Variables[i].Serialize(zpg);
					}
				}

				return zpg;
			}

			// because each mod serializes into its own ZPackage, we can assume all data in zpg is for us
			public void Deserialize(ZPackage zpg)
			{
				Plugin.Log.LogDebug(System.Text.Encoding.Default.GetString(zpg.GetArray()));

				while (zpg.m_reader.PeekChar() > -1)
				{
					int index = zpg.ReadInt();
					if (index < 0 || index >= Variables.Count)
					{
						Plugin.Log.LogError("Invalid variable index " + index + " read from package, aborting deserialization for mod " + Name);
						return;
					}
					try
					{
						zpg.ReadVariable(Variables[index]);
					}
					catch (Exception ex)
					{
						Plugin.Log.LogError("Exception deserializing variable " + Variables[index].GetName() + " @ " + index + " for mod " + Name + ": " + ex.Message);
						return;
					}
				}
			}
		}

		public static bool ShouldUseLocalConfig = true;

		static readonly List<string> Mods = new List<string>();
		static readonly Dictionary<string, ModConfig> ModConfigs = new Dictionary<string, ModConfig>();
		static readonly Dictionary<string, IConfigVariable> AutomatedConfigsLocked = new Dictionary<string, IConfigVariable>();

		public static bool IsConfigLocked(string name)
		{
			return AutomatedConfigsLocked.ContainsKey(name);
		}

		public static void RegisterRPC(ZRpc zrpc)
		{
			zrpc.Register<ZPackage>(ConfigRPCName, ClientReceiveConfigData);
		}

		public static List<ModConfig> GetRegisteredModConfigs() => ModConfigs.Values.ToList();
		public static ModConfig GetRegisteredModConfig(string name, bool ignoreCase)
		{
			if (!ignoreCase)
			{
				if (ModConfigs.TryGetValue(name, out var mc)) return mc;
				else return null;
			}
			else
			{
				return ModConfigs.Values.FirstOrDefault(mc => string.Compare(mc.Name, name, true) == 0);
			}
		}

		public static void RegisterMod(string modName, ConfigFile configFile, Action scrd = null, Action cr = null)
		{
			if (ModConfigs.TryGetValue(modName, out var mc))
			{
				mc.Variables.Clear();
				return;
			}
			
			Mods.Add(modName);
			ModConfigs[modName] = new ModConfig { Name = modName, Config = configFile, ServerConfigReceived = scrd, ConfigReloaded = cr };
			configFile.ConfigReloaded += ConfigFile_ConfigReloaded;
		}

		static void ConfigFile_ConfigReloaded(object sender, EventArgs e)
		{
			if (ZNet.instance && !ZNet.instance.IsDedicated() && !ZNet.instance.IsServer()) return;

			List<ModConfig> updatedModConfigs = new List<ModConfig>();

			foreach (var mc in ModConfigs.Values.Where(mc => mc.Config == sender))
			{
				updatedModConfigs.Add(mc);
				mc.ConfigReloaded?.Invoke();
			}

			if (updatedModConfigs.Count > 0) SendConfigsToClients(updatedModConfigs, true);
		}

		public static ConfigVariable<T> RegisterModConfigVariable<T>(string modName, string varName, T defaultValue, string configSection, string configDescription, bool localOnly)
		{
			if (!ModConfigs.TryGetValue(modName, out ModConfig mc)) return null;
			var cv = new ConfigVariable<T>(mc.Config, configSection, varName, defaultValue, configDescription, localOnly);
			mc.Variables.Add(cv);
			return cv;
		}

		public static ConfigVariable<T> RegisterModConfigVariable<T>(string modName, string varName, T defaultValue, string configSection, ConfigDescription configDescription, bool localOnly)
		{
			if (!ModConfigs.TryGetValue(modName, out ModConfig mc)) return null;
			var cv = new ConfigVariable<T>(mc.Config, configSection, varName, defaultValue, configDescription, localOnly);
			mc.Variables.Add(cv);
			return cv;
		}

		public static void RegisterAutomatedModConfigVariable<T>(string modName, ConfigEntry<T> entry)
		{
			if (entry == null || !ModConfigs.TryGetValue(modName, out ModConfig mc)) return;
			AutomatedConfigWrapper<T> acw = new AutomatedConfigWrapper<T>(entry);
			AutomatedConfigsLocked.Add(acw.GetName(), acw);
			mc.Variables.Add(acw);
		}

		public static ClientVariable<T> RegisterClientVariable<T>(string modName, string varName, T value)
		{
			var cv = new ClientVariable<T>(varName, value);
			if (RegisterModConfigVariable(modName, cv)) return cv;
			else return null;
		}

		public static bool RegisterModConfigVariable(string modName, IConfigVariable cv)
		{
			if (!ModConfigs.TryGetValue(modName, out ModConfig mc)) return false;
			mc.Variables.Add(cv);
			return true;
		}

		public static void SortModVariables(string modName)
		{
			if (ModConfigs.TryGetValue(modName, out var mc)) mc.SortVariables();
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

		static ZPackage SerializeMod(string modname, bool changesOnly)
		{
			if (!ModConfigs.TryGetValue(modname, out var modconfig)) return null;
			return modconfig.Serialize(changesOnly);
		}

		public static ZPackage[] CompressPackage(ZPackage data)
		{
			var c = new Compressor();
			var cd = c.Wrap(data.GetArray());
			int chunks = cd.Length / 450000 + 1;
			var tosend = new ZPackage[chunks];
			int chunkSize = cd.Length / chunks;
			byte[] tsc;
			ZPackage zpg;
			LastPackageID++;
			//Plugin.Log.LogDebug("Creating " + chunks + " package" + (chunks > 1 ? "s" : "") + " for packageID " + LastPackageID);
			for (int i = 0; i < chunks - 1; i++)
			{
				tsc = new byte[chunkSize];
				Array.Copy(cd, i * chunkSize, tsc, 0, tsc.Length);
				zpg = new ZPackage();
				zpg.Write(LastPackageID);
				zpg.Write(i + 1);
				zpg.Write(chunks);
				zpg.Write(tsc);
				tosend[i] = zpg;
			}
			int lastChunkIndex = (chunks - 1) * chunkSize;
			tsc = new byte[cd.Length - lastChunkIndex];
			Array.Copy(cd, lastChunkIndex, tsc, 0, tsc.Length);
			zpg = new ZPackage();
			zpg.Write(LastPackageID);
			zpg.Write(chunks);
			zpg.Write(chunks);
			zpg.Write(tsc);
			tosend[tosend.Length - 1] = zpg;

			return tosend;
		}

		public static void SendConfigToClient(string modname, bool changesOnly, long peerID = 0L)
		{
			if (!ZNet.instance.IsDedicated() && !ZNet.instance.IsServer()) return;

			ZPackage zpg = SerializeMod(modname, changesOnly);
			if (zpg == null) return;

			ZPackage[] tosend = CompressPackage(zpg);
			for (int i = 0; i < tosend.Length; i++)
				ZNet.instance.m_routedRpc.InvokeRoutedRPC(peerID, ConfigRPCName, tosend[i]);
		}

		static void SendConfigsToClients(List<ModConfig> list, bool changesOnly)
		{
			if (!ZNet.instance) return;
			if (!ZNet.instance.IsDedicated() && !ZNet.instance.IsServer()) return;

			ZPackage zpg = new ZPackage();
			foreach (var mc in list)
			{
				ZPackage mzpg = mc.Serialize(changesOnly);
				if (mzpg != null) zpg.Write(mzpg);
			}

			if (zpg.Size() > 0)
			{
				ZPackage[] tosend = CompressPackage(zpg);
				for (int i = 0; i < tosend.Length; i++)
					ZNet.instance.m_routedRpc.InvokeRoutedRPC(ZNetView.Everybody, ConfigRPCName, tosend[i]);
			}
		}

		public static void SendConfigsToClient(ZRpc rpc)
		{
			if (!ZNet.instance) return;
			if (!ZNet.instance.IsDedicated() && !ZNet.instance.IsServer()) return;

			ZPackage data = new ZPackage();
			foreach (string m in Mods)
			{
				ZPackage mzpg = SerializeMod(m, false);
				if (mzpg != null) data.Write(mzpg);
			}

			if (data.Size() > 0)
			{
				ZPackage[] tosend = CompressPackage(data);
				for (int i = 0; i < tosend.Length; i++)
					rpc.Invoke(ConfigRPCName, tosend[i]);
			}
		}

		static void ClientReceiveConfigData(ZRpc rpc, ZPackage data)
		{
			if (ZNet.instance.IsDedicated() || ZNet.instance.IsServer())
			{
				Plugin.Log.LogWarning("Server should not be sent config values!");
				return;
			}

			ulong packageID = data.ReadULong();
			if (!PackageTracking.TryGetValue(packageID, out var pti))
			{
				Plugin.Log.LogDebug("Client received new packageID " + packageID);
				pti = new PackageTrackerInfo();
				PackageTracking[packageID] = pti;
			}

			if (!pti.Add(data)) return;

			SetConfigValues(pti.GetPackage());
		}

		static void SetConfigValues(ZPackage data)
		{
			ShouldUseLocalConfig = false;

			Dictionary<string, ModConfig> mods = new Dictionary<string, ModConfig>();

			// first decompress
			Decompressor d = new Decompressor();
			var dd = d.Unwrap(data.GetArray());
			ZPackage zpg = new ZPackage(dd.ToArray());

			try
			{
				ZPackage mzpg = zpg.ReadPackage();
				while (mzpg != null)
				{
					string m = mzpg.ReadString();
					if (!string.IsNullOrWhiteSpace(m))
					{
						if (!ModConfigs.TryGetValue(m, out var modconfig))
						{
							// this gives any registered mods a chance to claim or create a new mod config to receive the incoming data
							UnknownModConfigReceived?.Invoke(m);
							if (!ModConfigs.TryGetValue(m, out modconfig))
							{
								Plugin.Log.LogError("Could not find registered mod " + m);
								continue;
							}
							else Plugin.Log.LogInfo("Client received data for previously unregistered mod config " + m);
						}

						modconfig.Deserialize(mzpg);
						mods[m] = modconfig;
						Plugin.Log.LogDebug("Client updated with settings for mod " + m);
					}

					if (zpg.m_reader.PeekChar() != -1) mzpg = zpg.ReadPackage();
					else mzpg = null;
				}
			}
			catch (Exception ex)
			{
				Plugin.Log.LogError("Exception on client SetConfigValues: " + ex.Message);
			}

			foreach (var mod in mods.Values)
				mod.ServerConfigReceived?.Invoke();
		}

		public static void ClearModConfigs()
		{
			Mods.Clear();
			ModConfigs.Clear();
			AutomatedConfigsLocked.Clear();
		}
	}
}
