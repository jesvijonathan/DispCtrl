### BenQ XL2566K

Device key: `BNQ-7FB7`

| | |
|---|---|
| Model | XL2566K |
| Manufacturer | BenQ (BNQ) |
| Manufacturer and product | BNQ-7FB7 |
| Controller type | 0x09 (raw 0x1909) |
| Connector | DisplayPort |
| Panel technology | LCD (TFT) |
| Physical size | 544 x 303 mm (24.5 in) |
| Highest mode | 1920 x 1080 @ 360 Hz |
| Bit depth | 8-bit per channel |
| Colour format | RGB |
| HDR | supported |
| Variable refresh | 303-360 Hz |
| DDC/CI | answers |
| Brightness over DDC/CI | yes |
| MCCS version | 2.2 |
| EDID manufacturer | BenQ (BNQ) |
| EDID product | 7FB7 |
| EDID name | ZOWIE XL LCD |
| Made | week 42 of 2022 |
| EDID version | 1.4 |

#### Controls it lists (49, 10 DispCtrl will drive)

- `0x04` Restore factory defaults (read-only): 0 to 1
- `0x08` Restore factory colour defaults (read-only): 0 to 1
- `0x10` Brightness (range): 0 to 100 **(DispCtrl writes this)**
- `0x12` Contrast (range): 0 to 100 **(DispCtrl writes this)**
- `0x14` Colour preset (list): 0x04 5000 K, 0x05 6500 K, 0x08 9300 K, 0x0B User 1 **(DispCtrl writes this)**
- `0x4D` Manufacturer-specific control 4D (read-only): 0x00, 0x01, 0x02
- `0x4E` Manufacturer-specific control 4E (read-only): 0 to 1
- `0x4F` Manufacturer-specific control 4F (read-only): 0 to 1
- `0x60` Input source (list): 0x0F DisplayPort 1, 0x11 HDMI 1, 0x12 HDMI 2 **(DispCtrl writes this)**
- `0x62` Speaker volume (range): 0 to 100 **(DispCtrl writes this)**
- `0x72` Gamma (list): 0x50, 0x64, 0x78, 0x8C, 0xA0 **(DispCtrl writes this)**
- `0x7A` Manufacturer-specific control 7A (read-only): 0 to 20
- `0x86` Manufacturer-specific control 86 (read-only): 0x01, 0x02, 0x05, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10, 0x11, 0x12, 0x13
- `0x87` Sharpness (range): 0 to 10 **(DispCtrl writes this)**
- `0x89` Manufacturer-specific control 89 (read-only): 0 to 20
- `0x8D` Audio mute (list): 0x01 Mute, 0x02 Unmute **(DispCtrl writes this)**
- `0xAC` Horizontal frequency (read-only): 0 to 7
- `0xAE` Vertical frequency (read-only): 0 to 65535
- `0xB2` Flat panel sub-pixel layout (read-only): 0 to 2
- `0xB5` Manufacturer-specific control B5 (read-only): 0 to 1
- `0xB6` Display technology (read-only): 0 to 5
- `0xC6` Application enable key (read-only): 0 to 65535
- `0xC8` Display controller type (read-only): 0 to 39
- `0xCC` OSD language (list): 0x01 Chinese (traditional), 0x02 English, 0x03 French, 0x04 German, 0x05 Italian, 0x06 Japanese, 0x07 Korean, 0x08 Portuguese, 0x09 Russian, 0x0A Spanish, 0x0B Swedish, 0x0D Chinese (simplified), 0x0F Arabic, 0x11 Croatian, 0x12 Czech, 0x14 Dutch, 0x1A Hungarian, 0x1E Polish, 0x1F Romanian **(DispCtrl writes this)**
- `0xCD` Manufacturer-specific control CD (read-only): 0x00, 0x02
- `0xD2` Manufacturer-specific control D2 (read-only): 0 to 255
- `0xD9` Manufacturer-specific control D9 (read-only): 0x18, 0x19, 0x1A
- `0xDC` Picture mode (list): 0x00 Standard, 0x03 Movie, 0x15, 0x16, 0x18, 0x19, 0x1A, 0x1B **(DispCtrl writes this)**
- `0xDF` MCCS version (read-only): 0 to 65535
- `0xE0` Manufacturer-specific control E0 (read-only): 0 to 255
- `0xE1` Manufacturer-specific control E1 (read-only): 0 to 255
- `0xE2` Manufacturer-specific control E2 (read-only): 0 to 255
- `0xE3` Manufacturer-specific control E3 (read-only): 0 to 255
- `0xE4` Manufacturer-specific control E4 (read-only): 0 to 255
- `0xE5` Manufacturer-specific control E5 (read-only): 0 to 255
- `0xE7` Manufacturer-specific control E7 (read-only): 0 to 255
- `0xE8` Manufacturer-specific control E8 (read-only): 0 to 65535
- `0xED` Manufacturer-specific control ED (read-only): 0x18, 0x19, 0x1A
- `0xEE` Manufacturer-specific control EE (read-only): 0x00, 0x01, 0x02
- `0xF0` Manufacturer-specific control F0 (read-only): 0x00, 0x01, 0x02, 0x03
- `0xF3` Manufacturer-specific control F3 (read-only): 0x00, 0x01, 0x02, 0x03, 0x04
- `0xF4` Manufacturer-specific control F4 (read-only): 0x00, 0x01, 0x02, 0x03
- `0xF5` Manufacturer-specific control F5 (read-only): 0 to 100
- `0xF6` Manufacturer-specific control F6 (read-only): 0x00, 0x01
- `0xF7` Manufacturer-specific control F7 (read-only): 0x00, 0x02
- `0xF8` Manufacturer-specific control F8 (read-only): 0x00, 0x0A, 0x14, 0x1E
- `0xF9` Manufacturer-specific control F9 (read-only): 0 to 25700
- `0xFA` Manufacturer-specific control FA (read-only): 0x00, 0x01
- `0xFB` Manufacturer-specific control FB (read-only): 0 to 3

Low-level commands it accepts: 0x01 0x02 0x03 0x07 0x0C 0xE3 0xF3

#### Capabilities string

```
(prot(monitor)type(LCD)model(XL2566K)cmds(01 02 03 07 0C E3 F3)vcp(04 08 10 12 14(04 05 08 0B) 4D(00 01 02) 4E 4F 60(0F 11 12) 62 72(50 64 78 8C A0) 7A 86(01 02 05 0B 0C 0D 0E 0F 10 11 12 13) 87 89 8D(01 02) AC AE B2 B5 B6 C6 C8 CC(01 02 03 04 05 06 07 08 09 0A 0B 0D 0F 11 12 14 1A 1E 1F) CD(00 02) D2 D9(18 19 1A) DC(00 03 15 16 18 19 1A 1B) DF E0 E1 E2 E3 E4 E5 E7 E8 ED(18 19 1A) EE(00 01 02) F0(00 01 02 03) F3(00 01 02 03 04) F4(00 01 02 03) F5 F6(00 01) F7(00 02) F8(00 0A 14 1E) F9 FA(00 01) FB)mswhql(1)asset_eep(40)mccs_ver(2.2))
```

<details><summary>Modes the driver reports (19)</summary>

- 1920 x 1080 @ 360, 240, 144, 120, 119, 100, 60, 59, 50, 30, 29, 25, 24, 23 Hz
- 1680 x 1050 @ 360, 240, 144, 120, 119, 100, 60, 59 Hz
- 1600 x 1024 @ 360, 240, 144, 120, 119, 100, 60, 59 Hz
- 1440 x 1080 @ 360, 240, 144, 120, 119, 100, 60, 59, 50, 30, 29, 25, 24, 23 Hz
- 1600 x 900 @ 360, 240, 144, 120, 119, 100, 60 Hz
- 1280 x 1024 @ 360, 240, 144, 120, 119, 100, 75, 60 Hz
- 1280 x 960 @ 360, 240, 144, 120, 119, 100 Hz
- 1366 x 768 @ 360, 240, 144, 120, 119, 100, 60 Hz
- 1360 x 768 @ 360, 240, 144, 120, 119, 100, 60 Hz
- 1280 x 800 @ 360, 240, 144, 120, 119, 100, 60 Hz
- 1152 x 864 @ 360, 240, 144, 120, 119, 100 Hz
- 1280 x 768 @ 360, 240, 144, 120, 119, 100, 60 Hz
- 1280 x 720 @ 360, 240, 144, 120, 119, 100, 60, 59, 50 Hz
- 1024 x 768 @ 360, 240, 144, 120, 119, 100, 75, 60 Hz
- 1176 x 664 @ 360, 240, 144, 120, 119, 100, 60, 59, 50 Hz
- 800 x 600 @ 360, 240, 144, 120, 119, 100, 75, 60 Hz
- 720 x 576 @ 360, 240, 144, 120, 119, 100, 50 Hz
- 720 x 480 @ 360, 240, 144, 120, 119, 100, 60, 59 Hz
- 640 x 480 @ 360, 240, 144, 120, 119, 100, 75, 60, 59 Hz

</details>

#### Observed connection (may differ between setups)

| | |
|---|---|
| Active signal mode | 1920 x 1080 @ 360 Hz |
| Pixel clock | 932.4 MHz |
| Pixel density at current resolution | 90 PPI |
| Windows rendering | 96 DPI (100% scaling) |

---

Submitted from DispCtrl. Serial number, device path, file paths, user name are not included. Model capabilities and the observed signal/scaling are included; brightness, wallpaper and app settings are not.
