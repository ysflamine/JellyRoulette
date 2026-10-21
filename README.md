# 🎲 JellyRoulette

![CI](https://github.com/ysflamine/JellyRoulette/actions/workflows/build.yml/badge.svg)
![Jellyfin](https://img.shields.io/badge/Jellyfin-12.x%20%7C%2010.11.x-informational)
![.NET](https://img.shields.io/badge/.NET-10%20%7C%209-512BD4)
![Docs](https://img.shields.io/badge/docs-English%20%7C%20Espa%C3%B1ol-blue)

🇬 English · [🇪 Español](README.es.md)

> A **Jellyfin** plugin that adds a dice to the web header. Click it, choose a genre and media type, and a 3D roulette of posters spins until it stops on a title selected at random from everything matching your filters.
>
> For when the question is not “What should I watch?” but “I don’t know what to watch.”

<!-- Optional: add a screenshot or short demo GIF here, e.g. docs/jellyroulette-demo.gif -->

## Table of contents

- [Overview](#overview)
- [Features](#features)
- [Localization note](#localization-note)
- [Requirements](#requirements)
- [Installation](#installation)
  - [Catalog installation (recommended)](#catalog-installation-recommended)
  - [Manual installation](#manual-installation)
- [Updating](#updating)
- [Usage](#usage)
  - [Keyboard and mobile](#keyboard-and-mobile)
  - [Filters](#filters)
  - [Plugin settings](#plugin-settings)
- [Troubleshooting](#troubleshooting)
- [Uninstall](#uninstall)
- [Development](#development)
  - [Build from source](#build-from-source)
  - [Tests](#tests)
- [API](#api)
- [Support](#support)
- [Acknowledgements](#acknowledgements)

## Overview

JellyRoulette helps you discover something to watch from your own Jellyfin library. Instead of scrolling endlessly, you define a few filters and let the plugin pick a random title from the matching items.

## Features

- Random title picker based on your library filters.
- 3D poster roulette animation.
- Genre, media type, watched state and library filters.
- Header dice button plus keyboard shortcut: `Ctrl` / `⌘` + `Shift` + `J`.
- Mobile floating button fallback.
- Configurable spin duration, poster count, default filters and genre limit.
- REST API for the roulette logic and library metadata.
- Separate builds for Jellyfin 12.x and 10.11.x.

## Localization note

This README is available in English and Spanish. The current plugin UI uses Spanish labels such as **Girar**, **Reproducir** and **Ver ficha**. If you add English UI strings later, update this section or implement browser-language detection.

## Requirements

| Requirement | Details |
| --- | --- |
| **Jellyfin** | **12.x** (recommended) or **10.11.x** |
| **Browser** | Any modern browser on desktop or mobile |
| **Library** | At least one scanned library with movies and/or series |

Check your Jellyfin version in **Control Panel → General**, or run:

```bash
curl -s http://localhost:8096/System/Info/Public | grep Version
```

Replace `localhost:8096` with your Jellyfin address if needed.

## Installation

### Catalog installation (recommended)

This method allows the plugin to update from the Jellyfin admin panel.

1. Sign in to Jellyfin as an **administrator**.
2. Go to **Control Panel → Plugins → Repositories** and click **`+`**.
3. Fill in:
   - **Name:** `JellyRoulette`
   - **URL:** use the URL matching your Jellyfin version below.

**Jellyfin 12.x**

```text
https://raw.githubusercontent.com/ysflamine/JellyRoulette/main/manifest.json
```

**Jellyfin 10.11.x**

```text
https://raw.githubusercontent.com/ysflamine/JellyRoulette/main/manifest-10.11.json
```

4. Save, open the **Catalog** tab, search for **JellyRoulette** under the **General** category and click **Install**.
5. **Restart the server.**
6. Hard-refresh the web interface with `Ctrl + Shift + R`.

> [!NOTE]
> Why two URLs? Each Jellyfin branch requires a different build: .NET 10 for Jellyfin 12.x and .NET 9 for Jellyfin 10.11.x. The binaries are not interchangeable.

### Manual installation

1. Download the `.zip` for your branch from **Releases**:
   - Jellyfin **12.x** → `jellyroulette_12.zip`
   - Jellyfin **10.11.x** → `jellyroulette_10.11.zip`
2. Extract these two files into a **subfolder named `JellyRoulette`** inside the Jellyfin plugins folder:
   - `Jellyfin.Plugin.JellyRoulette.dll`
   - `meta.json`

| Installation type | Plugins path |
| --- | --- |
| Windows (installer or service) | `C:\ProgramData\Jellyfin\Server\plugins\JellyRoulette\` |
| Linux (package) | `/var/lib/jellyfin/plugins/JellyRoulette/` |
| Docker | `/config/plugins/JellyRoulette/` |
| macOS | `~/.local/share/jellyfin/plugins/JellyRoulette/` |

3. Restart the server and hard-refresh the web interface with `Ctrl + Shift + R`.

> [!WARNING]
> The DLL must be inside its own `JellyRoulette` subfolder, not directly under `plugins/`.

> [!WARNING]
> On Linux and Docker, check file permissions. If the Jellyfin process cannot read the DLL, it may ignore the plugin silently.

## Updating

- **Catalog installation:** update from **Control Panel → Plugins → Catalog**, restart the server and hard-refresh the web UI.
- **Manual installation:** download the correct `.zip` for your Jellyfin branch, replace `Jellyfin.Plugin.JellyRoulette.dll` and `meta.json` inside `plugins/JellyRoulette/`, restart the server and hard-refresh the web UI.

## Usage

### Keyboard and mobile

1. Click the **dice 🎲** in the header, or press `Ctrl` / `⌘` + `Shift` + `J`.
2. Adjust the filters.
3. Click **Spin** (`Girar` in the Spanish UI).
4. The roulette spins and stops on the selected title.
5. On the result card you can **Play** (`Reproducir`), **View details** (`Ver ficha`) or **Spin again**.
6. Press `Esc` to close.

On mobile, a **floating button** also appears in the bottom-right corner.

### Filters

| Filter | Options |
| --- | --- |
| **Genre** | Multi-select chips showing the number of titles per genre. Multiple genres are combined with **OR**. |
| **Type** | Movies · Series · Both |
| **State** | Only unwatched · All |
| **Library** | All libraries, or one specific library |

### Plugin settings

Go to **Control Panel → Plugins → JellyRoulette → Settings**.

| Setting | Purpose |
| --- | --- |
| Show JellyRoulette in the web interface | Disables the injected header button. The API keeps working. |
| Floating button | `Automatic` (mobile or if the header button fails) · `Always` · `Never` |
| Default media type | Filter used when the roulette opens |
| Filter “only unwatched” by default | Same as above, for watched state |
| Spin duration (ms) | `1600`–`9000`. Higher values feel more dramatic |
| Posters in the roulette | Number of candidate posters loaded per spin (`8`–`60`) |
| Maximum genres | Shows first the genres with more titles |

Settings changes apply after reloading the web interface. A server restart is not required.

## Troubleshooting

| Symptom | Solution |
| --- | --- |
| The plugin does not appear in **Control Panel → Plugins** | The DLL must be in `plugins/JellyRoulette/`, not directly in `plugins/`. Check read permissions. |
| The plugin appears, but there is no dice in the header | Hard-refresh with `Ctrl + Shift + R`. If it still does not appear, set **Floating button → Always** in the plugin settings. |
| Plugin status is **Malfunctioned** | You are using the DLL built for the other Jellyfin branch. Download the correct `.zip`. |
| The roulette is empty (for example, “Nada por aquí…”) | No title matches those filters. Remove some genres or disable **Only unwatched**. |
| The roulette is empty and no genres are shown | The library is not scanned or has no metadata. Go to **Control Panel → Libraries → Scan all**. |
| Posters appear as gray boxes | The title has no **Primary** image. Add a poster in Jellyfin. |
| Catalog installation fails | Check the server log and search for `JellyRoulette`, `checksum` or `Only zip`. |
| The old version is still loaded after updating | Hard-refresh with `Ctrl + Shift + R`. The browser may cache `index.html`. |

**Where is the log?**

| System | Log location |
| --- | --- |
| Windows | `C:\ProgramData\Jellyfin\Server\log\log_YYYYMMDD.log` |
| Linux | `/var/log/jellyfin/log_YYYYMMDD.log` |
| Docker | `docker logs jellyfin --tail 100` |

## Uninstall

Use **Control Panel → Plugins → JellyRoulette → Uninstall**, then restart the server.

Manual removal:

1. Delete the folder `plugins/JellyRoulette/`.
2. Restart Jellyfin.
3. Optional: delete the settings file from `configurations/JellyRoulette_<guid>.xml` if you want a completely fresh start. The GUID part may vary.

## Development

### Build from source

| Jellyfin version | .NET SDK | Command |
| --- | --- | --- |
| 12.x | 10.0.x | `dotnet publish Jellyfin.Plugin.JellyRoulette -c Release -o publish` |
| 10.11.x | 9.0.x | `dotnet publish Jellyfin.Plugin.JellyRoulette -c Release -o publish-jf10 -p:JellyfinTarget=jf10` |

Copy `Jellyfin.Plugin.JellyRoulette.dll` and `meta.json` to the plugin folder and restart Jellyfin. The `.pdb`, `.xml` and `.deps.json` files are not required.

### Tests

```bash
dotnet test tests/JellyRoulette.Tests        # backend tests
cd tests/frontend && npm install && npm test # frontend tests (jsdom)
```

The backend currently has 41 tests, but the exact count may change over time.

## API

All data routes require an authenticated Jellyfin session. Send the header:

```http
Authorization: MediaBrowser Token="YOUR_TOKEN"
```

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/JellyRoulette/Spin` | Picks a random title and returns posters for the animation |
| `GET` | `/JellyRoulette/Genres` | Returns library genres with title counts |
| `GET` | `/JellyRoulette/Libraries` | Returns server libraries |
| `GET` | `/JellyRoulette/Info` | Returns plugin version and active settings |

### `/Spin` query parameters

| Parameter | Type | Notes |
| --- | --- | --- |
| `mediaType` | `movie`, `series` or `both` | Media type filter |
| `unwatchedOnly` | boolean | Use `true` to restrict to unwatched items |
| `libraryId` | GUID | Optional library id |
| `genres` | comma-separated strings | Use internal genre names, for example `Horror,Comedy` |
| `candidates` | integer | Number of candidate posters, normally `1`–`60`; the UI setting uses `8`–`60` |

Example:

```http
GET /JellyRoulette/Spin?mediaType=movie&unwatchedOnly=true&genres=Horror,Comedy&candidates=24
```

## Support

If something does not work, open an issue with:

- Jellyfin version.
- JellyRoulette version.
- Installation method: catalog or manual.
- Operating system and deployment type: Windows, Linux, Docker, macOS.
- Browser and whether you are using desktop or mobile.
- Relevant server log lines.
- Steps to reproduce the problem.
- Expected behavior and actual behavior.

Pull requests are welcome.

## Acknowledgements

Built for [Jellyfin](https://jellyfin.org/). Thanks to the Jellyfin project and community.
