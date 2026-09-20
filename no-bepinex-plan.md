# No-BepInEx Plan — Run ValheimTooler by Mono Injection

Goal: run the tool on an otherwise untouched Valheim install, with **zero files
added to the game directory**. The game is started normally through Steam; a
small console tool injects the assembly into the running process afterwards.

This is not "vanilla" — code still enters the game process. It is, however,
the only option that leaves the install byte-identical on disk.

---

## Why this is feasible

The architecture already supports it. `ValheimTooler.Loader.Init()` is the
entry point the old launcher used, and it is still `public static` in the
shipped 1.12.0 binary (verified by decompilation). `ValheimToolerMod.dll` is
only the BepInEx bridge — it calls that same method by reflection and is not
needed here.

Valheim 1.0 is still a Mono runtime, not IL2CPP: the game ships managed
assemblies in `valheim_Data/Managed/`, and `ValheimToolerMod.dll` references
`BepInEx 5.4.23.5`, which is the Mono-only line of BepInEx.

`SharpMonoInjector.dll` is already committed at
`ValheimToolerLauncher/SharpMonoInjector.dll`, and its API matches what
`ValheimToolerLauncher/Utils/MonoInjector.cs` calls:

    Injector(string processName)
    IntPtr Inject(byte[] rawAssembly, string @namespace, string className, string methodName)
    void Eject(IntPtr assembly, string @namespace, string className, string methodName)

## What is missing

The old launcher copied a `Managed/` folder over the game's
`valheim_Data/Managed/`, which is how the injected assembly resolved its
dependencies. **That folder was never in git** — it only ever existed in release
artifacts.

It can be reconstructed exactly: every reference in
`ValheimTooler/ValheimTooler.csproj` *without* `<Private>False</Private>` is
copied to the build output and is therefore a real runtime dependency. That is
eight files:

    0Harmony.dll
    Mono.Cecil.dll
    Mono.Cecil.Mdb.dll
    Mono.Cecil.Pdb.dll
    Mono.Cecil.Rocks.dll
    MonoMod.RuntimeDetour.dll
    MonoMod.Utils.dll
    SharpConfig.dll

Everything marked `<Private>False</Private>` is a game assembly already loaded
in the process.

Rather than copying these into the game folder — which is what we are trying to
avoid — step 2 merges them into a single self-contained assembly.

---

## Step 1 — Replace the launcher with a console injector

New project `ValheimToolerInjector` (net472, x64, Exe). Reuses the existing
`SharpMonoInjector.dll`. The 2321-line WPF launcher is deleted
(see `ponytail-plan.md` P1).

```csharp
using System;
using System.IO;
using SharpMonoInjector;

namespace ValheimToolerInjector
{
    static class Program
    {
        static int Main(string[] args)
        {
            string dll = args.Length > 0
                ? args[0]
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ValheimTooler.dll");

            if (!File.Exists(dll))
            {
                Console.Error.WriteLine("Not found: " + dll);
                return 1;
            }

            try
            {
                using (var injector = new Injector("valheim"))
                {
                    IntPtr handle = injector.Inject(
                        File.ReadAllBytes(dll), "ValheimTooler", "Loader", "Init");

                    if (handle == IntPtr.Zero)
                    {
                        Console.Error.WriteLine("Injection returned a null handle.");
                        return 1;
                    }
                }
                Console.WriteLine("Injected. Press Del in game to toggle the window.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }
    }
}
```

`Injector.Eject` plus the existing `Loader.Unload` would allow a `--eject`
flag later. Not needed for a first working build.

**Must be built x64** — Valheim is a 64-bit process and the injector opens it
directly.

## Step 2 — Merge dependencies with ILRepack

Add the `ILRepack` package and a post-build target to
`ValheimTooler.csproj`, producing one self-contained `ValheimTooler.dll`:

```xml
<Target Name="Merge" AfterTargets="Build" Condition="'$(Configuration)'=='Release'">
  <ItemGroup>
    <MergeAsm Include="$(TargetPath)" />
    <MergeAsm Include="$(TargetDir)0Harmony.dll" />
    <MergeAsm Include="$(TargetDir)Mono.Cecil*.dll" />
    <MergeAsm Include="$(TargetDir)MonoMod.*.dll" />
    <MergeAsm Include="$(TargetDir)SharpConfig.dll" />
  </ItemGroup>
  <Exec Command="&quot;$(SolutionDir)packages\ILRepack.2.0.18\tools\ILRepack.exe&quot; /target:library /out:&quot;$(TargetDir)merged\ValheimTooler.dll&quot; /lib:&quot;$(ValheimManaged)&quot; @(MergeAsm->'&quot;%(FullPath)&quot;',' ')" />
</Target>
```

`/lib:` points ILRepack at the game assemblies so it can *resolve* them without
merging them. Do **not** pass `/internalize` on the first attempt: MonoMod
reflects over its own types at runtime to build detours, and internalizing them
is a known way to break that.

If the merge turns out to break Harmony, the fallback is to ship the eight DLLs
next to the injector and add an `AssemblyResolve` handler in `Loader.Init` that
loads them from there. That keeps the game folder clean too, at the cost of ~20
more lines.

## Step 3 — Make the projects buildable on any machine

Both `.csproj` files hardcode `F:\SteamLibrary\steamapps\common\Valheim\...`.
Replace with one property, set once:

```xml
<PropertyGroup>
  <ValheimPath Condition="'$(ValheimPath)'==''">C:\Program Files (x86)\Steam\steamapps\common\Valheim</ValheimPath>
  <ValheimManaged>$(ValheimPath)\valheim_Data\Managed</ValheimManaged>
</PropertyGroup>
```

Then every game reference becomes
`<HintPath>$(ValheimManaged)\assembly_valheim.dll</HintPath>`.
Affected: `assembly_valheim`, `assembly_utils`, `assembly_guiutils`,
`Splatform`, and the eight `UnityEngine.*` references, in
`ValheimTooler.csproj` and `ValheimToolerMod.csproj`.

Overridable per machine with `msbuild /p:ValheimPath=...` or a
`Directory.Build.props` that stays out of git.

## Step 4 — Fix the config path

Without BepInEx nobody writes `config_vt.path`, so
`Utils/ConfigManager.cs:88` falls through to `configPath = ""`. That resolves
to a relative path and drops `valheimtooler_settings.cfg` and
`valheimtooler_internal.cfg` into the game folder — exactly what this plan is
trying to avoid.

Note that an injected assembly has an **empty `Assembly.Location`**, so the
DLL's own directory is not available at runtime. Use a fixed location instead,
alongside the game's own save data:

```csharp
s_configurationPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "AppData", "LocalLow", "IronGate", "Valheim", "ValheimTooler");
Directory.CreateDirectory(s_configurationPath);
```

This also removes the `config_vt.path` handshake entirely
(`ponytail-plan.md` P4).

---

## Risks

**Main-thread violation — the one to watch.** `Loader.Init()` calls
`new GameObject()`, `AddComponent` and `DontDestroyOnLoad`. SharpMonoInjector
runs this on a remote thread attached to Mono, not on Unity's main thread.
Unity officially forbids that. In release players the guard is absent for many
of these calls, which is why the technique has worked for years — but it is not
guaranteed, and this is the most likely point of failure on 1.0.

If it does fail, the fix is a bootstrap: have `Init()` only install a Harmony
patch on a method that runs every frame, and create the `GameObject` from that
patch on the next frame, on the main thread. `RunPatches()` itself is fine to
call from the injected thread.

**Antivirus.** Process injection into a running game is indistinguishable from a
malware loader by signature. SharpMonoInjector is flagged by Windows Defender
regularly. An exclusion will be needed.

**Game updates.** Every Valheim patch can move the methods the Harmony patches
target. Since 1.12.0 a failed patch is logged instead of fatal
(`Loader.cs:27`), so check the log for `[ValheimTooler] Skipped Harmony patch`
rather than guessing in game.

**Timing.** Injection must happen after the game's Mono runtime is up. Inject
at the main menu, not during startup.

---

## Build and run

1. Set `ValheimPath` (step 3) to the local install.
2. Build `ValheimTooler` and `ValheimToolerInjector` as **Release | x64** in
   Visual Studio 2022, or `msbuild ValheimTooler.sln /p:Configuration=Release /p:Platform=x64`.
3. Put `ValheimTooler.dll` (merged), `ValheimToolerInjector.exe` and
   `SharpMonoInjector.dll` in any folder outside the game directory.
4. Start Valheim normally through Steam, wait for the main menu.
5. Run `ValheimToolerInjector.exe`.
6. Press `Del`.

Nothing is written to the Valheim directory at any point.

## Verification checklist

- [ ] Injector reports success and the process does not crash
- [ ] The window opens on `Del` at the main menu
- [ ] `Loader.RunPatches` logged no unexpected `Skipped Harmony patch` lines
- [ ] Config files appear under `LocalLow/IronGate/Valheim/ValheimTooler`
- [ ] `git status` in the game folder equivalent: Steam "verify integrity of
      game files" reports zero files replaced
