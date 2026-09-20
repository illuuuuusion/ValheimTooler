# Final Plan — Combining the Audit and the No-BepInEx Work

Merges `ponytail-plan.md` (over-engineering cleanup) and `no-bepinex-plan.md`
(Mono injection instead of BepInEx) into one ordered sequence.

They are not independent. One audit finding is cancelled by the no-BepInEx
decision, two are promoted from "nice" to "required", and several become more
valuable because every line removed is a smaller injected payload. Read this
file rather than the two source plans when deciding what to do first.

---

## The decision that drives everything

**`ValheimTooler.dll` must work without BepInEx being present.**

`ValheimToolerMod.dll` (139 lines) stays, so the BepInEx path keeps working for
anyone who wants it. But the core assembly can no longer *depend* on BepInEx
being loaded.

### Consequences for the audit findings

| Audit | Finding | Effect of going BepInEx-free |
| --- | --- | --- |
| P1 | delete launcher | **Modified** — becomes "replace", keep `MonoInjector.cs` + `SharpMonoInjector.dll` |
| P2 | use `BepInEx.Configuration` | **Void** — cannot depend on BepInEx. Costs 1710 lines of savings |
| P3 | drop SharpConfig | **Promoted** — one fewer assembly to merge into the payload |
| P4 | remove `config_vt.path` | **Required** — nothing writes that file without BepInEx |
| P5, P7, P8 | trim the config system | **Promoted** — were "subsumed by P2", now standalone and payload-relevant |
| P6, P9–P12 | cleanups | Unchanged |
| (out of scope) | hardcoded `F:\` paths | **Required** — blocks any build |

Going BepInEx-free costs about 1710 lines of otherwise available savings. That
is the price of the requirement, stated once so it is not rediscovered later.

**Revised target: -2835 lines, -7 runtime dependencies**
(6 launcher packages + SharpConfig), +1 build-time only (ILRepack).

---

## Phase 0 — Unblock the build

Nothing else can be verified until the projects compile on this machine.

1. Introduce `$(ValheimPath)` / `$(ValheimManaged)` and replace every hardcoded
   `F:\SteamLibrary\...` HintPath in `ValheimTooler.csproj` and
   `ValheimToolerMod.csproj`. — `no-bepinex-plan.md` step 3
2. Confirm a clean `Release | x64` build of `ValheimTooler` before touching
   anything else.

**Gate:** the unmodified source builds. Do not proceed otherwise — every later
failure would be ambiguous.

## Phase 1 — Make it run without BepInEx

This phase is the point of the exercise. It also lands audit P1 and P4.

3. Add the `ValheimToolerInjector` console project; delete
   `ValheimToolerLauncher/` except `Utils/MonoInjector.cs` and
   `SharpMonoInjector.dll`; drop the launcher from `ValheimTooler.sln:13`.
   — step 1 / audit P1
4. Move the config path to `LocalLow/IronGate/Valheim/ValheimTooler` and remove
   the `config_vt.path` handshake on both sides. — step 4 / audit P4
5. Add the ILRepack post-build target, without `/internalize`. — step 2

**Gate — first real test, and the one that can invalidate the approach:**
inject at the main menu and press `Del`. If `Loader.Init()` throws a Unity
main-thread exception, apply the bootstrap fix from the risks section of
`no-bepinex-plan.md` before continuing. Everything after this phase is cleanup;
if this gate fails, none of it matters yet.

## Phase 2 — Shrink the payload

Only worth doing once Phase 1 works. Every line here comes out of the assembly
that gets injected, so the motivation is no longer aesthetic.

6. Replace SharpConfig with a small parser in `Utils/Translator.cs:154` — drops
   one assembly from the merge set, leaving seven. — audit P3
7. Delete `ConfigWrapper<T>` + `ConfigFile.Wrap<T>` (zero callers) and
   `AcceptableValueRange<T>` (zero usages). — audit P7, P8
8. Trim `TomlTypeConverter` to the five types actually bound. — audit P5
9. Collapse `s_internalFile` into the settings file. — audit P9

Order matters: 7 before 8, since removing the dead wrappers shrinks the surface
the converter trim has to keep working against.

## Phase 3 — Polish

Independent of everything above. Safe to do any time, or never.

10. `ReflectionExtensions` → Harmony `AccessTools` / `Traverse`. Harmony is
    already merged into the payload, so this adds nothing. — audit P6
11. `Texture2D.grayTexture` in `ItemGiver.cs:82-93`. — audit P10
12. `input.CopyTo(ms)` and the LINQ long form in `ResourceUtils.cs`.
    — audit P11, P12

---

## What not to do

- **Do not** take audit P2. It is void under this plan. If BepInEx ever becomes
  a hard requirement again, revisit it — it is the single largest remaining cut.
- **Do not** delete `ValheimToolerMod/`. It is 139 lines and keeps the BepInEx
  install path alive for free.
- **Do not** reorder Phase 1 ahead of Phase 0, or Phase 2 ahead of the Phase 1
  gate. Both gates exist to keep failures attributable.

## Open question

Whether the ILRepack merge survives MonoMod's runtime detour generation is
untested. If it does not, the fallback (ship the eight DLLs beside the injector,
resolve them with an `AssemblyResolve` handler) still meets the "nothing in the
game folder" requirement and costs roughly 20 lines. Decide at step 5, not
before.
