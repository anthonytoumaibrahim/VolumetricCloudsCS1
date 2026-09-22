using System.Reflection;
using System.Runtime.InteropServices;
using VolumetricClouds;

[assembly: AssemblyTitle("Volumetric Weather")]
[assembly: AssemblyDescription("3D clouds that follow the game's weather, with real shadows, rain, lightning, fog and light halo controls. Requires Harmony.")]
[assembly: AssemblyProduct("VolumetricClouds")]
[assembly: ComVisible(false)]

// The release is Mod.Version; the wildcard fills the fourth part with the time of day
// (seconds / 2), which gives every build a distinct assembly identity -- what lets the
// game load a rebuilt DLL instead of the copy it already has cached. Paired with
// <Deterministic>false</Deterministic> in the .csproj -- it does nothing without it.
[assembly: AssemblyVersion(Mod.Version + ".*")]
[assembly: AssemblyInformationalVersion(Mod.Version)]
