This project requires a LEGITIMATE Tes 3 : Morrowind installation
It does not carry along any proprietary technologies or assets from Bethesda.


Vvardenfell is a Unity 6 project powered by DOTS. All geometry and object refs are spawned in the ECS World.
It leverages multi-threading on full scale by utilizing Burst compiled jobs to asynchronously load/unload cells.

There is no real gameplay yet, you can just coast with the camera and explore the world.

- Used OpenMW docs to decode .Nif & matrix transformations
- Vvardenfell bakes .bsa .esm archives into fast binaries the first time you load your morrowind installation, it does so to optimise the runtime.
- You should be able to load mods as well... Although some mods might have unpredictable behavior
