# LangFix

Windows tray tool that fixes text typed with the wrong keyboard language (Thai ⇄ English, Kedmanee layout).

Typed `.s,jmujf^l;p'k,` while the keyboard was in English? Press **Ctrl+Shift+Space** and it becomes `ใหม่ที่ดูสวยงาม`, then the keyboard layout switches so you can keep typing.

## Use

1. Run `LangFix.exe`. It sits in the system tray (right-click → Exit to quit).
2. Typed in the wrong language? Press **Ctrl+Shift+Space**.
   - Nothing selected: the last space-delimited word before the caret is converted (spaces after it are kept).
   - Text selected (and nothing typed since the last click/Enter): only the selection is converted.

It never converts on its own, so passwords are only touched if you press the hotkey.

## How it works

- A low-level keyboard hook remembers only the current word in memory (physical keys and which layout was active). Nothing is written to disk or sent anywhere.
- The fix is Backspace + retype, so the clipboard is not used in word mode.
- Selection mode copies with Ctrl+C using plain Win32 clipboard calls and puts the clipboard back afterwards.
- `%LOCALAPPDATA%\LangFix\log.txt` holds content-free diagnostics (key counts and layout ids, never the typed characters).

## Limits

- Programs running as administrator don't deliver keystrokes to a normal-privilege hook.
- Some editors copy the whole line on Ctrl+C with no selection; selection mode may then convert that line.

## Build

Needs only the .NET Framework compiler that ships with Windows:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /codepage:65001 /platform:x64 /target:winexe /win32icon:LangFix.ico /out:LangFix.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll LangFix.cs
```
