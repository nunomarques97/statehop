# Statehop

A native, local-first Windows app that observes how the PC is used and helps
prepare, restore and clean up work contexts (for example *Development* or
*Gaming*), with no cloud, no account and no telemetry by default.

Status: in development.

## Layout

| Project | Purpose |
|---|---|
| `src/Statehop.App` | WinUI 3 app (packaged as MSIX) |
| `src/Statehop.Core` | Sessions, normalisation and timeline logic |
| `src/Statehop.Observation` | Foreground, process and idle observation |
| `src/Statehop.Storage` | Local SQLite storage and retention |
| `tests/Statehop.Tests` | Tests |

Product principles are in [docs/PRODUCT.md](docs/PRODUCT.md), the visual system in
[DESIGN.md](DESIGN.md), and the packaging decision in
[docs/adr/001-packaging.md](docs/adr/001-packaging.md).

## Build and test

Requires Windows 11 and the .NET 10 SDK.

```powershell
dotnet build Statehop.slnx
dotnet test tests/Statehop.Tests
```
