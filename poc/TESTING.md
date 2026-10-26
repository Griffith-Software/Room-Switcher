# Room Switcher: hardware test on the 3-monitor PC

Room Switcher was built and tested on a PC with one monitor. These checks need the real setup:
the rack PC with two office monitors and one living-area monitor. They cover build steps 1 and 6
and the acceptance criteria that need several monitors.

The test kit has two programs:

- `DisplayPoc.exe`: the proof-of-concept tool. Every change it makes to the screens reverts by itself after 15 seconds unless you press **K**.
- `RoomSwitcher.exe` from the release zip.

If anything ever leaves a screen dark: press **Win+P** and choose **Extend**, or run `DisplayPoc showall`, or `RoomSwitcher.exe --all`.

Write down anything that doesn't match "Expect". The `(applied: â€¦)` / `(validated: â€¦)` line DisplayPoc prints is the most useful detail.

## Part 1: proof of concept (DisplayPoc)

Open a Command Prompt in the test-kit folder.

1. `DisplayPoc list`
   *Expect:* all 3 monitors listed with names and connections, the graphics card's source count, and the screens that are on.
2. In Windows display settings, extend to the two office monitors only (living monitor disconnected from desktop). Arrange them, choose the main display, and set scale. Then run `DisplayPoc save office.json`.
3. Set Windows to show only the living monitor (at the resolution and scale you want). Run `DisplayPoc save living.json`.
4. `DisplayPoc validate office.json` and `DisplayPoc validate living.json`
   *Expect:* `OK (validated: saved arrangement, exact)`.
5. `DisplayPoc toggle office.json living.json 3`
   *Expect:* it switches 6 times. Each time, the monitors not in the arrangement show **No signal** and go to sleep, and each check line says `all match` (size, refresh, rotation, scale, primary). It ends by putting back how things were.
   - If `scale` doesn't match, note it (Room Switcher sets it explicitly, but some drivers ignore that).
   - If a step says `ChangeDisplaySettingsEx fallback`, the driver rejected partial path sets. Note it.
6. Mirroring: `DisplayPoc clone 1 3` (use the numbers from `list` for the desk monitor and the TV).
   *Expect:* `OK`. Then `DisplayPoc clone 1 3 --apply`: both show the same picture for 15 s, then it reverts. Note the resolution `list` shows during the mirror.
7. `DisplayPoc impossible`
   *Expect:* "Without SDC_ALLOW_CHANGES: refused â€¦ (as expected)". With 3 monitors it also checks "all 3 as separate screens" (expect OK) and "all 3 mirrored" (OK or a plain reason).

## Part 2: Room Switcher for real

1. Unzip `RoomSwitcher_v0.5.0.zip` and run `RoomSwitcher.exe`. Setup opens.
2. **Monitors:** name them (for example Office left, Office right, Living room TV). Click **Identify**; turn all monitors on when asked. *Expect:* each monitor shows its number and name.
3. **Rooms:** make these rooms:
   - **Office**: Office left â†’ Screen 1, Office right â†’ Screen 2, TV â†’ Off.
   - **Living Area**: TV â†’ Screen 1, both office monitors â†’ Off.
   - **Presentation**: Office left and TV both â†’ Screen 1 (mirrored), Office right â†’ Off.
   - **Everything**: each monitor on its own screen.
   - **Mixed**: Office left and TV â†’ Screen 1, Office right â†’ Screen 2.
   *Expect:* each says "âœ” This room works on this PC", or gives a plain reason it can't.
4. **Arrange** each room: Turn on â†’ arrange in display settings â†’ Save this room's layout.
5. **Finish**, then pick rooms from the tray icon.

### Acceptance checks

- [ ] Switching to a room turns on exactly its monitors at the saved resolution, position, refresh rate and main display; every other monitor shows **No signal**.
- [ ] Open a few windows in Office (one maximised, one on the right monitor, one minimised). Switch to Living Area, open or move something there, then switch back. *Expect:* Office's windows are back on the same monitors and positions.
- [ ] Switch Office â†’ Living Area â†’ Office ten times. *Expect:* windows don't creep or shrink.
- [ ] Presentation: the desk monitor and TV show identical content.
- [ ] Mixed: two extended screens with the TV mirroring the first.
- [ ] The TV is in four rooms with different routing; each switch applies the right one.
- [ ] Unplug the TV's cable, then open the tray menu. *Expect:* rooms that need the TV are greyed out with "Living room TV isn't connected right now." Plug it back in.
- [ ] **Show all monitors** turns everything on from any room.
- [ ] `RoomSwitcher.exe --room "Living Area"` from a Command Prompt, with the tray copy running and again after Exit. Both switch. `--list` marks the current room with `*`.
- [ ] Reboot. *Expect:* rooms and window layouts are still there; with Start with Windows on, the tray icon comes back.
- [ ] Portable: copy `RoomSwitcher.exe` and `config.json` to a USB drive and run it from there. *Expect:* the Finish page says "Portable mode", and nothing new appears in `%APPDATA%\Room Switcher`.
- [ ] A program run as administrator (for example an elevated Command Prompt) is reported as "couldn't be moved back" rather than silently ignored.

Send back: the DisplayPoc output from Part 1, and which checks passed or failed with what you saw.
