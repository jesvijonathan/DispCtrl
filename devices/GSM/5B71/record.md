### LG Electronics GL63T

Device key: `GSM-5B71`

| | |
|---|---|
| Model | GL63T |
| Manufacturer | LG Electronics (GSM) |
| Manufacturer and product | GSM-5B71 |
| Controller type | 0x05 (raw 0x0005) |
| Connector | DisplayPort |
| Panel technology | LCD (TFT) |
| Physical size | 597 x 336 mm (27.0 in) |
| Highest mode | 1080 x 1920 @ 144 Hz |
| Bit depth | 10-bit per channel |
| Colour format | RGB |
| HDR | supported |
| Variable refresh | 56-144 Hz |
| DDC/CI | answers |
| Brightness over DDC/CI | yes |
| MCCS version | 2.1 |
| EDID manufacturer | LG Electronics (GSM) |
| EDID product | 5B71 |
| EDID name | LG ULTRAGEAR |
| Made | week 4 of 2021 |
| EDID version | 1.4 |

#### Controls it lists (39, 9 DispCtrl will drive)

- `0x02` New control value (read-only): 0 to 2
- `0x04` Restore factory defaults (read-only): 0 to 255
- `0x05` Restore factory brightness and contrast (read-only): 0 to 1
- `0x08` Restore factory colour defaults (read-only): 0 to 255
- `0x10` Brightness (range): 0 to 100 **(DispCtrl writes this)**
- `0x12` Contrast (range): 0 to 100 **(DispCtrl writes this)**
- `0x14` Colour preset (list): 0x05 6500 K, 0x08 9300 K, 0x0B User 1
- `0x16` Red gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x18` Green gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x1A` Blue gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x52` Active control (read-only): 0 to 255
- `0x60` Input source (list): 0x11 HDMI 1, 0x0F DisplayPort 1 **(DispCtrl writes this)**
- `0xAC` Horizontal frequency (read-only): 0 to 2
- `0xAE` Vertical frequency (read-only)
- `0xB2` Flat panel sub-pixel layout (read-only)
- `0xB6` Display technology (read-only): 0 to 5
- `0xC0` Hours in use (read-only): 0 to 65535
- `0xC6` Application enable key (read-only): 0 to 65535
- `0xC8` Display controller type (read-only): 0 to 255
- `0xC9` Firmware level (read-only): 0 to 65535
- `0xD6` Power mode (list): 0x01 On, 0x04 Off (soft) **(DispCtrl writes this)**
- `0xDF` MCCS version (read-only): 0 to 255
- `0x62` Speaker volume (range): 0 to 100 **(DispCtrl writes this)**
- `0x8D` Audio mute (list): 0x01 Mute, 0x02 Unmute **(DispCtrl writes this)**
- `0xF4` Manufacturer-specific control F4 (read-only): 0 to 65535
- `0xF5` Manufacturer-specific control F5 (read-only): 0x01, 0x02, 0x03, 0x04
- `0xF6` Manufacturer-specific control F6 (read-only): 0x00, 0x01, 0x02
- `0x4D` Manufacturer-specific control 4D (read-only): 0 to 65535
- `0x4E` Manufacturer-specific control 4E (read-only): 0 to 65535
- `0x4F` Manufacturer-specific control 4F (read-only): 0 to 65535
- `0x15` Manufacturer-specific control 15 (read-only): 0x01, 0x06, 0x11, 0x13, 0x14, 0x15, 0x18, 0x19, 0x20, 0x22, 0x23, 0x24, 0x28, 0x29, 0x32, 0x48
- `0xF7` Manufacturer-specific control F7 (read-only): 0x00, 0x01, 0x02, 0x03
- `0xF8` Manufacturer-specific control F8 (read-only): 0x00, 0x01
- `0xF9` Manufacturer-specific control F9 (read-only): 0 to 255
- `0xEF` Manufacturer-specific control EF (read-only): 0 to 65535
- `0xFA` Manufacturer-specific control FA (read-only): 0x00, 0x01
- `0xFD` Manufacturer-specific control FD (read-only): 0x00, 0x01
- `0xFE` Manufacturer-specific control FE (read-only): 0x00, 0x01, 0x02
- `0xFF` Manufacturer-specific control FF (read-only)

Low-level commands it accepts: 0x01 0x02 0x03 0x0C 0xE3 0xF3

#### Capabilities string

```
(prot(monitor)type(lcd)model(GL63T)cmds(01 02 03 0C E3 F3)vcp(02 04 05 08 10 12 14(05 08 0B ) 16 18 1A 52 60(11 0F ) AC AE B2 B6 C0 C6 C8 C9 D6(01 04) DF 62 8D F4 F5(01 02 03 04) F6(00 01 02) 4D 4E 4F 15(01 06 11 13 14 15 18 19 20 22 23 24 28 29 32 48) F7(00 01 02 03) F8(00 01) F9 EF FA(00 01) FD(00 01) FE(00 01 02) FF)mccs_ver(2.1)mswhql(1))
```

<details><summary>Modes the driver reports (16)</summary>

- 1080 x 1920 @ 144, 120, 119, 100, 75, 60, 59, 50 Hz
- 1050 x 1680 @ 144, 120, 119, 100, 75, 60, 59, 50 Hz
- 990 x 1760 @ 144, 120, 119, 100, 75, 60, 59, 50 Hz
- 900 x 1600 @ 144, 120, 119, 100, 75, 60, 59, 50 Hz
- 1024 x 1280 @ 144, 120, 119, 100, 75, 60, 59 Hz
- 900 x 1440 @ 144, 120, 119, 100, 75, 60, 59 Hz
- 960 x 1280 @ 144, 120, 119, 100, 75, 60, 59 Hz
- 768 x 1366 @ 144, 120, 119, 100, 75, 60, 59 Hz
- 870 x 1152 @ 144, 120, 119, 100, 75, 60, 59 Hz
- 720 x 1280 @ 144, 120, 119, 100, 75, 60, 59, 50 Hz
- 768 x 1024 @ 144, 120, 119, 100, 75, 60, 59 Hz
- 634 x 1128 @ 144, 120, 119, 100, 75, 60, 59 Hz
- 600 x 800 @ 144, 120, 119, 100, 75, 60, 59 Hz
- 480 x 720 @ 144, 120, 119, 100, 75, 60, 59 Hz
- 480 x 640 @ 144, 120, 119, 100, 75, 60, 59 Hz
- 400 x 720 @ 144, 120, 119, 100, 75, 70, 60, 59 Hz

</details>

#### Observed connection (may differ between setups)

| | |
|---|---|
| Active signal mode | 1920 x 1080 @ 120 Hz |
| Pixel clock | 285.5 MHz |
| Pixel density at current resolution | 46 PPI |
| Windows rendering | 96 DPI (100% scaling) |

---

Submitted from DispCtrl v0.2.1. Serial number, device path, file paths, user name are not included. Model capabilities and the observed signal/scaling are included; brightness, wallpaper and app settings are not.
