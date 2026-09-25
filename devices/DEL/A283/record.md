### Dell AW2725Q

Device key: `DEL-A283`

| | |
|---|---|
| Model | AW2725Q |
| Manufacturer | Dell (DEL) |
| Manufacturer and product | DEL-A283 |
| Controller type | 0x05 (raw 0x5605) |
| Connector | HDMI |
| Panel technology | OLED |
| Physical size | 590 x 333 mm (26.7 in) |
| Highest mode | 3840 x 2160 @ 240 Hz |
| Bit depth | 12-bit per channel |
| Colour format | RGB |
| HDR | supported |
| Variable refresh | 48-240 Hz |
| DDC/CI | answers |
| Brightness over DDC/CI | yes |
| MCCS version | 2.1 |
| EDID manufacturer | Dell (DEL) |
| EDID product | A283 |
| EDID name | AW2725Q |
| Made | week 12 of 2025 |
| EDID version | 1.3 |

#### Controls it lists (43, 11 DispCtrl will drive)

- `0x02` New control value (read-only): 0 to 2
- `0x04` Restore factory defaults (read-only): 0 to 255
- `0x05` Restore factory brightness and contrast (read-only): 0 to 1
- `0x08` Restore factory colour defaults (read-only): 0 to 255
- `0x10` Brightness (range): 0 to 100 **(DispCtrl writes this)**
- `0x12` Contrast (range): 0 to 100 **(DispCtrl writes this)**
- `0x14` Colour preset (list): 0x01 sRGB, 0x05 6500 K, 0x08 9300 K, 0x0B User 1, 0x0C User 2 **(DispCtrl writes this)**
- `0x16` Red gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x18` Green gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x1A` Blue gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x52` Active control (read-only): 0 to 255
- `0x60` Input source (list): 0x0F DisplayPort 1, 0x11 HDMI 1, 0x12 HDMI 2 **(DispCtrl writes this)**
- `0x87` Sharpness (range): 0 to 100 **(DispCtrl writes this)**
- `0xAC` Horizontal frequency (read-only): 0 to 8
- `0xAE` Vertical frequency (read-only)
- `0xB2` Flat panel sub-pixel layout (read-only): 0 to 8
- `0xB6` Display technology (read-only): 0 to 5
- `0xC6` Application enable key (read-only): 0 to 65535
- `0xC8` Display controller type (read-only)
- `0xC9` Firmware level (read-only): 0 to 65535
- `0xCA` OSD and power button lock (list): 0x01 Unlocked, 0x02 Locked
- `0xCC` OSD language (list): 0x02 English, 0x03 French, 0x04 German, 0x06 Japanese, 0x09 Russian, 0x0A Spanish, 0x0D Chinese (simplified), 0x0E Portuguese (Brazil) **(DispCtrl writes this)**
- `0xD6` Power mode (list): 0x01 On, 0x04 Off (soft), 0x05 Off (hard) **(DispCtrl writes this)**
- `0xDC` Picture mode (list): 0x00 Standard, 0x03 Movie **(DispCtrl writes this)**
- `0xDF` MCCS version (read-only): 0 to 255
- `0xE0` Manufacturer-specific control E0 (read-only): 0 to 1
- `0xE1` Manufacturer-specific control E1 (read-only): 0 to 1
- `0xE2` Manufacturer-specific control E2 (read-only): 0x00, 0x04, 0x0E, 0x12, 0x0B, 0x1B, 0x14, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x2F, 0x23, 0x24, 0x27, 0x3A
- `0xE3` Manufacturer-specific control E3 (read-only): 0 to 1
- `0xE4` Manufacturer-specific control E4 (read-only): 0 to 1
- `0xE5` Manufacturer-specific control E5 (read-only): 0 to 2
- `0xE8` Manufacturer-specific control E8 (read-only): 0 to 65535
- `0xE9` Manufacturer-specific control E9 (read-only): 0x00, 0x01, 0x02, 0x21, 0x22, 0x24, 0x29, 0x2A, 0x2D, 0x2E
- `0xEA` Manufacturer-specific control EA (read-only): 0 to 65025
- `0xEC` Manufacturer-specific control EC (read-only): 0x7F, 0xFF, 0x02
- `0xED` Manufacturer-specific control ED (read-only): 0x04, 0x00, 0x08, 0x00, 0x03, 0xFF
- `0xF0` Manufacturer-specific control F0 (read-only): 0x00, 0x05, 0x06, 0x0A, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x13, 0x31, 0x32, 0x34, 0x36
- `0xF1` Manufacturer-specific control F1 (read-only): 0 to 282
- `0xF2` Manufacturer-specific control F2 (read-only): 0 to 65280
- `0xF4` Manufacturer-specific control F4 (read-only): 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x30, 0x31, 0x32, 0x33, 0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0xA0, 0xA1, 0xA2, 0xA3
- `0xF5` Manufacturer-specific control F5 (read-only): 0x1D
- `0xFE` Manufacturer-specific control FE (read-only): 0 to 65535
- `0xFD` Manufacturer-specific control FD (read-only): 0 to 65535

Low-level commands it accepts: 0x01 0x02 0x03 0x07 0x0C 0xE3 0xF3

#### Capabilities string

```
(prot(monitor)type(lcd)model(AW2725Q)cmds(01 02 03 07 0C E3 F3)vcp(02 04 05 08 10 12 14(01 05 08 0B 0C) 16 18 1A 52 60( 0F 11 12) 87 AC AE B2 B6 C6 C8 C9 CA CC(02 03 04 06 09 0A 0D 0E) D6(01 04 05) DC(00 03 ) DF E0 E1 E2(00 04 0E 12 0B 1B 14 1E 1F 20 21 22 2F 23 24 27 3A)E3 E4 E5 E8 E9(00 01 02 21 22 24 29 2A 2D 2E) EA EC(7F FF02) ED(0400 0800 03FF) F0(00 05 06 0A 0D 0E 0F 10 11 13 31 32 34 36) F1 F2 F4(10 11 12 13 14 15 16 17 30 31 32 33 40 41 42 43 44 45 46 A0 A1 A2 A3) F5(1D) FE FD)mccs_ver(2.1)mswhql(1))
```

<details><summary>Modes the driver reports (26)</summary>

- 3840 x 2160 @ 240, 144, 120, 119, 60, 59, 50, 30, 29, 25, 24, 23 Hz
- 2560 x 1600 @ 240, 144, 120, 119, 60, 59, 50, 30, 29, 25, 24, 23 Hz
- 2560 x 1440 @ 240, 144, 120, 119, 60, 59 Hz
- 2048 x 1536 @ 240, 144, 120, 119, 60, 59, 50, 30, 29, 25, 24, 23 Hz
- 1920 x 1440 @ 240, 144, 120, 119, 60, 59 Hz
- 1920 x 1200 @ 240, 144, 120, 119, 60, 59 Hz
- 1920 x 1080 @ 240, 144, 120, 119, 60, 59, 50, 30, 29 Hz
- 1600 x 1200 @ 240, 144, 120, 119, 60, 59 Hz
- 1680 x 1050 @ 240, 144, 120, 119, 60, 59, 50, 30, 29 Hz
- 1600 x 1024 @ 240, 144, 120, 119, 60, 59, 50, 30, 29 Hz
- 1440 x 1080 @ 240, 144, 120, 119, 60, 59, 50, 30, 29 Hz
- 1600 x 900 @ 240, 144, 120, 119, 60 Hz
- 1280 x 1024 @ 240, 144, 120, 119, 75, 60 Hz
- 1280 x 960 @ 240, 144, 120, 119, 75, 60 Hz
- 1366 x 768 @ 240, 144, 120, 119, 60 Hz
- 1360 x 768 @ 240, 144, 120, 119, 60 Hz
- 1280 x 800 @ 240, 144, 120, 119, 75, 60 Hz
- 1152 x 864 @ 240, 144, 120, 119, 75 Hz
- 1280 x 768 @ 240, 144, 120, 119, 75, 60 Hz
- 1280 x 720 @ 240, 144, 120, 119, 60, 59, 50 Hz
- 1024 x 768 @ 240, 144, 120, 119, 75, 60 Hz
- 1176 x 664 @ 240, 144, 120, 119, 60, 59, 50 Hz
- 800 x 600 @ 240, 144, 120, 119, 75, 60 Hz
- 720 x 576 @ 240, 144, 120, 119, 50 Hz
- 720 x 480 @ 240, 144, 120, 119, 60, 59 Hz
- 640 x 480 @ 240, 144, 120, 119, 75, 60, 59 Hz

</details>

#### Observed connection (may differ between setups)

| | |
|---|---|
| Active signal mode | 3840 x 2160 @ 240 Hz |
| Pixel clock | 2427.96 MHz |
| Pixel density at current resolution | 165 PPI |
| Windows rendering | 144 DPI (150% scaling) |

---

Submitted from DispCtrl. Serial number, device path, file paths, user name are not included. Model capabilities and the observed signal/scaling are included; brightness, wallpaper and app settings are not.
