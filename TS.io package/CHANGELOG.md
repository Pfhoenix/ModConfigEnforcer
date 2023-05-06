## 4.0.1
* Fixed runtime exception from config variable registration

## 4.0.0
* Implemented sending of only updated congfig settings when changes happen at runtime.
* Implemented breaking up of large data into multiple, tracked chunks to clients.
* Implemented compression of sent data.
* Implemented arbitrary file syncing with filesystem watching.
* Implemented automated config discovery of file syncing variables.

## 3.0.3
* Fixed config file reloading to work at main menu

## 3.0.2
* Fixed console commands so they can be run at the main menu

## 3.0.1
* Fixed automatic discovered mods not being able to save config changes

## 3.0
* Added automatic config entry detection for mods that don't want a hard requirement on MCE.
* Added handling for config file being reloaded. Now enables an event handler for individual mods to react to config being reloaded, as well as sends the updated configs to all clients.
* Added console commands to see what mods MCE has registered, what config options are bound, and to reload a mod's config file and send to all clients
