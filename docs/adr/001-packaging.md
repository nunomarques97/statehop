# ADR 001 — Packaged (MSIX) vs unpackaged

**Date:** 27–28 Aug 2026 · **Blocking point:** B0.1

## Context

The Microsoft Store is the only viable signed distribution route at no cost
(€0, no SmartScreen), and the Store requires MSIX. But MSIX brings package
identity and a "Desktop Bridge" model that is historically confused with
UWP/AppContainer sandboxing. The decision therefore has to rest on evidence,
not assumption, because the product depends on enumerating third-party
processes, reading the foreground window, detecting idle time and (later)
closing processes.

## Method

Official documentation (Microsoft Learn) **plus a minimal empirical test** on a
development machine (Windows 11 Home, build 26200, non-elevated session):

1. A .NET 10 console app (`PkgSpike`, outside the repository, disposable) that
   runs five checks through plain P/Invoke (no WinUI, to isolate the effect of
   packaging from the UI stack):
   - Package identity (`Windows.ApplicationModel.Package.Current`).
   - Process enumeration (`Process.GetProcesses()` + each one's
     `MainModule.FileName`).
   - Foreground window (`GetForegroundWindow` +
     `GetWindowThreadProcessId`).
   - Idle detection (`GetLastInputInfo`).
   - Global hotkey (`RegisterHotKey` on a message-only window).
   - `OpenProcess(PROCESS_TERMINATE, …)` on an own process (Notepad launched by
     the app itself) and on a SYSTEM process (`services.exe`), without actually
     killing anything.
2. Run **A — unpackaged**: `dotnet build` + running the `.exe` directly.
3. Run **B — packaged**: manual MSIX packaging (`AppxManifest.xml` with
   `rescap:Capability Name="runFullTrust"`,
   `EntryPoint="Windows.FullTrustApplication"`), signed with a self-signed test
   certificate (created and removed at the end of the test), installed with
   `Add-AppxPackage` with Windows Developer Mode enabled for the purpose (it is
   a system change, not just an SDK install), and run directly from
   `C:\Program Files\WindowsApps\<PackageFullName>\PkgSpike.exe`.
4. Test package and certificate **removed at the end** of the test
   (`Remove-AppxPackage`, certificate removed from
   `Cert:\LocalMachine\TrustedPeople` and `Cert:\CurrentUser\My`). None of it
   stays installed on the machine.

## Results (side by side)

| Check | Unpackaged | Packaged (MSIX, full trust) |
|---|---|---|
| `Package.Current` resolves | ❌ (`InvalidOperationException`, expected) | ✅ `Statehop.PkgSpike_1.0.0.0_x64__x44vs2hqd2ayr` |
| Processes enumerated / `MainModule` accessible | 349 / 329 accessible | 332 / 312 accessible |
| `MainModule` denied | 20 — all SYSTEM/protected processes (Idle, System, Secure System, Registry, smss, csrss, wininit) | 20 — **exactly the same set** (Idle, System, Secure System, Registry, smss, csrss, wininit) |
| Foreground window | ✅ correct title+pid+name | ✅ identical |
| Idle detection (`GetLastInputInfo`) | ✅ works | ✅ works |
| Global hotkey (`RegisterHotKey`) | ✅ registers and unregisters without error | ✅ identical |
| `OpenProcess(PROCESS_TERMINATE)` on own process (child Notepad) | ✅ allowed | ✅ allowed |
| `OpenProcess(PROCESS_TERMINATE)` on `services.exe` (SYSTEM) | ❌ denied | ❌ denied — **same result** |

(The total process count differs slightly between the two runs, 349 vs 332,
because they were not run at the same time, not because of packaging; the
access/denial pattern is identical.)

**Empirical conclusion: there is no observable functional difference between a
packaged (full trust) and an unpackaged app for any of the five capabilities
tested.** The only restriction found (access denied to SYSTEM/protected
processes) happens **equally in both modes**: it is a matter of the target
process's integrity level/ACL (relevant to B0.2, not B0.1), not a restriction
imposed by MSIX.

## Answers to the questions

**1. Can a packaged (MSIX) WinUI 3 app enumerate third-party processes, read
the foreground window, detect idle time and register global hotkeys without
elevated privileges?**

Yes, confirmed empirically above. The underlying reason (official
documentation): an MSIX package declared `runFullTrust` runs as a normal Win32
process at **medium** integrity level. This is the "Desktop Bridge" model,
distinct from the AppContainer sandboxing used by restricted UWP apps.
Full-trust apps are not isolated by AppContainer and keep direct access to most
system resources, just like an unpackaged app of the same user
([MSIX containerization overview](https://learn.microsoft.com/en-us/windows/msix/msix-containerization-overview),
[Understanding how packaged desktop apps run on Windows](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)).
The common confusion ("MSIX = sandboxed") applies to AppContainer apps, not to
Statehop's case.

**2. How does starting with Windows work in each model?**

- **Unpackaged:** the classic mechanism, an `HKCU\...\Run` registry key or a
  shortcut in the user's Startup folder.
- **Packaged (MSIX):** those two mechanisms **stop working** for packaged apps.
  The correct model is the `StartupTask` extension declared in
  `AppxManifest.xml`
  (`<desktop:Extension><desktop:StartupTask TaskId="..." Enabled="..."
  DisplayName="..." /></desktop:Extension>`), available since the Windows 10
  Anniversary Update for Desktop Bridge apps. The user sees and controls it in
  Task Manager → "Startup" tab, like any other modern app
  ([StartupTask Class](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask?view=winrt-26100),
  [Supporting "launch at startup" in a desktop app converted with the Desktop Bridge](https://learn.microsoft.com/en-us/archive/blogs/appconsult/supporting-launch-at-startup-in-a-desktop-app-converted-with-the-desktop-bridge)).
  This is an implementation change, not a blocker.

**3. What restrictions exist on closing third-party processes from a packaged
app?**

No *additional* restriction imposed by packaging itself, beyond what already
applies to any non-elevated Win32 process: processes running at a higher
integrity level (SYSTEM, protected) cannot be opened/terminated without
elevation, confirmed empirically to be the same in both modes. This is the scope
of B0.2, to be measured with real apps (how many fall into elevated processes).

**4. Recommendation**

**Use MSIX (packaged) from the start.** Explicit trade-offs:

- **For:** a direct route to the Microsoft Store (free signing, no
  SmartScreen); no loss of technical capability found in the tests above;
  package identity gives access to useful APIs later (e.g. `ApplicationData`
  for isolated storage, if wanted).
- **Real costs, not blockers:**
  - Starting with Windows requires `StartupTask` instead of the simple registry
    key: more manifest code, but well documented.
  - Local iteration during development requires Developer Mode (or a signed
    package) for sideloading. This is irrelevant in normal use through
    `dotnet run`/F5 in Visual Studio (which handles it automatically through
    *single-project MSIX*
    ([Package your app using single-project MSIX](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix))),
    and only relevant for test scripts like the one in this ADR.
  - B0.4 (antivirus false positives) was not tested here: the test binary was
    neither distributed nor run on another machine. An AV false positive
    depends more on reputation/signing than on packaged vs unpackaged, and
    Store signing helps here too.
- **No evidence was found that MSIX makes any essential functionality
  impossible.** There is no reason to reopen this decision without new data.

## Sources

- [MSIX containerization overview — Microsoft Learn](https://learn.microsoft.com/en-us/windows/msix/msix-containerization-overview)
- [Understanding how packaged desktop apps run on Windows — Microsoft Learn](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-behind-the-scenes)
- [MSIX AppContainer apps — Microsoft Learn](https://learn.microsoft.com/en-us/windows/msix/msix-container)
- [StartupTask Class — Microsoft Learn](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask?view=winrt-26100)
- [Supporting "launch at startup" in a desktop app converted with the Desktop Bridge — Microsoft Learn](https://learn.microsoft.com/en-us/archive/blogs/appconsult/supporting-launch-at-startup-in-a-desktop-app-converted-with-the-desktop-bridge)
- [Package your app using single-project MSIX — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/single-project-msix)
- [Windows App SDK deployment guide for framework-dependent packaged apps — Microsoft Learn](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deploy-packaged-apps)
- Empirical test: `PkgSpike` (not versioned; raw results quoted in the table
  above, captured on 27–28 Aug 2026).

## Consequences

- `Statehop.App` is created as a WinUI 3 project with *single-project MSIX
  packaging* enabled by default (`WindowsPackageType=Desktop`,
  `Package.appxmanifest` present), not `None`.
- The app must implement starting with Windows through `StartupTask`, not
  through the registry or the Startup folder.
- B0.2 (elevated processes) still has to be measured with real use. This ADR
  only shows that the *cause* of that restriction is not packaging.
- Windows Developer Mode was enabled on the development machine to allow the
  test; it stays on. It is a normal prerequisite for local WinUI 3/MSIX
  development, not something to revert.
