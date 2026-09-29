### LG Electronics LG ULTRAWIDE

Device key: `GSM-5BF7`

| | |
|---|---|
| Model | LG ULTRAWIDE |
| Manufacturer | LG Electronics (GSM) |
| Manufacturer and product | GSM-5BF7 |
| Controller type | 0x09 (raw 0x0009) |
| Connector | HDMI |
| Panel technology | LCD (TFT) |
| Physical size | 673 x 284 mm (28.8 in) |
| Highest mode | 3840 x 2160 @ 100 Hz |
| Bit depth | 8-bit per channel |
| Colour format | RGB |
| HDR | supported |
| Variable refresh | 56-100 Hz |
| DDC/CI | answers |
| Brightness over DDC/CI | yes |
| MCCS version | 2.1 |
| EDID manufacturer | LG Electronics (GSM) |
| EDID product | 5BF7 |
| EDID name | LG ULTRAWIDE |
| Made | week 8 of 2025 |
| EDID version | 1.3 |

#### Controls it lists (46, 9 DispCtrl will drive)

- `0x02` New control value (read-only): 0 to 2
- `0x04` Restore factory defaults (read-only): 0 to 255
- `0x05` Restore factory brightness and contrast (read-only): 0 to 1
- `0x08` Restore factory colour defaults (read-only): 0 to 255
- `0x10` Brightness (range): 0 to 100 **(DispCtrl writes this)**
- `0x12` Contrast (range): 0 to 100 **(DispCtrl writes this)**
- `0x14` Colour preset (list): 0x05 6500 K, 0x08 9300 K, 0x0B User 1 **(DispCtrl writes this)**
- `0x16` Red gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x18` Green gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x1A` Blue gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x52` Active control (read-only): 0 to 255
- `0x60` Input source (list): 0x11 HDMI 1, 0x12 HDMI 2, 0x0F DisplayPort 1
- `0xAC` Horizontal frequency (read-only): 0 to 1
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
- `0x15` Manufacturer-specific control 15 (read-only): 0x01, 0x06, 0x11, 0x13, 0x14, 0x28, 0x29, 0x32, 0x48
- `0xF7` Manufacturer-specific control F7 (read-only): 0x00, 0x01, 0x02, 0x03
- `0xF8` Manufacturer-specific control F8 (read-only): 0x00, 0x01
- `0xF9` Manufacturer-specific control F9 (read-only): 0 to 255
- `0xE4` Manufacturer-specific control E4 (read-only): 0 to 255
- `0xE5` Manufacturer-specific control E5 (read-only)
- `0xE6` Manufacturer-specific control E6 (read-only)
- `0xE7` Manufacturer-specific control E7 (read-only): 0 to 65535
- `0xE8` Manufacturer-specific control E8 (read-only): 0 to 255
- `0xE9` Manufacturer-specific control E9 (read-only): 0 to 255
- `0xEA` Manufacturer-specific control EA (read-only): 0 to 255
- `0xEB` Manufacturer-specific control EB (read-only): 0 to 1
- `0xEF` Manufacturer-specific control EF (read-only): 0 to 65535
- `0xFD` Manufacturer-specific control FD (read-only): 0x00, 0x01
- `0xFE` Manufacturer-specific control FE (read-only): 0x00, 0x01, 0x02
- `0xFF` Manufacturer-specific control FF (read-only)

#### Capabilities string

```
(prot(monitor)type(lcd)WQ650cmds(01 02 03 0C E3 F3)vcp(02 04 05 08 10 12 14(05 08 0B ) 16 18 1A 52 60( 11 12 0F) AC AE B2 B6 C0 C6 C8 C9 D6(01 04) DF 62 8D F4 F5(01 02 03 04) F6(00 01 02) 4D 4E 4F 15(01 06 11 13 14 28 29 32 48) F7(00 01 02 03) F8(00 01) F9 E4 E5 E6 E7 E8 E9 EA EB EF FD(00 01) FE(00 01 02) FF)mccs_ver(2.1)mswhql(1))
```

<details><summary>Modes the driver reports (20)</summary>

- 3840 x 2160 @ 30, 29, 25, 24, 23 Hz
- 2560 x 1440 @ 60, 59 Hz
- 2560 x 1080 @ 100, 75, 60, 50 Hz
- 1920 x 1080 @ 100, 60, 59, 50, 30, 29, 24, 23 Hz
- 1680 x 1050 @ 100, 60, 59, 50, 30, 29, 24, 23 Hz
- 1620 x 1080 @ 100 Hz
- 1440 x 1080 @ 100, 60, 59, 50, 30, 29, 24, 23 Hz
- 1280 x 1024 @ 100, 60, 59, 50, 30, 29, 24, 23 Hz
- 1080 x 1080 @ 100 Hz
- 1366 x 768 @ 100, 60, 59, 50, 30, 29, 24, 23 Hz
- 1360 x 768 @ 100, 60, 59, 50, 30, 29, 24, 23 Hz
- 1280 x 800 @ 100, 60, 59, 50, 30, 29, 24, 23 Hz
- 1280 x 768 @ 100, 60, 59, 50, 30, 29, 24, 23 Hz
- 1280 x 720 @ 100, 60, 59 Hz
- 1024 x 768 @ 100, 60 Hz
- 1176 x 664 @ 100, 60, 59 Hz
- 800 x 600 @ 100, 60 Hz
- 720 x 576 @ 100, 50 Hz
- 720 x 480 @ 100, 60, 59 Hz
- 640 x 480 @ 100, 60, 59 Hz

</details>

#### Observed connection (may differ between setups)

| | |
|---|---|
| Active signal mode | 2560 x 1080 @ 100 Hz |
| Pixel clock | 299.97 MHz |
| Pixel density at current resolution | 97 PPI |
| Windows rendering | 96 DPI (100% scaling) |

---

Submitted from DispCtrl v0.1.5. Serial number, device path, file paths, user name are not included. Model capabilities and the observed signal/scaling are included; brightness, wallpaper and app settings are not.
