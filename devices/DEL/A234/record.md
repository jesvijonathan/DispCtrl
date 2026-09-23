### Dell U2424H

Device key: `DEL-A234`

| | |
|---|---|
| Model | U2424H |
| Manufacturer | Dell (DEL) |
| Manufacturer and product | DEL-A234 |
| Controller type | 0x09 (raw 0x8809) |
| Connector | HDMI |
| Panel technology | LCD (TFT) |
| Physical size | 527 x 296 mm (23.8 in) |
| Highest mode | 1920 x 1080 @ 120 Hz |
| Bit depth | 8-bit per channel |
| Colour format | YCbCr 4:4:4 |
| HDR | not supported |
| Variable refresh | 48-120 Hz |
| DDC/CI | answers |
| Brightness over DDC/CI | yes |
| MCCS version | 2.1 |
| EDID manufacturer | Dell (DEL) |
| EDID product | A234 |
| EDID name | DELL U2424H |
| Made | week 12 of 2024 |
| EDID version | 1.3 |

#### Controls it lists (37, 12 DispCtrl will drive)

- `0x02` New control value (read-only): 0 to 255
- `0x04` Restore factory defaults (read-only): 0 to 1
- `0x05` Restore factory brightness and contrast (read-only): 0 to 1
- `0x08` Restore factory colour defaults (read-only): 0 to 1
- `0x10` Brightness (range): 0 to 100 **(DispCtrl writes this)**
- `0x12` Contrast (range): 0 to 100 **(DispCtrl writes this)**
- `0x14` Colour preset (list): 0x01 sRGB, 0x04 5000 K, 0x05 6500 K, 0x06 7500 K, 0x08 9300 K, 0x09 10000 K, 0x0B User 1, 0x0C User 2 **(DispCtrl writes this)**
- `0x16` Red gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x18` Green gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x1A` Blue gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x52` Active control (read-only): 0 to 255
- `0x60` Input source (list): 0x0F DisplayPort 1, 0x11 HDMI 1 **(DispCtrl writes this)**
- `0x66` Ambient light sensor (list): 0x0F, 0x02 On
- `0x67` Manufacturer-specific control 67 (read-only): 0 to 65535
- `0x68` Manufacturer-specific control 68 (read-only): 0 to 65535
- `0x87` Sharpness (range): 0 to 100 **(DispCtrl writes this)**
- `0xAA` Screen orientation (read-only): 0x00, 0x01, 0x02, 0x04
- `0xAC` Horizontal frequency (read-only): 0 to 2
- `0xAE` Vertical frequency (read-only): 0 to 65535
- `0xB2` Flat panel sub-pixel layout (read-only): 0 to 1
- `0xB6` Display technology (read-only): 0 to 5
- `0xC6` Application enable key (read-only): 0 to 65535
- `0xC8` Display controller type (read-only): 0 to 39
- `0xC9` Firmware level (read-only): 0 to 65535
- `0xCA` OSD and power button lock (list): 0x01 Unlocked, 0x02 Locked **(DispCtrl writes this)**
- `0xCC` OSD language (list): 0x02 English, 0x0A Spanish, 0x03 French, 0x04 German, 0x08 Portuguese, 0x09 Russian, 0x0D Chinese (simplified), 0x06 Japanese **(DispCtrl writes this)**
- `0xD6` Power mode (list): 0x01 On, 0x04 Off (soft), 0x05 Off (hard) **(DispCtrl writes this)**
- `0xDC` Picture mode (list): 0x00 Standard, 0x03 Movie, 0x05 Games **(DispCtrl writes this)**
- `0xDF` MCCS version (read-only): 0 to 65535
- `0xE0` Manufacturer-specific control E0 (read-only): 0 to 1
- `0xE1` Manufacturer-specific control E1 (read-only): 0 to 1
- `0xE2` Manufacturer-specific control E2 (read-only): 0x00, 0x02, 0x04, 0x0C, 0x0D, 0x0F, 0x10, 0x11, 0x13, 0x0B, 0x1A, 0x14
- `0xF0` Manufacturer-specific control F0 (read-only): 0x09
- `0xEF` Manufacturer-specific control EF (read-only): 0x00, 0x01, 0x03, 0x0F
- `0xF1` Manufacturer-specific control F1 (read-only): 0 to 65535
- `0xF2` Manufacturer-specific control F2 (read-only): 0 to 65535
- `0xFD` Manufacturer-specific control FD (read-only): 0 to 255

Low-level commands it accepts: 0x01 0x02 0x03 0x07 0x0C 0xE3 0xF3

#### Capabilities string

```
(prot(monitor)type(LCD)model(U2424H)cmds(01 02 03 07 0C E3 F3)vcp(02 04 05 08 10 12 14(01 04 05 06 08 09 0B 0C) 16 18 1A 52 60(0F 11 ) 66(0F02) 67 68 87 AA(00 01 02 04 ) AC AE B2 B6 C6 C8 C9 CA CC(02 0A 03 04 08 09 0D 06 ) D6(01 04 05) DC(00 03 05 ) DF E0 E1 E2(00 02 04 0C 0D 0F 10 11 13 0B 1A 14 ) F0(09 ) EF(00 01 03 0F) F1 F2 FD)mswhql(1)asset_eep(40)mccs_ver(2.1))
```

<details><summary>Modes the driver reports (13)</summary>

- 1920 x 1080 @ 120, 119, 100, 60, 59, 50 Hz
- 1680 x 1050 @ 120, 119, 100, 60, 59, 50 Hz
- 1600 x 900 @ 120, 119, 100, 60 Hz
- 1280 x 1024 @ 120, 119, 100, 75, 60 Hz
- 1366 x 768 @ 120, 119, 100, 60 Hz
- 1152 x 864 @ 120, 119, 100, 75, 60, 59 Hz
- 1280 x 720 @ 120, 119, 100, 75, 60, 59, 50 Hz
- 1024 x 768 @ 120, 119, 100, 75, 60 Hz
- 800 x 600 @ 120, 119, 100, 75, 60 Hz
- 720 x 576 @ 120, 119, 100, 60, 59, 50 Hz
- 720 x 480 @ 120, 119, 100, 60, 59 Hz
- 640 x 480 @ 120, 119, 100, 75, 60, 59 Hz
- 720 x 400 @ 120, 119, 100, 70, 60, 59 Hz

</details>

#### Observed connection (may differ between setups)

| | |
|---|---|
| Active signal mode | 1920 x 1080 @ 120 Hz |
| Pixel clock | 297 MHz |
| Pixel density at current resolution | 93 PPI |
| Windows rendering | 96 DPI (100% scaling) |

---

Submitted from DispCtrl. Serial number, device path, file paths, user name are not included. Model capabilities and the observed signal/scaling are included; brightness, wallpaper and app settings are not.
