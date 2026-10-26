# Changelog

All notable changes to Room Switcher. Versions follow [semantic versioning](https://semver.org/).

## 1.0.0 (2026-10-26)

- First public release. Licensed under Apache License 2.0 with the Commons Clause (source-available: free to use, modify and share, not to sell).
- The download now also includes `NOTICE` (copyright and trademark notice).
- Exit asks whether to turn devices back on that your rooms turned off, since they'd otherwise stay off after Room Switcher closes.
- `startup.log` no longer contains your Windows user name: paths are written as `%USERPROFILE%`.
- The download now includes `THIRD-PARTY-NOTICES.md` (the bundled .NET runtime's MIT license) and comes with a `.sha256` checksum file.
- README: new **Privacy** and **Removing Room Switcher** sections, and the build instructions no longer name an old version.

## 0.6.9 (2026-10-25)

- New description: one PC, several workstations, like a KVM switch in reverse. The README explains what Room Switcher does and doesn't do (every desk is still cabled to the PC; one room is on at a time), and its setup steps now include Audio, Devices and the room's sound and devices.

## 0.6.8 (2026-10-25)

- Audio step: the "two devices are both called…" warning now names the two devices, and ignores nicknames kept for devices Windows no longer lists (such as speakers that were renamed after moving to another USB port), which couldn't be seen or fixed on the page. The note under the lists now says a device without a nickname is shown by its **model name**.

## 0.6.7 (2026-10-25)

- Settings buttons: **Save and Close** is in the bottom-right corner, **Next** is now **Save and Continue** (it saves your changes, then goes to the next step), and Cancel is gone; closing the window still asks whether to save. On the last step Save and Close is the main button. First-run setup keeps Next and Finish, and saves at the end.

## 0.6.6 (2026-10-25)

- Rooms step: each device in the room's list has a trash can button instead of a Remove button.

## 0.6.5 (2026-10-25)

- Rooms step: Speakers, Microphone and **Devices** (the Add a device list) are stacked on the left, and the room's devices are in a fixed-size scrolling list on the right, styled like the other lists, with a **Remove** button on each.
- Fixed: after maximising a window and restoring it, a strip appeared above the blue header.

## 0.6.4 (2026-10-25)

- Rooms step: the room's device tags are listed one per line and the list scrolls after three devices, so adding more doesn't shrink the rest of the page. Check again and its result now share a line, leaving more room for the displays grid.

## 0.6.3 (2026-10-25)

- Rooms step: **Devices in this room** is now an **Add a device…** list with the room's devices shown next to it as tags; select × on a tag to take it out of the room. (0.6.2 listed every device as a tick box, which gets long.) The Devices step's camera button is now **Test**, like the Audio step.

## 0.6.2 (2026-10-25)

- Rooms step: new **Devices in this room** list. Tick the devices a room uses, whether or not rooms turn them on and off; they show (and can be tested) in Test devices. For a device rooms switch, ticked means on in that room, the same as on the Devices step. Turning Switch by room on or off for a device keeps the rooms it's in.
- Devices step: the **Type** can be changed (select it). Use this when Windows reports a device wrongly, such as a mouse receiver that also says it's a keyboard. The type you choose is used everywhere, including the keyboard warning. (The safety rule that always keeps one keyboard and one mouse working still goes by what Windows reports.)
- Devices step: the on/off-in-each-room grid uses check boxes, since a device can be on in any number of rooms.
- The Open camera button no longer runs out of its column.

## 0.6.1 (2026-10-25)

- Identify keeps the display numbers on screen until you turn them off: the button changes to **Stop identifying**, and the numbers also go away when you click one or close Test devices or settings. Before, they disappeared after four seconds. In Test devices, switching rooms moves the numbers to the new screens.
- Test devices: the bottom of the page (the automatic restart check box) is no longer cut off.

## 0.6.0 (2026-10-25)

- **Test devices…** replaces Turn on all displays in the tray menu. Pick a room on the left and test everything it uses: its displays (Identify, or Switch to the room first), its speakers (chime) and microphone (live level), and the devices it keeps on (press a key or move a mouse and its row says Working; webcams open the Camera app). Also `RoomSwitcher.exe --test`. Turning on all displays is still available as `RoomSwitcher.exe --all`.
- Silent apps fix, for PCs where Windows sounds play after a restart but apps don't: **Check app sound** plays a chime the way apps do and reads the speaker's own level meter to tell whether it got through; **Restart Windows Audio** restarts the service (like Services > Windows Audio > Restart) and checks again; and **Restart Windows Audio automatically after I sign in** does it about 15 seconds after each sign-in. Uses the administrator helper, which needs updating once (Windows asks for permission).

## 0.5.3 (2026-10-25)

- If your rooms can't be read when Room Switcher starts at sign-in, it now keeps trying every few seconds for 15 minutes (and again whenever you open its menu or use a shortcut or command), and picks them up the moment they can be read, with a "Your rooms are back" notification. Before, it gave up after a minute.
- startup.log now records what Windows answered when the rooms couldn't be read.

## 0.5.2 (2026-10-25)

- Fixed: Start with Windows didn't start Room Switcher. Some Windows 11 builds skip its startup entry in the registry without saying why, so it's now a **Room Switcher** shortcut in your Startup folder (it shows in Task Manager's Startup apps, where it can also be turned off). The old registry entry is removed.

## 0.5.1 (2026-10-25)

- Fixed: if Room Switcher couldn't read your rooms when it started, it opened an empty setup. At sign-in it now waits up to a minute for your settings, and if it still can't find them it says where it looked instead of opening an empty setup that could be saved over them.
- Every save keeps the settings it replaces as `config.previous.json` next to `config.json`, so a bad save can be undone.
- Each start notes what it found in `%LOCALAPPDATA%\Room Switcher\startup.log`, to help track down startup problems.

## 0.5.0 (2026-10-24)

- Arrange your rooms inside Room Switcher: the Arrange step shows only the chosen room's screens, to scale and with their nicknames. Drag them into place (they snap edge to edge like in Windows) and set each screen's resolution, refresh rate, orientation, scale and main display. Apply turns the room on like that and asks "Keep these display settings?", going back by itself after 15 seconds. Windows display settings, and copying the layout Windows is showing, are still there.
- Room Switcher remembers the resolutions each display supports while it's on, so the arranger can offer them while it's off.
- The USB step is now **Devices**: USB and Bluetooth keyboards, mice, webcams, game controllers, pens and tablets, printers, phones and USB drives, with a Connection column. Paired Bluetooth devices that aren't connected show as Not connected. Speakers and microphones stay on the Audio step.
- "Windows calls it" is now **Model name** on every step: the name the hardware reports itself (for displays, the model stored in the display).
- The version number shows in the bottom-left corner of setup.
- Clearer wording that matches Windows: monitors are now called displays everywhere (the first step is **Displays**, with **Detect displays** and **Remove display**), "mirror" is now "duplicate", the tray's Show all monitors is now **Turn on all displays**, the Devices column Rooms control is now **Switch by room**, the Rooms step's sound lists are **Speakers** and **Microphone** with **No change**, and the setup window opened from the tray is called **Room Switcher settings**. The wait after switching is now set in seconds, and error messages say what happened and what to do next.

## 0.4.1 (2026-10-22)

- The USB step (renamed from USB devices) no longer lists speakers and microphones; they're on the Audio step. A webcam's microphone stays on when a room turns its camera off.
- Nickname columns: Display Nickname, Speakers Nickname, Microphones Nickname, Device Nickname.
- In setup, Enter or a click outside a text box keeps what you typed and takes the cursor away. One click on a nickname box always opens it for typing (before, the first click sometimes didn't, and the typing was lost).

## 0.4.0 (2026-10-21)

- New **Audio** step in setup: every speaker and microphone (USB ones included), with a nickname for each (used in the Rooms sound lists). Test plays a chime through that one speaker, or shows a microphone's live level, without changing the Windows default.
- New **USB** step: keyboards, mice, webcams and USB drives, one row per physical device (a receiver that is both keyboard and mouse is one row). Speakers and microphones are on the Audio step instead, and a webcam's microphone stays on when a room turns the camera off. Give them nicknames; press a key or move a mouse and its row lights up; webcams open the Camera app.
- USB devices per room: tick "Rooms control" for a device, then choose the rooms it's on in. Switching rooms turns those devices on and off. Unticked devices are never touched. Keyboards ask for confirmation first, and Room Switcher always keeps at least one keyboard and one mouse working.
- Device switching needs one Windows administrator approval: it puts a helper copy of Room Switcher in Program Files and a scheduled task that runs it on request. It can be updated or removed from the USB step.

## 0.3.0 (2026-10-21)

- Sound per room: choose which speakers and which microphone Windows uses in each room (Rooms step, under the routing grid). Switching to the room makes them the Windows defaults for apps, calls and system sounds. A TV's HDMI sound is waited for, since it only appears once the TV's screen is on. "Don't change" leaves sound alone.
- Keyboard shortcuts keep working while the room editor is open. They pause only while you're typing into a shortcut box. (In 0.2.0 an open editor switched them all off.)

## 0.2.0 (2026-10-11)

- New look for the tray menu, matching the setup window: a rounded white panel, a header showing which room is on, an icon for each item, rounded blue highlight, shortcuts shown in grey, and a check box for Start with Windows.
- New look for setup: its own header and title bar, a step list down the side, and a white card for each step.
- Identify now works like Windows' own: each monitor shows its number in a dark rounded box in the bottom-left corner, fading in and out. Mirrored monitors show both numbers ("1 | 3").
- Keyboard shortcuts from anywhere in Windows: one per room (defaults Ctrl+Alt+1…9 by list order) and a next-room shortcut (default Ctrl+Alt+0). Change them by pressing the keys in the room editor; clashes with other rooms or other programs are flagged.
- New app and tray icon: two monitors with switch arrows. The tray icon adds a badge with the current room's first letter.

## 0.1.0 (2026-10-11)

First release.

- Rooms: named groups of monitors, each edited in a routing grid (Off, Screen 1, Screen 2…). Monitors in the same column mirror each other. Rooms can be added, renamed, duplicated, reordered and deleted at any time.
- Switching with `SetDisplayConfig`: the room's monitors come on at their saved position, resolution, refresh rate, rotation, scale and main-display setting; every other monitor is detached and shows "No signal". Falls back to Windows' remembered arrangement and then to `ChangeDisplaySettingsEx` for drivers that reject partial path sets.
- Rollback: if a switch fails or leaves no monitor on, the previous setup is put back (or all monitors are turned on), with a plain-language message.
- Live hardware check in the editor (`SDC_VALIDATE`, no flicker), with reasons such as "on different graphics cards and can't mirror each other".
- Each room remembers where its windows were and puts them back when you return.
- Tray menu with the current room checked, Edit rooms…, Show all monitors, Start with Windows and Exit.
- Command line: `--room "<name>"`, `--list`, `--all`, `--setup`, working with or without a running copy (named pipe, current user only).
- Setup: monitor list with Identify numbers, routing grid, arrange in Windows display settings, finish.
- Portable mode when `config.json` sits next to the exe. Atomic writes; unknown JSON fields are kept.
- Single self-contained exe, no installer, no network access.
