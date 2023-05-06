UPDATE: Version 4.0 involves enough changes to completely deprecate previous versions. Any mods relying on versions 2.X will no long be supported and will have to update themselves. **MCE will now conflict with the Better Networking mod, due to both mods making changes to Valheim's networking subsystem. You can disable MCE's networking optimizations via its new config file. MCE now removes buffer limits for Steam users, making Better Networking unnecessary when using mods that also use MCE. This change was necessary to support the sending of large data packages in an optimized way.**

UPDATE: Version 3.0 is a major version change due to breaking backwards compatibility. If you are a user, do not update MCE unless a mod you are using that uses it tells you to. This change is a lot less painful than the version 2 break, and the biggest benefit is the ability for mod authors to now take advantage of MCE syncing their configs without having a hard dependency on MCE. This means for playing by yourself, MCE isn't required to have. Mod authors can now, with a little code, have their mods be "MCE Compatible" with no required downloading or bundling.

Mod Config Enforcer is a utility mod for mod authors. It allows them to support servers setting configs and then enforcing those settings onto connecting clients using their mod. The mod allows mod authors to define config variables that can be local only (no server enforcement) as well as server enforced.

Manual Installation : Download and copy the ModConfigEnforcer.dll to your Valheim\Bepinex\plugins folder.

If you are downloading MCE because another mod relies on it for server config enforcement, you're done! When setup correctly, you will see two new DLL files created in the plugins folder, ZstdNet.dll and libzstd.dll. These files are necessary for MCE to compress and decompress ZPackage data for optimal performance.

-----

Hard dependency usage is still supported as it's the best way to do more complex configuration management with MCE, but it is no longer the only option. MCE v3 now supports config-by-convention, which is very easy to setup and ideal for smaller mods which simply use ConfigEntry objects for their Bepinex config. For a mod to be MCE compatible with automated config discovery, simply follow this example :

```
﻿public class Plugin : BaseUnityPlugin
{
   ﻿// this field is required to exist and be set to an object in order for MCE to process your mod
   ﻿object AutomatedConfigDiscovery;
   ﻿// this field will be discovered and picked up by MCE
   ﻿public ConfigEntry<int> Something;
   // this field will be discovered and picked up by MCE, with the value expected to be a full path to a file you want servers to sync to clients
   ﻿public string File_TestFile = "C:\Steam\steamapps\common\Valheim\BepInEx\plugins\testfile.txt";
   ﻿// this field will be ignored by MCE and thus not synced from servers to clients
   ﻿[NonSerialized]
   ﻿public ConfigEntry<bool> OtherVar;
    
   ﻿public Awake()
   ﻿{
   ﻿   ﻿// setting this here in Awake is required in order to guarantee MCE's automated config discovery finding your mod properly
   ﻿   ﻿AutomatedConfigDiscovery = this;
   ﻿   ﻿// all ConfigEntry fields you want MCE to discover must be bound before Awake ends for your mod
   ﻿   ﻿Something = Config.Bind("Section", "Something", 0, "Description");
   ﻿   ﻿OtherVar = Config.Bind("Section", "Other Var", true, "A config setting that will be ignored by MCE due to the [NonSerialized] attribute on it");
   ﻿}
    
   ﻿// these are entirely optional. If you don't have them, your mod simply won't get a callback for when these events happen
   ﻿void ServerConfigReceived() {}
   ﻿void ConfigReloaded() {}

﻿   // if file syncing is desired, this method is required to exist
﻿   void FileChanged() {}
}
```

If your Plugin class (name isn't required to be "Plugin", just that it extends BaseUnityPlugin, which is required for your mod to work with Valheim anyways), doesn't contain a field named "AutomatedConfigDiscovery" (spelling matters but not capitalization), MCE will not process your mod for automated config discovery. You can set the variable to any object instance you want. The object referenced by the variable will be what MCE scans for ConfigEntries and the callback methods for your mod. Any ConfigEntry fields with the NonSerialized attribute (as demonstrated above) will be ignored by MCE's automated config discovery.

An alternate example is as follows :

```
public class Plugin : BaseUnityPlugin
{
   ﻿﻿// this field is required to exist and be set to an object in order for MCE to process your mod
   public ﻿﻿object AutomatedConfigDiscovery;
   public ﻿WhateverYouWantToCallThis ConfigStuff = new WhateverYouWantToCallThis();
    
   ﻿﻿public Awake()
﻿   ﻿{
﻿﻿   ﻿   ﻿// setting this here in Awake is required in order to guarantee MCE's automated config discovery finding your mod properly
   ﻿﻿﻿   ﻿AutomatedConfigDiscovery = ConfigStuff;
   ﻿   ﻿﻿﻿// all ConfigEntry fields you want MCE to discover must be bound before Awake ends for your mod
   ﻿   ﻿﻿﻿Something = Config.Bind("Section", "Something", 0, "Description");
   ﻿   ﻿﻿﻿OtherVar = Config.Bind("Section", "Other Var", true, "A config setting that will be ignored by MCE due to the [NonSerialized] attribute on it");
﻿   ﻿}
}
    
public class WhateverYouWantToCallThis
{
   ﻿﻿﻿// this field will be discovered and picked up by MCE
   ﻿﻿﻿public ConfigEntry<int> Something;
   ﻿﻿// this field will be ignored by MCE and thus not synced from servers to clients
   ﻿﻿[NonSerialized]
   ﻿﻿public ConfigEntry<bool> OtherVar;
    
   ﻿﻿// these are entirely optional. If you don't have them, your mod simply won't get a callback for when these events happen.
   ﻿﻿void ServerConfigReceived() {}
   ﻿﻿void ConfigReloaded() {}
}
``` 

For manual mod registration and usage, the following still applies :

Mod authors: once downloaded and setup, in your mod project, add a reference to the ModConfigEnforcer.dll file. Your C# plugin class file should at minimum look like the following :

```
﻿using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using ModConfigEnforcer;
    
namespace YourMod
{
    [BepInPlugin("you.yourmod", ModName, Plugin.Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Version = "1.0";
        public const string ModName = "Your Mod";
        Harmony _Harmony;
    
        public static ConfigVariable<bool> TestVariable;
    
        private void Awake()
        {
            ConfigManager.RegisterMod(ModName, Config, ServerConfigReceived, ConfigReloaded);
            TestVariable = ConfigManager.RegisterModConfigVariable<bool>(ModName, "Test Variable", true, "General", "Just a test variable, doesn't do anything.", false);
	        FileVariable = ConfigManager.RegisterModFileWatcher(ModName, Path.Combine(Path.GetDirectoryName(Info.Location), "test.png"), false, FileChanged);
            _Harmony = Harmony.CreateAndPatchAll(Assembly.GetExecutingAssembly(), null);
        }
    
        private void OnDestroy()
        {
            if (_Harmony != null) _Harmony.UnpatchSelf();
        }
    
       ﻿ ﻿private void ServerConfigReceived()
       ﻿ ﻿{
       ﻿ ﻿}
    
 ﻿   ﻿    private void ConfigReloaded()
 ﻿   ﻿    {
﻿        }

﻿﻿﻿        private void FileChanged()
        ﻿﻿{
        ﻿﻿}
    }
}
```
    
The ConfigVariable<bool> type comes from MCE. It is what manages the config value for your mod, based on whether it should be set by the server or not. It's uses a C# generic type - any of the following types are currently supported (the limitation is due to what data types the ZPackage class will read and write from its data stream) :

* int
* uint
* bool
* byte
* byte[]
* char
* sbyte
* long
* ulong
* float
* double
* string
* ZPackage
* List<string>
* Vector3
* Quaternion
* ZDOID
* HitData

In order to use your configuration variable in your code, simply call (using the example above) Plugin.TestVariable.Value

If you want to use custom configuration methods, that's also supported. 

If you have any questions or need help, find me on the [Valheim Modding discord server](https://discord.gg/89bBsvK5KC)﻿.

If you want to look at the code to see how ModConfigEnforcer works, I've made the [GitHub repo public.](https://github.com/Pfhoenix/ModConfigEnforcer)
If you'd like to donate, head to [my Patreon page](https://www.patreon.com/pfhoenix).
