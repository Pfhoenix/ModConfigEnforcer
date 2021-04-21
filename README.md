# Mod Config Enforcer mod for Valheim

The code in this repository has been made publicly accessible strictly for learning purposes for those wanting to learn how it works or as a reference for working with the mod.

If you came here looking for a release or distributable, that is available on the [Nexus Mods page for the mod](https://www.nexusmods.com/valheim/mods/460).


## What does Mod Config Enforcer do?

MCE is a mod that implements server enforceable configuration settings onto clients that also have MCE installed. Mod authors can choose to use the built-in class ConfigVariable<> to declare a configuration variable that uses Bepinex's default config file for their mod, or mod authors can choose to implement the IConfigVariable interface to wrap whatever custom data or file saving/loading scheme they want.


## Functionality overview

MCE on the server will send to connecting clients (as soon as the server receives their PeerID) all registered configuration variables that are flagged to not be LocalOnly for all registered mods. This is done via RPC. MCE on clients receive the RPC and the configuration variable values via ZPackage, then sets all the matching configuration variables' values. Part of the benefit of the built-in ConfigVariable<> class is that it does not overwrite client configurations when it receives override values from the server (this functionality is supported by the IConfigVariable interface, but requires implementation by the mod author should they choose to make their own configuration variable class). Once the client configuration variables are set, MCE invokes an event, ConfigManager.ServerConfigReceived, to let any interested mod authors know.

The process of sending server configs to clients can be freely initiated by any mod by simply calling ConfigManager.SendConfigToClient(peerID). Note that ZNetScene.Everybody is a valid value for peerID, should the mod author want to update all connected clients.
