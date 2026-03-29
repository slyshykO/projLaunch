# projlaunch

`projlaunch` is a Tauri desktop app for keeping a small catalog of development projects and opening them quickly in your preferred IDE.

The current app is built with:

- F# + Fable for the frontend application logic
- Elmish + Feliz + Feliz.DaisyUI for the UI
- Tauri 2 + Rust for desktop integration and process launching
- Vite for the frontend dev/build pipeline
- Paket for .NET package management

## What The App Does

The app currently lets you:

- view saved projects in a grid sorted by `lastOpened`
- add a project from the UI with name, description, IDE, and solution/workspace path
- open projects in:
  - VS Code
  - Visual Studio 2022
  - Visual Studio 2026
  - Rider
- launch VS Code projects through remote targets:
  - SSH
  - WSL
- persist window state between launches
- inspect app/runtime details on the `About` page

When a project is opened, the app updates its `lastOpened` value and rewrites the stored project record.

## Current Limitations

- The add-project dialog does not expose editing or deleting existing entries yet.
- Remote launch options are only wired up for VS Code.
- Visual Studio launch paths are currently hard-coded for Windows installs.
- The test project exists, but it only contains a placeholder test right now.
- The app expects its Tauri app-data `projects` directory to exist when reading saved projects.

## Project Storage

Saved projects are stored as individual JSON files in the Tauri app data directory under `projects/`.

Each record includes:

- `id`
- `name`
- `lastOpened`
- `description`
- `path`
- `ide`
- `environment`
- `remote`

`remote` supports either:

- `Ssh { host, username }`
- `Wsl "<distro>"`

## Repository Layout

- `src/` - F# frontend application code compiled with Fable
- `src-tauri/` - Rust/Tauri host application and native commands
- `src-test/` - xUnit test project
- `build/` - custom F# build helper used by `build.bat`
- `serde-probe-lib/` - local shared/library code referenced by the app

## Requirements

For local development, this repo expects:

- Node.js + npm
- .NET SDK 10
- Rust toolchain
- Tauri prerequisites for your platform

On Windows, you will also want the IDEs you plan to launch installed and available on disk/path.

## Development

Install/restore dependencies as needed:

```powershell
npm install
dotnet tool restore
dotnet paket restore
```

Start the frontend + Tauri dev workflow:

```powershell
npm run tauri dev
```

Useful npm scripts:

```powershell
npm run dev
npm run start
npm run build
```

Notes:

- `npm run dev` starts Fable in watch mode and runs Vite in development mode.
- `npm run start` is a lighter watch/serve variant.
- `npm run build` compiles the frontend into `dist/`.

## Build And Maintenance

The repo also includes a custom Windows helper:

```powershell
.\build.bat build
.\build.bat clean
.\build.bat update
.\build.bat build-daisyui
.\build.bat help
```

What these do:

- `build` syncs Feliz.DaisyUI source references in `src/index.css` and then runs `npm run tauri build`
- `clean` runs `cargo clean` for the Tauri app
- `update` runs npm/cargo update checks
- `build-daisyui` builds a local `Feliz.DaisyUI` checkout if present

## Tests

Run tests with:

```powershell
dotnet test .\src-test\src-test.fsproj
```

At the moment, the test suite is only a placeholder and does not cover the launcher behavior yet.
