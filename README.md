# Peripheral Battery Preview

Windows ROG / Razer 外设电量组件，0.2.0-preview.1 兼容测试版。
自动发现已连接设备，集中显示电量、连接方式、可读的充电状态与部分雷蛇鼠标 DPI。
这是扩大协议覆盖范围的测试版，不承诺所有型号、固件或接收器均可读取。

## 下载与运行

完整解压 `Peripheral-Battery-v0.2.0-preview.1-win-x64.zip` 到新的可写目录，
例如 `D:\PeripheralBatteryPreview`，双击 `PeriDock.exe`。
自带 .NET，无需安装 SDK。不要在压缩包内启动，也不要覆盖当前安装。

初次启动在通知区域常驻，悬浮条默认隐藏。右键图标可切换“显示悬浮窗”，
此选择会保存。悬停显示简短电量提示，单击打开基本信息，双击打开详情。
多设备时可滚动查看。测试版使用独立进程锁和程序目录中的设置、日志，
可以与原版共存；测试版暂不提供开机启动，避免覆盖原版启动注册。

## 兼容范围

精确 VID/PID 清单见 [docs/device-catalog.json](docs/device-catalog.json)。
其中 `protocolAvailable` 仅表示已接入对应协议；`hardwareVerified` 默认 false，
不作为全型号已验证的声明。不同 USB / 接收器 PID 可能属于同一型号。

| 类别 | 已接入的主要系列 | 限制 |
| --- | --- | --- |
| Razer 鼠标 | Viper、DeathAdder、Basilisk、Naga、Mamba、Lancehead、Pro Click、Cobra 等无线型号 | 按清单精确 PID；DPI 仅部分协议可读 |
| Razer 键盘 | BlackWidow V3 Pro、V3/V4 Mini HyperSpeed、V4 Tenkeyless HyperSpeed、DeathStalker V2 Pro/TKL | 接口与事务号按型号区分，新增型号待实机验证 |
| Razer 耳机 | BlackShark V3 Pro、V2 Pro 2020/2023 部分 PID、Barracuda Pro | 不能推广到所有黑鲨、梭鱼、北海巨妖或主机版本；0557 未适配 |
| ROG 键盘 | Azoth/X/Extreme/SE、Falchion/RX、Claymore II、部分 Strix Scope | OMNI 仅保留已验证过的 Azoth X 路由 |
| ROG / ASUS 鼠标 | Gladius III、Chakram/X、Keris Wireless、Harpe Ace Aim Lab、Spatha X、Pugio II、部分 Strix/TUF/TX | 部分旧型号仅提供 25% 档位估计；新 OMNI 型号未全面接入 |
| ROG 耳机 | Delta II/KJP、Pelta、Cetra SpeedNova | Cetra 显示左右耳较低电量，充电盒信息放在状态提示中 |
| 蓝牙 | 名称含 ROG / Razer 的已连接设备 | 优先标准 Battery Service；不提供服务的设备不能保证有电量 |

本机已读到：Viper V3 Pro 2.4G 电量和 1600 DPI、Azoth X 蓝牙电量。
既有版本曾验证上述鼠标、BlackShark V3 Pro 同时有线读取，以及 Azoth X USB 电量。
新增适配型号没有逐台实测；本轮 BlackShark V3 Pro 未连接，未完成新版耳机读数实测。

## 已知限制

- 未知型号仅识别名称与 VID/PID，标注未适配，不发送猜测的私有命令。
- 纯有线、没有电池的外设不会有电量；部分有电池设备也不开放可读协议。
- 蓝牙充电状态只有设备公开相应特征时才显示，否则为未知。
- 系统缓存明确标注；灰色百分比表示过期或缓存，失败不会伪造新读数。
- USB 与接收器同时枚举时，可能显示两个通道；不按名称强行合并，以免吞掉第二台同型号设备。
- 经典蓝牙目前只显示连接状态。BLE 缓存按设备名查找，同名设备的缓存可能无法可靠区分。
- 厂商软件占用、设备休眠、固件差异或共享接收器可能造成超时；约 30 秒刷新，失败时退避。
- 查询不改 DPI、灯光、音效或固件。部分 PA 耳机协议会临时切换查询模式，结束或超时后尝试恢复。

## 日志与诊断

`data/` 保存设置和当前读数，`logs/` 保存轮换日志与崩溃文件，
`diagnostics/` 保存主动导出的诊断 ZIP。应用不自动上传日志。
诊断中保留设备名称、匿名关联 ID、VID/PID、读取错误；原始 HID 路径不写入发现结果。
导出会过滤常见用户名路径、设备路径和蓝牙地址。分享前仍需检查内容。
日志写入失败时回退到当前用户 LocalAppData/PeriDock/logs。

## 系统与构建

Windows 10 build 19041 或更高版本，x64。本轮仅在当前 Windows 11 电脑验证。
运行包仍使用 .NET 8.0.33，未签名；未完成干净系统、全部固件、休眠重连与长期稳定性测试。
此前发布记录的 .NET 8 支持结束日为 2026-11-10；本次未迁移运行时，
长期维护需要迁移到仍受支持的版本后重新验证。

在 Windows 安装 .NET 8 SDK 后，可运行：

```powershell
dotnet run --project src/tests/PeriDock.Tests.csproj -c Release -- --catalog docs/device-catalog.json
dotnet build src/PeriDock.csproj -c Release
dotnet run --project src/ui-checks/PeriDock.UiChecks.csproj -c Release
dotnet publish src/PeriDock.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o dist/Peripheral-Battery-Preview
```

UI 检查使用模拟数据。公开包不含个人设置、实时状态、日志、设计参考图或 SDK。
发行包包含 LICENSE、THIRD-PARTY.md 与 docs/licenses/，自包含运行时许可随包提供。
原创代码采用 MIT，第三方来源见 [THIRD-PARTY.md](THIRD-PARTY.md)。
公开版使用通用设备图标；ASUS、ROG、Razer 与产品名称归各自权利人所有。
本项目是非官方工具，与厂商无隶属关系。
