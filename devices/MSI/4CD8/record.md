### MSI _DDCCI_MODEL_NAME

Device key: `MSI-4CD8`

| | |
|---|---|
| Model | _DDCCI_MODEL_NAME |
| Manufacturer | MSI (MSI) |
| Manufacturer and product | MSI-4CD8 |
| Controller type | 0x09 (raw 0x0009) |
| Connector | DisplayPort |
| Panel technology | QD-OLED 3rd Gen |
| Physical size | 590 x 334 mm (26.7 in) |
| Highest mode | 2560 x 1440 @ 240 Hz |
| Bit depth | 10-bit per channel |
| Colour format | RGB |
| HDR | supported |
| Variable refresh | 48-240 Hz |
| DDC/CI | answers |
| Brightness over DDC/CI | yes |
| MCCS version | 2.2 |
| EDID manufacturer | MSI (MSI) |
| EDID product | 4CD8 |
| EDID name | MAG 271QPX E2 |
| Made | week 51 of 2023 |
| EDID version | 1.4 |

#### Controls it lists (28, 10 DispCtrl will drive)

- `0x02` New control value (read-only): 0 to 2
- `0x04` Restore factory defaults (read-only): 0 to 1
- `0x05` Restore factory brightness and contrast (read-only): 0 to 1
- `0x06` Restore factory geometry (read-only): 0 to 1
- `0x08` Restore factory colour defaults (read-only): 0 to 1
- `0x0B` Colour temperature increment (read-only)
- `0x0C` Colour temperature request (range): 0 to 63 **(DispCtrl writes this)**
- `0x10` Brightness (range): 0 to 100 **(DispCtrl writes this)**
- `0x12` Contrast (range): 0 to 100 **(DispCtrl writes this)**
- `0x14` Colour preset (list): 0x01 sRGB, 0x02 Display native, 0x04 5000 K, 0x05 6500 K, 0x06 7500 K, 0x08 9300 K, 0x0B User 1 **(DispCtrl writes this)**
- `0x16` Red gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x18` Green gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x1A` Blue gain (range): 0 to 100 **(DispCtrl writes this)**
- `0x52` Active control (read-only): 0 to 100
- `0x60` Input source (list): 0x01 VGA 1, 0x03 DVI 1, 0x04 DVI 2, 0x0F DisplayPort 1, 0x10 DisplayPort 2, 0x11 HDMI 1, 0x12 HDMI 2 **(DispCtrl writes this)**
- `0x87` Sharpness (range): 0 to 5 **(DispCtrl writes this)**
- `0xAC` Horizontal frequency (read-only): 0 to 60620
- `0xAE` Vertical frequency (read-only): 0 to 65535
- `0xB2` Flat panel sub-pixel layout (read-only): 0 to 1
- `0xB6` Display technology (read-only): 0 to 5
- `0xC6` Application enable key (read-only): 0 to 255
- `0xC8` Display controller type (read-only)
- `0xCA` OSD and power button lock (list): 0x01 Unlocked, 0x02 Locked **(DispCtrl writes this)**
- `0xCC` OSD language (list): 0x01 Chinese (traditional), 0x02 English, 0x03 French, 0x04 German, 0x06 Japanese, 0x0A Spanish, 0x0D Chinese (simplified)
- `0xD6` Power mode (list): 0x05 Off (hard)
- `0xDF` MCCS version (read-only): 0 to 65535
- `0xFD` Manufacturer-specific control FD (read-only)
- `0xFF` Manufacturer-specific control FF (read-only)

Low-level commands it accepts: 0x01 0x02 0x03 0x07 0x0C 0xE3 0xF3

#### Capabilities string

```
(prot(monitor)type(LCD)model(_DDCCI_MODEL_NAME)cmds(01 02 03 07 0C E3 F3)vcp(02 04 05 06 08 0B 0C 10 12 14(01 02 04 05 06 08 0B) 16 18 1A 52 60(01 03 04 0F 10 11 12) 87 AC AE B2 B6 C6 C8 CA CC(01 02 03 04 06 0A 0D) D6(05) DF FD FF)mswhql(1)asset_eep(40)mccs_ver(2.2))
```

<details><summary>Modes the driver reports (18)</summary>

- 2560 x 1440 @ 240, 120, 60 Hz
- 1920 x 1200 @ 240, 120, 60 Hz
- 1920 x 1080 @ 240, 120, 119, 60, 59 Hz
- 1600 x 1200 @ 240, 120, 60 Hz
- 1680 x 1050 @ 240, 120, 60 Hz
- 1760 x 990 @ 240, 120, 119, 60, 59 Hz
- 1600 x 900 @ 240, 120, 60 Hz
- 1280 x 1024 @ 240, 120, 75, 60 Hz
- 1440 x 900 @ 240, 120, 60 Hz
- 1280 x 960 @ 240, 120, 60 Hz
- 1366 x 768 @ 240, 120, 60 Hz
- 1152 x 864 @ 240, 120, 75, 60 Hz
- 1280 x 720 @ 240, 120, 60, 59, 50 Hz
- 1024 x 768 @ 240, 120, 75, 70, 60 Hz
- 1128 x 634 @ 240, 120, 75, 60 Hz
- 800 x 600 @ 240, 120, 75, 72, 60, 56 Hz
- 640 x 480 @ 240, 120, 75, 72, 67, 60, 59 Hz
- 720 x 400 @ 240, 120, 70, 60 Hz

</details>

#### Observed connection (may differ between setups)

| | |
|---|---|
| Active signal mode | 2560 x 1440 @ 240 Hz |
| Pixel clock | 1116.25 MHz |
| Pixel density at current resolution | 110 PPI |
| Windows rendering | 96 DPI (100% scaling) |

---

Submitted from DispCtrl v0.2.1. Serial number, device path, file paths, user name are not included. Model capabilities and the observed signal/scaling are included; brightness, wallpaper and app settings are not.
