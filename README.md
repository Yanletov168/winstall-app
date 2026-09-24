# winstall

English | [Русский](README.ru.md)

winstall is a lightweight native Windows GUI for the Windows Package Manager (winget).
It stays a thin frontend: every package operation goes through the system-wide
winget, nothing is reimplemented. Built primarily for Windows 10, including 21H2;
Windows 11 and newer work through compatibility.

## Features

- Single checklist window: installed apps, with updatable ones pinned in bold on top.
- Checkbox semantics: updatable upgrades (↑), current uninstalls (×), new search
  results install (+). The glyph column always shows the pending action.
- Search across the whole winget catalog; three views: Installed, Updates, Search.
- Native display names and icons resolved from Uninstall registry entries.
- Details pane with publisher and description from `winget show`.
- Update-all in one click; package list export and import.
- Automatic reinstall offer when winget refuses an upgrade over changed
  installer technology (`0x8A15008E`).
- Optional update checker (logon autostart + wake task) with a count-only
  tray balloon; clicking it opens winstall.
- UI localizations via `locales/*.json` (English, Russian); a new language
  is one file, picked from the ☰ menu.
- Light, dark and system-following theme, also from the ☰ menu.

## Installation

### Installer

`Setup-winstall-x64.exe`, `Setup-winstall-x86.exe`, `Setup-winstall-arm64.exe`
from [Releases](https://github.com/Yanletov168/winstall-app/releases).
Per-user install, no UAC for the app itself.

At the end of setup the installer checks for a system-wide WinGet. If it is
already there, nothing else happens. If it is missing, setup asks for explicit
consent and then downloads the official App Installer from Microsoft and
installs it — that step shows its own UAC prompt. Errors are reported, never
hidden. Without WinGet the app cannot work: it never ships its own package
manager (see Compatibility).

### Portable

`winstall-portable.zip` from
[Releases](https://github.com/Yanletov168/winstall-app/releases):

```text
winstall-x86.exe
winstall-x64.exe
winstall-arm64.exe
locales/
  en.json
  ru.json
```

Pick the exe for the machine, no install needed. All three binaries share the
single `locales/` directory next to them; running without it falls back to
built-in English. A tiny framework-dependent build for machines without
.NET 8 is published separately as `winstall-portable-net48.zip` (needs the
in-box .NET Framework 4.8).

## Compatibility

Primary target: **Windows 10 21H2 and newer** (x64/x86). Windows 11 and newer
releases are supported through compatibility, not as the primary platform.
ARM64 builds target Windows 10/11 on ARM64. Installers refuse anything below
Windows 10. The .NET 8 builds are self-contained and carry their runtime;
the net48 build uses the in-box .NET Framework 4.8.

WinGet itself requires Windows 10 1809 or newer.

### Portable behavior / WinGet fallback

Portable builds need a system-wide WinGet. On first launch winstall probes
once (PATH plus the App Installer alias) and caches a working location in
`options.ini` (`winget_path` key, AppData fallback). A missing backend is
re-probed on every launch, so installing App Installer later just works, and
a backend that fails at runtime invalidates the cache and is re-detected once.
If no backend is found, the app offers to install the official App Installer
(the same offer the setup makes) and then exits either way: Yes opens the
download page, No just closes. Refusing means no app — winstall never runs
without a backend.

There is intentionally no bundled WinGet fallback: `winget.exe` alone does
not work — it needs the registered App Installer package (MSIX identity and
COM server), and running it outside the official distribution is unsupported
by Microsoft. Shipping a fake fallback was deliberately avoided; the
installer path above (download + register the official App Installer) is the
supported way to get WinGet on a machine that lacks it.

## Usage

- Check an updatable row to upgrade it, a current row to remove it
  (with confirmation), a search result to install it. Then `▶ Run`.
- `Update all (N)` runs a single `winget upgrade --all --silent`.
- The ☰ menu holds export/import, bulk selection, the update checker toggle,
  language selection and the winget log folder.
- `winstall.exe --check-updates` is the quiet mode used by the checker:
  no window, balloon on updates, silent exit otherwise.
- Settings live in `options.ini` next to the exe (UI language, cached backend
  path, installer log folder, per-verb flags). The ☰ menu edits language and
  log folder; the backend entry is maintained automatically.
- Policy flags are centralized, one set per verb, no hidden defaults:
  `upd_flags` (upgrade), `add_flags` (install), `rem_flags` (uninstall).
  Example: `upd_flags = --accept-package-agreements --accept-source-agreements --disable-interactivity --silent --include-unknown`.
  Read-only queries keep a fixed non-interactive pair so fresh machines don't
  hang on prompts.

## Building from source

.NET SDK 8 or newer. No runtime needs to be installed on the target machine.

```bat
dotnet build winstall.csproj -c Release
```

Self-contained per-architecture publish:

```bat
dotnet publish winstall.csproj -c Release -f net8.0-windows -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Installers need [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```bat
iscc /DMyArch=x64 /DMyVersion=1.1.0 /DSrcDir=..\dist\win-x64 /DOutDir=..\dist-installer installer\winstall.iss
```

Checks (table parsing, localizations, scheduler task cycle, backend cache):

```bat
dotnet run --project tests\parsetest\parsetest.csproj
```

## Technical notes

- winget speaks in tables; columns split on 2+ spaces. `winget list` ships in
  two layouts (with and without an Available column) and both are parsed.
- Apps installed outside winget show up as sourceless ARP rows; they are
  correlated with catalog entries by exact name and upgraded by the linked id.
- `uninstall` accepts no `--accept-package-agreements`; passing it makes
  winget print usage and exit `0x8A150002`.
- schtasks output is read in the console OEM code page; `/TR` quoting is
  argv-escaped so paths with spaces survive.
- Power users can pin the backend explicitly with the `WINSTALL_WINGET`
  environment variable (a path, or `none` to force the no-backend path).
- The update checker deliberately avoids admin rights: logon via the HKCU Run
  key, wake via an ONEVENT scheduler task (Power-Troubleshooter ID 1).
  ONLOGON triggers and XML task imports require elevation and are not used.

## License

MIT — see [LICENSE](LICENSE).

## Development

Development of this project was assisted by OpenCode with Meta Muse Spark 1.3.
