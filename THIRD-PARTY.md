# Third-party notices

Peripheral Battery 0.2.0-preview.5 is an unofficial Windows peripheral battery utility.
Original application code is MIT-licensed; upstream notices remain applicable.

## Protocol implementations

- BlackShark packet framing and reply validation are adapted from
  havioo/scythe, commit ee0cee03c7d83e628a78ef25ceeb4991f38a7430:
  https://github.com/havioo/scythe
  MIT license: docs/licenses/LICENSE-scythe.txt.
- Razer mouse/keyboard feature reports, legacy headset protocols and ASUS
  device identifiers are adapted from HeyOkay/HaloBattery,
  commits 2251f0534a8e886c9692ead42056059b4f69d8c0 and
  73e229cbee2e97fad659fcd96c78afdfa78488fb (six-brand expansion):
  https://github.com/HeyOkay/HaloBattery
  MIT license: docs/licenses/LICENSE-halobattery.txt.
- Logitech HID++/legacy, HyperX families, MCHOSE, ATK, AULA and VGN/VXE
  identifiers and battery protocols reference the pinned HaloBattery expansion
  commit above. G7 filtering follows its spaced-sample calibration. The MIT
  notice is retained; the research checkout and device diagnostics are excluded.
- Mouse DPI command reference: OpenRazer, commit
  477f4d6d3ab7e9deb6310d5bd3742560c5cbe80d, driver/razerchromacommon.c and
  driver/razermouse_driver.c. Only the read-only wire command and numeric
  encoding are independently implemented; OpenRazer source is not bundled.
  https://github.com/openrazer/openrazer
- Azoth X power, transport and OMNI-slot wire-format references:
  ASUS Gear Link 7204 v1.00.36, bundle 7204-1788853886-139979;
  seerge/g-helper commit f9ebe939edd0ddd78b4cf0e302b4bcf28c8337b5,
  app/Peripherals/Keyboard/Models/Azoth.cs and AsusKeyboard.cs.
  Independently encoded read commands: 12:03, 12:01, A0:00.
  Upstream source is not bundled.
  https://gearlink.asus.com/view/7204/manifest.json
  https://github.com/seerge/g-helper
- Additional ASUS model identifiers, power field offsets, mouse gauge scales
  and headset read commands were cross-checked against seerge/g-helper,
  commit a4efd01c8ba10d103da04c4c895ef849a96f6437, app/Peripherals/.
  Only protocol facts are independently encoded; no GHelper GPL source is
  copied or bundled. Razer model/interface references were cross-checked
  against the OpenRazer commit above; no OpenRazer GPL source is bundled.
- Bluetooth charging fields use Battery Service 1.1, cross-checked against
  Zephyr commit 36118765dd7f40a6c81dc9e281996346245fac7c.
  No Zephyr implementation is copied.
  https://www.bluetooth.com/specifications/specs/battery-service-1-1/

## Icons and public artwork

UI SVG icons are from lucide-icons/lucide 0.468.0.
Product illustrations are rasterizations of keyboard.svg, mouse.svg and
headphones.svg. Application and tray PNG/ICO battery artwork is original
project artwork, distributed under the project's MIT license.
ISC notice: docs/licenses/LICENSE-lucide.txt.
Feather-derived icons retain the MIT notice in docs/licenses/LICENSE-feather.txt.
https://github.com/lucide-icons/lucide/tree/0.468.0
https://github.com/feathericons/feather/tree/v4.29.2

Manufacturer product photographs and the user's private design reference are
excluded from both public source and public binary packages. Device names
identify supported products and do not imply manufacturer endorsement.
At runtime the application may download model-specific photographs and color
variants from Razer / ROG official pages and official image hosts into a local
cache. Such photographs and trademarks remain the property of their respective
rights holders; the project's MIT license does not license manufacturer artwork.
Website availability, page structure and available variants may change.

## Microsoft runtime

The prepared win-x64 ZIP contains the Microsoft.NETCore.App.Runtime.win-x64
and Microsoft.WindowsDesktop.App.Runtime.win-x64 8.0.33 runtime libraries.
It also contains Microsoft.Windows.SDK.NET and WinRT.Runtime from the build.
Microsoft licenses and third-party notices are included under docs/licenses/.
These notices apply in addition to the application's MIT license.

## 新品牌运行时官网图片

Logitech、HP/HyperX、MCHOSE、ATK、VXE/VGN、AULA 产品图片在运行时从已核对的官方来源下载，
具体地址及系列图限制见 docs/OFFICIAL-IMAGES.md 与 src/official-image-sources.json。
公开运行包与源码包仅提供地址和程序，不附带图片缓存；厂商图片不随本项目 MIT 许可重新授权。
