# Clipboard Manager

A lightweight Windows clipboard history manager built with `.NET 8 + WPF`.

## Features

- Runs in the system tray
- Global hotkey to open the history panel
- Tracks clipboard text, images, files, and folders
- Preserves rich text clipboard payloads
- Alfred-inspired layout:
  - top search box
  - left history list
  - right preview panel
- Keyboard and hover-driven selection
- `clear` command support from the search box:
  - `clear`
  - `clear 5` (deletes front/newest 5 items, negative deletes back/oldest items)
  - `clear 1d`
- Local SQLite storage
- Image cache cleanup
- Retention policy and maximum history item cap
- Launch at startup
- Manual clear-history action in settings and tray
- Single-instance protection
- Custom app icon and publish script scaffold

## Project Structure

```text
src/
  ClipboardManager.App
  ClipboardManager.Application
  ClipboardManager.Domain
  ClipboardManager.Infrastructure
  ClipboardManager.Shared
scripts/
```

## Requirements

- Windows 10 / Windows 11
- .NET 8 SDK for local development

## Build

```powershell
dotnet restore .\ClipboardManager.sln
dotnet build .\ClipboardManager.sln
```

## Run

```powershell
dotnet run --project .\src\ClipboardManager.App\ClipboardManager.App.csproj
```

## Publish

Portable self-contained publish:

```powershell
.\scripts\publish.ps1
```

Default output:

```text
artifacts\publish\portable\win-x64
```

Portable self-contained single-file zip package:

```powershell
.\scripts\publish-zip.ps1
```

Default output:

```text
artifacts\publish\portable-single-file\win-x64
artifacts\publish\ClipboardManager-win-x64-Release-portable-single-file.zip
```

Optional arguments:

```powershell
.\scripts\publish.ps1 -Runtime win-arm64
.\scripts\publish.ps1 -SingleFile
.\scripts\publish-zip.ps1 -Runtime win-arm64
```

Note:

- `publish.ps1` defaults to self-contained portable publish
- `publish-zip.ps1` defaults to self-contained single-file publish and zip packaging

## Current App Notes

- Default hotkey is `Alt+C`
- Hotkey strings support combinations like `Alt+C`, `Ctrl+Shift+V`, and function keys like `Ctrl+Alt+F8`
- Clipboard history is deduplicated by content hash and type
- File copy history uses the Windows file drop list clipboard format and can be replayed back to the clipboard
- Image binaries are stored on disk, while metadata is stored in SQLite
- Rich text content is captured and replayed with HTML/RTF payloads when present
- File previews use real image thumbnails for image files and system icons for other files/folders
- Pasted items are moved to the top of the history list
- Search supports clear commands:
  - `clear` deletes all history
  - `clear 5` deletes the front/newest 5 items (negative `n` deletes back/oldest `n` items)
  - `clear 1d` deletes the last 24 hours
  - `clear 2d` deletes the last 2 days

## End User Usage

1. Download and extract the zip package.
2. Double-click `ClipboardManager.App.exe` to start the app.
3. After startup, the app stays in the system tray.
4. Press `Alt+C` to open the clipboard history panel.
5. Copy text, rich text, images, files, or folders as usual. They will appear in the history list automatically.
6. Select a record and press `Enter` to copy it again and paste it into the active window.
7. Press `Delete` to remove the selected history record.
8. Use the search box to search history, or enter commands such as `clear`, `clear 5` (deletes front 5 items, negative deletes back items), or `clear 1d`.
9. Right-click the tray icon for quick actions like open, settings, clear history, and exit.

Data is stored under:

```text
%LocalAppData%\ClipboardManager
```

## License

This project is licensed under the MIT License. See `LICENSE` for details.

## Possible Next Enhancements

- Auto-update support
- App blacklist / privacy exclusions
- Favorite / pin clipboard items
- OCR and richer search
