# GAFIL — a pixel cat on your desktop × Claude

[한국어](README.md) | **English**

![GAFIL sprites: walking, sitting, thinking, lying down, jumping](docs/gafil.png)

**GAFIL** (Genius Autonomous Feline Intelligence & Laziness) is an orange pixel cat that wanders along the bottom of your Windows desktop.
Click it to talk and **Claude** answers. With your permission it can also run commands, work with files and even **operate your screen** for you.

> Unofficial fan project — not affiliated with or endorsed by Anthropic.

---

## What it does

| Feature | Description |
|---|---|
| 💬 Chat | Click the cat → ask in the speech bubble → Claude's answer types itself out. Keeps the conversation context |
| 📈 CPU/GPU | When you're not chatting, shows `CPU 12% · GPU 3% 45°C` above the cat. NVIDIA GPUs also show temperature and VRAM; integrated GPUs show usage |
| ⚙ Commands & files | "Close my browser", "Tidy up my Downloads folder", … **An approval window before every run** |
| 🐾 Screen control | Tasks that need the screen, like "Search the weather in my browser". It looks at screenshots and clicks/types. **One approval per task + red border + emergency stop** |
| ↗ Hand off to the Claude app | Double-click the bubble header → a new chat in the Claude desktop app, pre-filled with the conversation |
| 🔁 Starts with Windows | Appears automatically when you sign in (can be turned off in the menu) |
| ⌨ Summon hotkey | `Ctrl+Alt+G` from anywhere → the bubble opens, ready to type |
| 🎤 Talk to it | 🎤 button or `Ctrl+Alt+Space` → speak; it writes down what you say and sends it when you pause (Windows voice typing) |
| 🖐 Pick it up | Drag the cat and it dangles **like a kitten carried by the scruff of its neck**. Drop it on another monitor and it lives there |
| 📎 Drop files & text | Drag a file or selected text onto the cat and say "summarize this" (only that file becomes readable without approval) |
| ⏰ Reminders | "Remind me about the meeting in 30 minutes" → when it's time, the cat runs to the middle of the screen to tell you (remembered across restarts) |
| 😺 Moods | Pants when the CPU is busy, sweats when the GPU is hot, and at night it moves slowly and naps with Zz |
| 🎨 Dress up | Right-click → **Change look**: 5 fur colors, 4 outfits |
| 💾 History | Conversation and context carry over after a restart |
| 🌐 Korean / English | Follows your Windows display language (can be fixed in settings) |
| ⬆ Update notice | The cat tells you when a new version is out, with a one-click download from the menu |

![Fur colors: orange, black, white, gray tabby, cream](docs/skins.png)

![Outfits: crown, sunglasses, red scarf, bow tie, held by the scruff](docs/outfits.png)

![Dangling by the scruff, panting and sweating](docs/moods.png)

---

## Requirements

- **Windows 11** or **Windows 10 version 2004+** (64-bit)
- **Claude Code** (Anthropic's command-line tool), installed and logged in
  - Either a Claude **Pro / Max subscription** or an **Anthropic API key**
- .NET is **not** required (it's bundled in the exe)

---

## Installation

### 1. Install Claude Code and log in

In PowerShell:

```powershell
irm https://claude.ai/install.ps1 | iex
```

(If you have Node.js, `npm install -g @anthropic-ai/claude-code` works too.)

Then run it once in a terminal to log in:

```powershell
claude
```

Log in with your Claude account in the browser that opens, then close the terminal with `/exit`.
More: [Claude Code docs](https://docs.claude.com/en/docs/claude-code/overview)

### 2. Get GAFIL

**Option A — download the exe (recommended)**

1. From [Releases](../../releases/latest), download `GAFIL.exe` and `install.cmd` into **the same folder**.
2. Double-click `install.cmd`.
   → It is copied to `%LOCALAPPDATA%\Programs\GAFIL\GAFIL.exe`, started, and registered to start with Windows.

> The exe is not code-signed, so **Windows SmartScreen** may warn you the first time. Click "More info → Run anyway".

**Option B — build from source**

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/steeze1213/GAFIL.git
cd GAFIL
dotnet publish -c Release -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist
.\install.cmd
```

### 3. Check

- If a cat appears at the bottom right and starts walking, the app works.
- Click the cat → say "hi" → if it answers, the Claude connection works too.
- If you see "Couldn't find the 'claude' command" or a login error, see [Troubleshooting](#troubleshooting).

---

## Accounts

GAFIL uses **whatever account Claude Code on that PC is logged into**. There's no separate account setting.

| To do this | Do this |
|---|---|
| Switch Claude accounts | In a terminal: `claude` → `/logout` → `/login` → right-click GAFIL → **New conversation** |
| Use an API key instead of a subscription | Set the `ANTHROPIC_API_KEY` environment variable and restart GAFIL (billed by API usage) |
| Pin a model | Set `"Model"` in the settings file to `"sonnet"`, `"opus"`, `"haiku"`, … → restart GAFIL. Empty = Claude Code's default |

> Logins, settings and logs are stored per Windows user account.

---

## Usage

### The cat

- **Left-click**: open/close the speech bubble
- **`Ctrl+Alt+G`** (anywhere): open the bubble and start typing
- **`Ctrl+Alt+Space`** (anywhere) or 🎤 in the bubble: talk. It sends 1.8 s after you stop speaking
- **Drag**: pick it up by the scruff. It drops to the floor where you release it and stays on that monitor
- **Drop files/text**: they appear as 📎 chips above the input box → type what to do, or just press `Enter` (summarize)
- **Right-click**: menu — Talk, New conversation, Change look (fur / outfit), Show CPU/GPU, Start with Windows, Open settings file, Open command log, Quit GAFIL

### The speech bubble

- `Enter` send · `Shift+Enter` new line · `Esc` close
- Header buttons: ■ Stop (while working) · 🎤 Talk · Continue in app · Expand (up to the top of the screen) · Close
- **Double-click** the header or chat history to continue in the Claude desktop app (requires the app)

### Try asking

```
Hi, help me plan my day
Which 5 programs are using the most memory right now?
Close my browser
Open Notepad and type "Hello GAFIL"
Remind me to stretch in 30 minutes
Remind me tomorrow at 9:30 to send the meeting notes
```

---

## Safety

GAFIL is built so that it **doesn't change your PC without your permission**.

| Action | How it's handled |
|---|---|
| Running commands (PowerShell/Bash), writing or editing files | **Approval window every time**, showing exactly what will run/be written. Default button is "Deny"; auto-denied after 5 minutes |
| Reading files | Inside allowed folders: allowed. Outside: approval window (no allowed folders by default) |
| Fetching web pages | Approval window (a URL can carry data out) |
| Web search | Allowed |
| Screen control | **One approval per task (question)**. Red border while working, **`Ctrl+Alt+Shift+F12` stops it instantly**. Asks again next time |
| Clicks during screen control | A **yellow ring is shown for 0.8 s** where it's about to click. If **you move the mouse** during that time or between actions, it stops immediately |
| Dropped files | Only those files/folders can be **read** without approval, for this conversation. Cleared on a new conversation |
| Reminders | Local timers that don't change your PC, so no approval |

- If you deny something, GAFIL doesn't try the same thing again.
- If another program owns the emergency-stop hotkey, screen control won't start at all (there'd be no way to stop it).
- GAFIL is **instructed** to stop and ask right before paying, sending, deleting or entering passwords. That's an instruction to the AI, not a guarantee; the hard safeguards are the approval window, the red border and the emergency stop.
- Claude Code's global auto-allow rules are not used by GAFIL (it uses only its own settings).

---

## Settings file

Right-click → **Open settings file** (`%LOCALAPPDATA%\ClaudeDesk\settings.json`)

```json
{
  "AllowedFolders": ["D:\\Work", "C:\\Users\\me\\Documents\\notes"],
  "ShowStats": true,
  "AutoStart": true,
  "Model": "",
  "Language": "auto",
  "OpenHotkey": "Ctrl+Alt+G",
  "Skin": "orange",
  "Outfit": "none",
  "ScreenPreviewMs": 800,
  "CheckUpdates": true,
  "SaveHistory": true,
  "VoiceHotkey": "Ctrl+Alt+Space",
  "VoiceAutoSendMs": 1800
}
```

| Key | Meaning |
|---|---|
| `AllowedFolders` | Folders GAFIL may **read** without approval (writing always needs approval) |
| `ShowStats` | CPU/GPU line above the cat |
| `AutoStart` | Start with Windows (applies only when running from the install location) |
| `Model` | Model to use. Empty = Claude Code's default |
| `Language` | `"auto"` (Windows display language), `"ko"`, `"en"` |
| `OpenHotkey` | Hotkey to summon GAFIL, e.g. `"Ctrl+Shift+Space"`. Empty = off |
| `Skin` / `Outfit` | Look (same as the menu) |
| `ScreenPreviewMs` | Click preview time during screen control (ms). `0` = no preview (moving the mouse still stops it) |
| `CheckUpdates` | Check for new versions (once a day) |
| `SaveHistory` | Save the conversation. `false` = don't save and delete the existing history |
| `VoiceHotkey` | Hotkey for talking. Empty = off |
| `VoiceAutoSendMs` | How long to wait after you stop speaking before sending (ms). `0` = no auto-send, press Enter |

Settings are read when GAFIL starts. After saving, restart GAFIL (right-click → **Quit GAFIL** → run `%LOCALAPPDATA%\Programs\GAFIL\GAFIL.exe`).

---

## Privacy & data

- **Sent to Anthropic**: your questions, file contents GAFIL reads, command output, and **screenshots during screen control** (same as using Claude Code).
- **Voice input** uses Windows voice typing (`Win+H`). Speech-to-text is handled by Windows (Microsoft); GAFIL only receives the transcribed text.
- **Stored only on this PC** (`%LOCALAPPDATA%\ClaudeDesk\`):
  - `settings.json` settings
  - `history.json` conversation history (turn off with `SaveHistory: false`, clear with **New conversation**)
  - `reminders.json` reminders
  - `logs\commands.log` approval requests, allow/deny, command results, screen actions (typed text is logged by length only)
- **Update check**: one anonymous request a day to GitHub's public API (`api.github.com`), no personal data (turn off with `CheckUpdates: false`).
- Claude Code itself also stores conversations in `%USERPROFILE%\.claude\projects\`.
- GAFIL has no server of its own and no remote-access feature.

---

## Troubleshooting

| Problem | Fix |
|---|---|
| The cat doesn't appear | Check for `GAFIL.exe` in Task Manager. If it's not there, run `%LOCALAPPDATA%\Programs\GAFIL\GAFIL.exe`. If an error box appears, check `%LOCALAPPDATA%\ClaudeDesk\logs\commands.log` |
| "Couldn't find the 'claude' command" | Check that Claude Code is installed: `claude --version` in a new terminal. If you just installed it, restart GAFIL |
| Login / auth error | Run `claude` in a terminal → log in → New conversation in GAFIL |
| Screen control never starts | Check whether another program uses `Ctrl+Alt+Shift+F12` (GAFIL's log says "단축키 등록 실패" = hotkey registration failed) |
| Clicks land in the wrong place | Screen control supports the primary monitor only. If you changed display scaling, restart GAFIL |
| Screen control keeps stopping | Touching the mouse while it works stops it for safety. Take your hand off the mouse while it works |
| `Ctrl+Alt+G` does nothing | Another program may own it → change `OpenHotkey` |
| 🎤 doesn't transcribe | Windows Settings → Time & language → **Speech** / **Typing**: make sure voice typing is enabled. Try pressing `Win+H` yourself |
| Can't click an administrator window | Windows blocks it (UIPI). Please click it yourself |
| SmartScreen warning | "More info → Run anyway" |
| The cat is hidden behind the taskbar | Can happen with an auto-hiding taskbar |

---

## Uninstall

1. Right-click GAFIL → uncheck **Start with Windows** → **Quit GAFIL**
2. Delete the folders `%LOCALAPPDATA%\Programs\GAFIL` and `%LOCALAPPDATA%\ClaudeDesk`

---

## Known limitations

- Screen control works on the primary monitor only (the cat itself can live on any monitor).
- Screen control takes a few seconds per action. Anything doable with a command is much faster that way.
- Reminders only fire while GAFIL is running (missed ones are shown when it starts).
- On integrated GPUs (Intel/AMD) the GPU temperature shows N/A (there's no vendor-neutral way to read it). Usage is shown.
- If the taskbar is at the top or side, the "floor" may not line up.

---

## Project structure

| File | Role |
|---|---|
| `MainWindow.xaml(.cs)` | Cat behavior & pixel drawing, speech bubble, menu, screen-control mode |
| `MainWindow.*.cs` | Split by feature: `Drag`, `Drop`, `History`, `Mood`, `Look`, `Reminders`, `Update`, `OpenHotkey`, `Voice` |
| `Reminders.cs` | Reminder store + reminder tools (in-process MCP server `gafil_time`) |
| `L.cs` | Korean/English strings |
| `ClaudeClient.cs` | Runs the Claude Code CLI (stream-json), approvals, logging |
| `ScreenTools.cs` | Screen-control tools (in-process MCP server for the CLI) |
| `ScreenInput.cs` | Screen capture, mouse & keyboard input (`SendInput`) |
| `ScreenControlOverlay.cs` | Red border during screen control |
| `ApprovalWindow.xaml(.cs)` | Approval window |
| `SystemStats.cs` | CPU/GPU readings (NVIDIA NVML / Windows performance counters) |
| `AppSettings.cs` | Settings file, autostart, logging |
| `app.manifest` | Per-monitor DPI awareness (accurate mouse coordinates) |
| `install.cmd` | Install script |

Code comments are in Korean.

---

## Changelog

- **v1.2.1** — 🎤 Talk to it (`Ctrl+Alt+Space`, Windows voice typing, auto-send), English README, better display-language detection
- **v1.1.0** — Summon hotkey, pick up by the scruff & multiple monitors, drop files/text, saved history, reminders, moods (panting, sweating, napping), fur colors & outfits, English UI, update notice, click preview + stop when you move the mouse
- **v1.0.0** — First release: chat, CPU/GPU display, commands & files behind approval, screen control, hand-off to the Claude app, start with Windows

---

## License

[MIT License](LICENSE) — free to use, modify and redistribute (keep the copyright notice).

GAFIL's pixel cat was drawn for this project and is not an official Anthropic character.
"Claude" is a trademark of Anthropic; this is an unofficial project not affiliated with Anthropic.
