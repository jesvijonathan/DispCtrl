### LG Electronics FALCON

Device key: `GSM-5C89`

| | |
|---|---|
| Model | FALCON |
| Manufacturer | LG Electronics (GSM) |
| Manufacturer and product | GSM-5C89 |
| Controller type | 0x12 (raw 0x0012) |
| Connector | HDMI |
| Panel technology | LCD (TFT) |
| Physical size | 527 x 296 mm (23.8 in) |
| Highest mode | 2560 x 1440 @ 180 Hz |
| Bit depth | 10-bit per channel |
| Colour format | RGB |
| HDR | supported |
| Variable refresh | 48-180 Hz |
| DDC/CI | answers |
| Brightness over DDC/CI | yes |
| MCCS version | 2.0 |
| EDID manufacturer | LG Electronics (GSM) |
| EDID product | 5C89 |
| EDID name | LG ULTRAGEAR |
| Made | week 11 of 2025 |
| EDID version | 1.3 |

#### Controls it lists (31, 16 DispCtrl will drive)

- `0x02` New control value (read-only): 0 to 255
- `0x04` Restore factory defaults (read-only): 0 to 1
- `0x05` Restore factory brightness and contrast (read-only): 0 to 1
- `0x08` Restore factory colour defaults (read-only): 0 to 1
- `0x0B` Colour temperature increment (read-only): 0 to 65535
- `0x0C` Colour temperature request (range): 0 to 170 **(DispCtrl writes this)**
- `0x10` Brightness (range): 0 to 100 **(DispCtrl writes this)**
- `0x12` Contrast (range): 0 to 100 **(DispCtrl writes this)**
- `0x14` Colour preset (list): 0x01 sRGB, 0x04 5000 K, 0x05 6500 K, 0x06 7500 K, 0x07 8200 K, 0x08 9300 K, 0x0A 11500 K, 0x0B User 1 **(DispCtrl writes this)**
- `0x16` Red gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x18` Green gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x1A` Blue gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x6C` Red black level (range): 0 to 100 **(DispCtrl writes this)**
- `0x6E` Green black level (range): 0 to 100 **(DispCtrl writes this)**
- `0x70` Blue black level (range): 0 to 100 **(DispCtrl writes this)**
- `0xAC` Horizontal frequency (read-only): 0 to 65282
- `0xAE` Vertical frequency (read-only): 0 to 65535
- `0xB6` Display technology (read-only): 0 to 4
- `0xC0` Hours in use (read-only): 0 to 65535
- `0xC6` Application enable key (read-only): 0 to 65535
- `0xC8` Display controller type (read-only): 0 to 65535
- `0xC9` Firmware level (read-only): 0 to 65535
- `0xCA` OSD and power button lock (list): 0x01 Unlocked, 0x02 Locked **(DispCtrl writes this)**
- `0xCC` OSD language (list): 0x00, 0x02 English, 0x03 French, 0x04 German, 0x05 Italian, 0x08 Portuguese, 0x09 Russian, 0x0A Spanish, 0x0D Chinese (simplified) **(DispCtrl writes this)**
- `0xD6` Power mode (list): 0x01 On, 0x04 Off (soft) **(DispCtrl writes this)**
- `0xDC` Picture mode (list): 0x00 Standard, 0x01 Productivity, 0x02 Mixed, 0x03 Movie, 0x04 User
- `0xDF` MCCS version (read-only): 0 to 65535
- `0x60` Input source (list): 0x01 VGA 1, 0x03 DVI 1 **(DispCtrl writes this)**
- `0x62` Speaker volume (range): 0 to 100 **(DispCtrl writes this)**
- `0x8D` Audio mute (list): 0x01 Mute, 0x02 Unmute **(DispCtrl writes this)**
- `0xFF` Manufacturer-specific control FF (read-only): 0 to 1

Low-level commands it accepts: 0x01 0x02 0x03 0x07 0x0C 0x4E 0xF3 0xE3

#### Capabilities string

```
(prot(monitor)type(lcd)model(FALCON)cmds(01 02 03 07 0C 4E F3 E3)vcp(02 04 05 08 0B 0C 10 12 14(01 04 05 06 07 08 0A 0B) 16 18 1A 6C 6E 70 AC AE B6 C0 C6 C8 C9 CA CC(00 02 03 04 05 08 09 0A 0D) D6(01 04) DC(00 01 02 03 04) DF 60(01 03) 62 8D FF)mswhql(1)mccs_ver(2.0)asset_eep(32)mpu_ver(01))
```

<details><summary>Modes the driver reports (16)</summary>

- 2560 x 1440 @ 75 Hz
- 1920 x 1200 @ 75 Hz
- 1920 x 1080 @ 144, 120, 119, 100, 60, 59, 50 Hz
- 1600 x 1200 @ 75 Hz
- 1680 x 1050 @ 180, 144, 120, 119, 100, 60, 59, 50 Hz
- 1760 x 990 @ 180, 144, 120, 119, 100, 60, 59, 50 Hz
- 1600 x 900 @ 180, 144, 120, 119, 100, 60, 59, 50 Hz
- 1280 x 1024 @ 180, 144, 120, 119, 100, 75, 60, 59 Hz
- 1366 x 768 @ 180, 144, 120, 119, 100, 60, 59, 50 Hz
- 1280 x 720 @ 180, 144, 120, 119, 100, 60, 59, 50 Hz
- 1024 x 768 @ 180, 144, 120, 119, 100, 75, 60 Hz
- 1128 x 634 @ 180, 144, 120, 119, 100, 60, 59 Hz
- 800 x 600 @ 180, 144, 120, 119, 100, 75, 60 Hz
- 720 x 576 @ 180, 144, 120, 119, 100, 60, 59, 50 Hz
- 720 x 480 @ 180, 144, 120, 119, 100, 60, 59 Hz
- 640 x 480 @ 180, 144, 120, 119, 100, 75, 60, 59 Hz

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
