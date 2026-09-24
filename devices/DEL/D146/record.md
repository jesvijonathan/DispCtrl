### Dell P2723DE

Device key: `DEL-D146`

| | |
|---|---|
| Model | P2723DE |
| Manufacturer | Dell (DEL) |
| Manufacturer and product | DEL-D146 |
| Controller type | 0x05 (raw 0x5605) |
| Connector | DisplayPort |
| Panel technology | LCD (TFT) |
| Physical size | 597 x 336 mm (27.0 in) |
| Highest mode | 2560 x 1440 @ 75 Hz |
| Bit depth | 8-bit per channel |
| Colour format | RGB |
| HDR | supported |
| Variable refresh | 49-75 Hz |
| DDC/CI | answers |
| Brightness over DDC/CI | yes |
| MCCS version | 2.1 |
| EDID manufacturer | Dell (DEL) |
| EDID product | D146 |
| EDID name | DELL P2723DE |
| Made | week 11 of 2024 |
| EDID version | 1.4 |

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
- `0x60` Input source (list): 0x1B, 0x0F DisplayPort 1, 0x11 HDMI 1 **(DispCtrl writes this)**
- `0xAA` Screen orientation (read-only): 0x01, 0x02, 0x04
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
- `0xDF` MCCS version (read-only): 0 to 255
- `0xE0` Manufacturer-specific control E0 (read-only): 0 to 1
- `0xE1` Manufacturer-specific control E1 (read-only): 0 to 1
- `0xE2` Manufacturer-specific control E2 (read-only): 0x00, 0x02, 0x04, 0x0E, 0x12, 0x14
- `0xF1` Manufacturer-specific control F1 (read-only): 0 to 16651
- `0xF2` Manufacturer-specific control F2 (read-only): 0 to 65280
- `0xFE` Manufacturer-specific control FE (read-only)
- `0xFD` Manufacturer-specific control FD (read-only): 0 to 65535

Low-level commands it accepts: 0x01 0x02 0x03 0x07 0x0C 0xE3 0xF3

#### Capabilities string

```
(prot(monitor)type(lcd)model(P2723DE)cmds(01 02 03 07 0C E3 F3)vcp(02 04 05 08 10 12 14(05 08 0B 0C) 16 18 1A 52 60( 1B 0F 11) AA(01 02 04) AC AE B2 B6 C6 C8 C9 CC(02 03 04 06 09 0A 0D 0E) D6(01 04 05) DC(00 03 05) DF E0 E1 E2(00 02 04 0E 12 14) F1 F2 FE FD)mccs_ver(2.1)mswhql(1))
```

<details><summary>Modes the driver reports (27)</summary>

- 2560 x 1440 @ 60, 59 Hz
- 1920 x 1440 @ 60, 59 Hz
- 1856 x 1392 @ 60, 59 Hz
- 1792 x 1344 @ 60, 59 Hz
- 2048 x 1152 @ 60, 59 Hz
- 1920 x 1200 @ 60, 59 Hz
- 2048 x 1080 @ 60, 59 Hz
- 1920 x 1080 @ 60, 59, 50 Hz
- 1600 x 1200 @ 60, 59 Hz
- 1680 x 1050 @ 60, 59 Hz
- 1400 x 1050 @ 60, 59 Hz
- 1600 x 900 @ 60, 59 Hz
- 1280 x 1024 @ 75, 60, 59 Hz
- 1440 x 900 @ 60, 59 Hz
- 1280 x 960 @ 60, 59 Hz
- 1366 x 768 @ 60, 59 Hz
- 1360 x 768 @ 60, 59 Hz
- 1280 x 800 @ 60, 59 Hz
- 1152 x 864 @ 75, 60, 59 Hz
- 1280 x 768 @ 60, 59 Hz
- 1280 x 720 @ 60, 59, 50 Hz
- 1024 x 768 @ 75, 60, 59 Hz
- 1280 x 600 @ 60, 59 Hz
- 800 x 600 @ 75, 60, 59 Hz
- 720 x 576 @ 60, 59, 50 Hz
- 720 x 480 @ 60, 59 Hz
- 640 x 480 @ 75, 60, 59 Hz

</details>

#### Observed connection (may differ between setups)

| | |
|---|---|
| Active signal mode | 2560 x 1440 @ 59.951 Hz |
| Pixel clock | 241.5 MHz |
| Pixel density at current resolution | 109 PPI |
| Windows rendering | 96 DPI (100% scaling) |

---

Submitted from DispCtrl. Serial number, device path, file paths, user name are not included. Model capabilities and the observed signal/scaling are included; brightness, wallpaper and app settings are not.
