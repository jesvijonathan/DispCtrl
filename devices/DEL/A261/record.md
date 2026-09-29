### Dell P2425H

Device key: `DEL-A261`

| | |
|---|---|
| Model | P2425H |
| Manufacturer | Dell (DEL) |
| Manufacturer and product | DEL-A261 |
| Controller type | 0x05 (raw 0x5605) |
| Connector | HDMI |
| Panel technology | LCD (TFT) |
| Physical size | 527 x 296 mm (23.8 in) |
| Highest mode | 1920 x 1080 @ 100 Hz |
| Bit depth | 8-bit per channel |
| Colour format | RGB |
| HDR | supported |
| Variable refresh | 48-100 Hz |
| DDC/CI | answers |
| Brightness over DDC/CI | yes |
| MCCS version | 2.1 |
| EDID manufacturer | Dell (DEL) |
| EDID product | A261 |
| EDID name | DELL P2425H |
| Made | week 12 of 2024 |
| EDID version | 1.3 |

#### Controls it lists (31, 10 DispCtrl will drive)

- `0x02` New control value (read-only): 0 to 2
- `0x04` Restore factory defaults (read-only): 0 to 255
- `0x05` Restore factory brightness and contrast (read-only): 0 to 1
- `0x08` Restore factory colour defaults (read-only): 0 to 255
- `0x10` Brightness (range): 0 to 100 **(DispCtrl writes this)**
- `0x12` Contrast (range): 0 to 100 **(DispCtrl writes this)**
- `0x14` Colour preset (list): 0x05 6500 K, 0x08 9300 K, 0x0B User 1, 0x0C User 2 **(DispCtrl writes this)**
- `0x16` Red gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x18` Green gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x1A` Blue gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x52` Active control (read-only): 0 to 255
- `0x60` Input source (list): 0x01 VGA 1, 0x0F DisplayPort 1, 0x11 HDMI 1 **(DispCtrl writes this)**
- `0xAA` Screen orientation (read-only): 0x00, 0x01, 0x02, 0x04
- `0xAC` Horizontal frequency (read-only): 0 to 1
- `0xAE` Vertical frequency (read-only)
- `0xB2` Flat panel sub-pixel layout (read-only): 0 to 8
- `0xB6` Display technology (read-only): 0 to 5
- `0xC6` Application enable key (read-only): 0 to 65535
- `0xC8` Display controller type (read-only)
- `0xC9` Firmware level (read-only): 0 to 65535
- `0xCC` OSD language (list): 0x02 English, 0x03 French, 0x04 German, 0x06 Japanese, 0x09 Russian, 0x0A Spanish, 0x0D Chinese (simplified), 0x0E Portuguese (Brazil) **(DispCtrl writes this)**
- `0xD6` Power mode (list): 0x01 On, 0x04 Off (soft), 0x05 Off (hard) **(DispCtrl writes this)**
- `0xDC` Picture mode (list): 0x00 Standard, 0x03 Movie, 0x05 Games **(DispCtrl writes this)**
- `0xDF` MCCS version (read-only): 0 to 65535
- `0xE0` Manufacturer-specific control E0 (read-only): 0 to 1
- `0xE1` Manufacturer-specific control E1 (read-only): 0 to 1
- `0xE2` Manufacturer-specific control E2 (read-only): 0x00, 0x02, 0x04, 0x0E, 0x12, 0x14
- `0xF1` Manufacturer-specific control F1 (read-only): 0 to 255
- `0xF2` Manufacturer-specific control F2 (read-only): 0 to 255
- `0xFD` Manufacturer-specific control FD (read-only): 0 to 65535
- `0xFE` Manufacturer-specific control FE (read-only)

Low-level commands it accepts: 0x01 0x02 0x03 0x07 0x0C 0xE3 0xF3

#### Capabilities string

```
(prot(monitor)type(lcd)model(P2425H)cmds(01 02 03 07 0C E3 F3)vcp(02 04 05 08 10 12 14(05 08 0B 0C) 16 18 1A 52 60(01 0F 11) AA(00 01 02 04) AC AE B2 B6 C6 C8 C9 CC(02 03 04 06 09 0A 0D 0E) D6(01 04 05) DC(00 03 05 ) DF E0 E1 E2(00 02 04 0E 12 14) F1 F2 FD FE)mccs_ver(2.1)mswhql(1))
```

<details><summary>Modes the driver reports (19)</summary>

- 1920 x 1080 @ 100, 60, 59, 50 Hz
- 1680 x 1050 @ 100, 60, 59, 50 Hz
- 1400 x 1050 @ 100, 60, 59, 50 Hz
- 1600 x 900 @ 100, 60, 59, 50 Hz
- 1280 x 1024 @ 100, 75, 60, 59, 50 Hz
- 1440 x 900 @ 100, 60, 59, 50 Hz
- 1280 x 960 @ 100, 60, 59, 50 Hz
- 1366 x 768 @ 100, 60, 59, 50 Hz
- 1360 x 768 @ 100, 60, 59, 50 Hz
- 1280 x 800 @ 100, 60, 59, 50 Hz
- 1152 x 864 @ 100, 75, 60, 59, 50 Hz
- 1280 x 768 @ 100, 60, 59, 50 Hz
- 1280 x 720 @ 100, 75, 60, 59, 50 Hz
- 1024 x 768 @ 100, 75, 60, 59, 50 Hz
- 1280 x 600 @ 100, 60, 59, 50 Hz
- 800 x 600 @ 100, 75, 60, 59, 50 Hz
- 720 x 576 @ 100, 60, 59, 50 Hz
- 720 x 480 @ 100, 60, 59, 50 Hz
- 640 x 480 @ 100, 75, 60, 59, 50 Hz

</details>

#### Observed connection (may differ between setups)

| | |
|---|---|
| Active signal mode | 1920 x 1080 @ 60 Hz |
| Pixel clock | 148.5 MHz |
| Pixel density at current resolution | 93 PPI |
| Windows rendering | 96 DPI (100% scaling) |

---

Submitted from DispCtrl v0.1.5. Serial number, device path, file paths, user name are not included. Model capabilities and the observed signal/scaling are included; brightness, wallpaper and app settings are not.
