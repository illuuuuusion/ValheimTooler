# Ponytail Audit — Over-Engineering Plan

Repo-wide scan for over-engineering. Scope is complexity only: correctness,
security and performance were explicitly out of scope and belong in a separate
review pass.

Baseline: 8769 lines of C# across three projects
(`ValheimTooler` 6309, `ValheimToolerLauncher` 2321, `ValheimToolerMod` 139).

**Potential: -4150 lines, -7 dependencies.**

Findings are ranked biggest cut first. Tags: `delete` (dead code),
`native` (platform/host already ships it), `stdlib` (standard library ships it),
`yagni` (abstraction with no second caller), `shrink` (same logic, fewer lines).

---

## P1 — `delete:` the entire launcher project

**Cut:** `ValheimToolerLauncher/` — 2321 lines, 6 NuGet packages
(`Autoupdater.NET.Official`, `HandyControls`, `Microsoft.Web.WebView2`,
`Microsoft-WindowsAPICodePack-Core`, `Microsoft-WindowsAPICodePack-Shell`,
`Newtonsoft.Json`), plus the WPF/XAML surface.

**Why:** The fork installs through BepInEx by copying three DLLs. The README
states the launcher does not work on Valheim 1.0. It is also the only component
in the repo that makes a network call — `AutoUpdater.Start("https://www.codexus.fr/valheimtooler/AutoUpdater.xml")`
at `ViewModels/MainWindowViewModel.cs:179`, a third-party domain.

The single largest file in the repo lives here: `Utils/KeyValue.cs`, 1037 lines
of hand-rolled Steam VDF/ACF parsing, used only to locate the game directory.

**Keep:** `Utils/MonoInjector.cs` (81 lines) and `SharpMonoInjector.dll` if the
injection path is pursued — see `no-bepinex-plan.md`. Everything else goes.

**Replacement:** nothing.
**Effort:** low (delete directory, drop project from `ValheimTooler.sln:13`).
**Risk:** low.

---

## P2 — `native:` drop the vendored BepInEx configuration system

**Cut:** `ValheimTooler/Configuration/` — 1710 lines across 12 files.

**Why:** It is a verbatim copy of `BepInEx.Configuration`. The files say so
themselves: `// Class originally created by BepInEx: https://github.com/BepInEx/BepInEx`
appears in 8 of them. BepInEx is now a hard prerequisite, already referenced by
`ValheimToolerMod` (AssemblyRef `BepInEx 5.4.23.5`), and already loaded in the
process at runtime.

**Replacement:** `using BepInEx.Configuration;` and delete the folder.
**Effort:** medium (rewire `Utils/ConfigManager.cs`, `UI/Utils.cs`).
**Risk:** low, but see the conflict note in `final-plan.md` — this finding is
void if the project drops BepInEx.

---

## P3 — `native:` drop the SharpConfig dependency

**Cut:** the `SharpConfig` package and the third shipped DLL.

**Why:** Exactly one call site — `Utils/Translator.cs:154`,
`SharpConfig.Configuration.LoadFromString(translationFileContents)`, parsing
`Resources/Localization/translations.cfg`.

**Replacement:** BepInEx's config reader, or ~10 lines of `string.Split` over
the INI-shaped translation file.
**Effort:** low.
**Risk:** low. Cuts one file from every install.

---

## P4 — `yagni:` remove the `config_vt.path` handshake

**Cut:** `ValheimToolerMod/ValheimToolerMod.cs:51` (`File.WriteAllText`) and
`ValheimTooler/Utils/ConfigManager.cs:88` (`File.ReadAllText`) plus surrounding
fallback handling.

**Why:** The mod writes a file into the game directory purely to tell the
tooler DLL where the config lives. Both run in the same process, and since
1.12.0 the mod has a `ProjectReference` to `ValheimTooler` — it can just assign
the value.

**Replacement:** `ConfigManager.s_configurationPath = Paths.ConfigPath;` before
`CallLoaderInit()`.
**Effort:** low.
**Risk:** low. Also stops writing a stray file into the game folder.

---

## P5 — `yagni:` trim `TomlTypeConverter`

**Cut:** most of `ValheimTooler/Configuration/TomlTypeConverter.cs` — 393 lines.

**Why:** It covers the full Toml type matrix. The tool binds five types:
`bool`, `string`, `float`, `Vector2`, `KeyboardShortcut`.

**Replacement:** those five converters.
**Effort:** low.
**Risk:** low. Subsumed by P2 if P2 is taken.

---

## P6 — `stdlib:` drop `ReflectionExtensions`

**Cut:** `ValheimTooler/Utils/ReflectionExtensions.cs` — 43 lines
(`GetFieldValue`, `SetFieldValue`, `CallMethod`, `CallStaticMethod`).

**Why:** HarmonyX ships `AccessTools.Field` / `AccessTools.Method` and
`Traverse`, which do the same with caching. HarmonyX is already a dependency and
`AccessTools` is already used in 9 places in this repo.

**Replacement:** `Traverse.Create(obj).Field(name).GetValue<T>()` and
`AccessTools.Method(...)`.
**Effort:** medium (~15 call sites).
**Risk:** low.

---

## P7 — `yagni:` remove `ConfigWrapper<T>`

**Cut:** `ValheimTooler/Configuration/ConfigWrapper.cs` (53 lines) and
`ConfigFile.Wrap<T>` (`ConfigFile.cs:344-358`).

**Why:** Zero callers outside the `Configuration/` folder itself. Dead API
surface inherited from the BepInEx copy.

**Replacement:** nothing.
**Effort:** trivial.
**Risk:** none. Subsumed by P2.

---

## P8 — `yagni:` remove `AcceptableValueRange<T>`

**Cut:** `ValheimTooler/Configuration/AcceptableValueRange.cs` — 60 lines.

**Why:** Zero usages anywhere. Only `AcceptableValueList` is used, once, at
`Utils/ConfigManager.cs:81`.

**Replacement:** nothing.
**Effort:** trivial.
**Risk:** none. Subsumed by P2.

---

## P9 — `yagni:` collapse the two config files into one

**Cut:** the `s_internalFile` instance at `Utils/ConfigManager.cs:77`.

**Why:** Two `ConfigFile` objects and two files on disk
(`valheimtooler_settings.cfg`, `valheimtooler_internal.cfg`) for five internal
values: window positions, permanent pins, ESP radius toggle and value.

**Replacement:** one file, `[Internal]` section.
**Effort:** low.
**Risk:** low — existing users lose their saved window positions once.

---

## P10 — `native:` use Unity's built-in gray texture

**Cut:** `ValheimTooler/Core/ItemGiver.cs:82-93` — allocates a `Color[1024]`,
fills it in a loop, calls `SetPixels` and `Apply`.

**Replacement:** `Texture2D.grayTexture`.
**Effort:** trivial.
**Risk:** none.

---

## P11 — `stdlib:` replace the hand-rolled stream copy

**Cut:** `ValheimTooler/Utils/ResourceUtils.cs:15-29` — a 16 KB buffer loop.

**Replacement:** `input.CopyTo(ms)`.
**Effort:** trivial.
**Risk:** none.

---

## P12 — `shrink:` LINQ long form

**Cut:** `ValheimTooler/Utils/ResourceUtils.cs:37` —
`Enumerable.Single<string>(containingAssembly.GetManifestResourceNames(), (string str) => str.EndsWith(resourceFileName))`.

**Replacement:** `containingAssembly.GetManifestResourceNames().Single(n => n.EndsWith(resourceFileName))`.
**Effort:** trivial.
**Risk:** none.

---

## Out of scope but worth noting

Not over-engineering, so not ranked above, but it blocks anyone else building
the project: both `.csproj` files hardcode the fork author's absolute paths,
e.g. `F:\SteamLibrary\steamapps\common\Valheim\valheim_Data\Managed\assembly_valheim.dll`.
`ValheimToolerMod/Libs/` with its `.gitkeep` exists for exactly this and is now
unused. Fix is a single `$(ValheimPath)` MSBuild property — see
`no-bepinex-plan.md` step 3.
