# NaraNote

NaraNote is a lightweight sticky-note application for Windows. It opens directly into independent note windows instead of a traditional main window, keeps note data on the local machine, and restores open notes across sessions.

Version 1.0 targets Windows 10 22H2 or later and Windows 11 on x64 systems. The release is distributed as one self-contained `NaraNote.exe`; the .NET runtime does not need to be installed separately.

## Table of contents

- [Highlights](#highlights)
- [Notes and windows](#notes-and-windows)
- [Editing and objects](#editing-and-objects)
  - [Text and syntax highlighting](#text-and-syntax-highlighting)
  - [Images](#images)
  - [Attachments and dropped files](#attachments-and-dropped-files)
  - [Drawing](#drawing)
- [Reminders](#reminders)
- [Language support](#language-support)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Saving and `.naranote` files](#saving-and-naranote-files)
- [System tray and startup](#system-tray-and-startup)
- [Data and privacy](#data-and-privacy)
- [Build from source](#build-from-source)
- [Project structure](#project-structure)
- [NuGet packages](#nuget-packages)
- [Current limitations](#current-limitations)
- [License](#license)

## Highlights

- Independent frameless sticky-note windows with native rounded corners and Windows shadows
- Automatic local persistence with debounced writes, atomic replacement, and backup recovery
- Text, images, captions, file attachments, and ink strokes on the same note surface
- Mouse, stylus, tablet pressure, straight-line drawing, erasing, and scribble-to-erase
- Per-note undo and redo for objects, ink, captions, movement, and resizing
- Reminders with one-time, daily, weekly, and selected-weekday schedules
- Optional reminder auto-hide, alarm sound, foreground activation, and shake animation
- System tray controls, configurable global shortcuts, and current-user startup registration
- Syntax highlighting and automatic language detection for common source and markup formats
- English, French, Spanish, Vietnamese, and Korean UI localization
- No network access, telemetry, cloud account, or automatic update service

## Notes and windows

Each note is a separate Windows window with its own position, size, color, font, content, drawing data, reminder, and always-on-top state.

- Drag the empty header area to move a note.
- Drag any edge or corner with a mouse or pen to resize it.
- Use `+` or `Ctrl+N` to create a note beside the current or last active note.
- Use the pin button to toggle always-on-top.
- Use `X` to close only the current note. Closing is different from deleting.
- Choose **Hide note** from the note menu to hide one note without closing it.
- Hidden notes remain available from the tray menu's **Show all** command.
- New and restored windows are corrected to remain inside an available monitor work area.

## Editing and objects

NaraNote uses a document surface rather than a plain WPF `TextBox`.

### Text and syntax highlighting

- Unicode text editing with Korean IME composition support
- Automatic syntax detection after paste and line entry
- Manual syntax selection disables automatic detection for that note
- Highlighting for C#, C/C++, Python, Lua, JSON, XML, HTML, JavaScript, CSS, Markdown, and PowerShell
- Font family, font size, and note color settings

### Images

- Paste an image from the clipboard or drop an image file from Explorer.
- Move images and resize them from corner handles while preserving aspect ratio.
- Images are constrained so they do not become completely inaccessible outside a note.
- Double-click an image to add a caption.
- Click a caption to edit it inline.
- Captions follow the note font and move with their image.

### Attachments and dropped files

- Supported text files are inserted at the text cursor.
- Unknown files are classified by content as text or binary when possible.
- Image files become image objects.
- Other files become movable, eight-direction resizable attachment cards.
- Attachments open only after an explicit double-click using the Windows default application.
- Missing attachments show a warning instead of terminating the app.

### Drawing

- Text, pen, and stroke-eraser modes
- Mouse, stylus, and tablet input
- Stylus pressure support when pressure data is available
- Seven pen colors and five preset thicknesses
- Adjustable pen thickness with `Ctrl++`, `Ctrl+-`, and `Ctrl+0` while in pen mode
- Hold `Shift` in pen mode to draw connected straight segments
- The note expands when drawing reaches an edge
- A fast horizontal scribble over existing ink removes the intersected strokes as one undo operation
- `Esc` returns from pen mode to text mode

## Reminders

Open the note menu and choose **Reminder...** to configure a reminder.

- One-time reminder
- Daily at the same time
- Weekly at the same time
- Selected weekdays at the same time
- 12-hour or 24-hour time entry
- Optional automatic hiding until the next reminder
- Reminder indicator in the note header with remaining time in its tooltip
- Alarm sound, foreground activation, and continuous shake animation until `Esc`

The default alarm is:

```text
C:\Windows\Media\Alarm01.wav
```

Choose another WAV file from **Settings**. For a one-time reminder, pressing `Esc` dismisses the reminder and leaves the note visible. For a repeating auto-hidden reminder, pressing `Esc` hides the note again until its next scheduled time.

## Language support

NaraNote includes:

- English
- French
- Spanish
- Vietnamese
- Korean

The default is **System default**. If the Windows UI language is unsupported, NaraNote uses English. A language can also be selected explicitly as the first item under **General settings**. Menu, dialog, tooltip, validation, and error text use the selected language.

## Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| `Ctrl+N` | Create a new note |
| `Ctrl+S` | Save/export the current note |
| `Ctrl+Shift+S` | Save/export with a new name |
| `Ctrl+V` | Paste image, files, or text |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` / `Ctrl+Shift+Z` | Redo |
| `Ctrl++` / `Ctrl+-` | Change font size, or pen thickness in pen mode |
| `Ctrl+0` | Restore default font size, or pen thickness in pen mode |
| `Delete` | Delete the selected object |
| `Esc` | Clear selection, leave drawing mode, or dismiss an active reminder |
| `F10` | Open the note menu |

Default global shortcuts:

- `Ctrl+Alt+N`: create a note
- `Ctrl+Alt+H`: show or hide notes

Each global shortcut can be enabled independently and edited in Settings. Registration failures are logged rather than crashing the application.

## Saving and `.naranote` files

`Ctrl+S` is an export command for the current note, separate from automatic application-state persistence.

- A text-only note defaults to UTF-8 `.txt`.
- A note containing drawings or objects defaults to `.naranote`.
- Saving a rich note to a non-`.naranote` format displays a data-loss warning because only text will be exported.
- `.naranote` is a ZIP-based package containing a JSON manifest and available image or attachment assets.
- Existing linked export files are checked when the note receives focus. NaraNote asks before reloading an externally modified file.
- The `.naranote` extension is registered for the current Windows user at startup without administrator rights.

## System tray and startup

The system tray menu provides:

- New note
- Show all, including a count of hidden notes
- Hide all
- Exit

Double-clicking the tray icon creates a new note. When tray support is enabled, closing the last visible note does not terminate the process. Windows startup registration uses the current-user `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` entry and does not require elevation.

## Data and privacy

All application data is stored locally under:

```text
%LocalAppData%\NaraNote\
├─ app-state.json
├─ app-state.backup.json
├─ images\
├─ attachments\
└─ logs\
```

NaraNote does not send notes, clipboard contents, files, diagnostics, or telemetry over the network. Logs do not include full note text or clipboard text. To remove all local NaraNote data, exit the application and delete `%LocalAppData%\NaraNote`.

## Build from source

Requirements:

- Windows 10 22H2 or later, or Windows 11
- .NET 8 SDK
- x64 environment

```powershell
dotnet restore NaraNote.sln
dotnet build NaraNote.sln --configuration Release
dotnet test NaraNote.sln --configuration Release
dotnet run --project src/NaraNote.App/NaraNote.App.csproj --configuration Release
```

Create the self-contained single-file build:

```powershell
dotnet publish src/NaraNote.App/NaraNote.App.csproj `
  --configuration Release `
  --output release
```

The `release` directory should contain only `NaraNote.exe`. The executable is intentionally large because it contains the .NET runtime and required native components.

## Project structure

```text
NaraNote.sln
src/
├─ NaraNote.App/             WPF UI, view models, localization, and Windows interaction
├─ NaraNote.Core/            Models, undo/redo, drawing recognition, and utilities
└─ NaraNote.Infrastructure/  Persistence, logging, startup, and interop services
tests/
└─ NaraNote.Core.Tests/      xUnit tests for UI-independent behavior
```

WPF controls are never serialized. Notes, objects, images, attachments, and ink use plain data models. Persistence writes a temporary file, flushes it, replaces the primary state atomically, and retains a backup for recovery.

## NuGet packages

### Application

- **AvalonEdit 6.3.1.120** — lightweight WPF source editor used for syntax highlighting and text editing. WPF's built-in `TextBox` does not provide token-level syntax coloring, and implementing the same behavior with `RichTextBox` would interfere with text undo, caret, and IME behavior.

### Tests

- **Microsoft.NET.Test.Sdk** — test discovery and execution through `dotnet test`
- **xunit** — unit-test framework
- **xunit.runner.visualstudio** — xUnit integration with VSTest and IDE runners

## Current limitations

- The custom note color picker remains limited to presets.
- Object selection is single-object only.
- Images use corner resize handles; attachments use eight-direction handles.
- WebP support depends on the image codecs available to WPF on the system.
- Global shortcut conflicts are logged, but the Settings window does not yet show an inline conflict banner.
- Automatic updates and cloud synchronization are intentionally outside the 1.0 scope.

## License

Copyright © 2026 naranicca.

NaraNote is free software distributed under the **GNU General Public License,
Version 3.0 only** (`GPL-3.0-only`). You may use, study, modify, and redistribute
the software under the terms of that license. Modified versions distributed to
others must also comply with GPL 3.0 and make the corresponding source code
available as required by the license.

See the [LICENSE](LICENSE) file for the complete license text.
