# Peripheral Battery Preview

Windows ROG / Razer 外设电量组件，0.2.0-preview.3 官网产品图片与配色测试版。
自动发现已连接设备，集中显示电量、连接方式、可读的充电状态与部分雷蛇鼠标 DPI。
这是扩大协议覆盖范围的测试版，不承诺所有型号、固件或接收器均可读取。

## 下载与运行

完整解压 `Peripheral-Battery-v0.2.0-preview.3-win-x64.zip` 到新的可写目录，
例如 `D:\PeripheralBatteryPreview`，双击 `PeriDock.exe`。
自带 .NET，无需安装 SDK。不要在压缩包内启动，也不要覆盖当前安装。

初次启动在通知区域常驻，悬浮条默认隐藏。右键图标可切换“显示悬浮窗”，
此选择会保存。托盘悬停按类别显示“键盘：80% · 鼠标：10% · 耳机：48%”，
不显示具体型号；单击打开基本信息，双击打开详情。
多设备时可滚动查看。测试版使用按安装目录区分的进程锁和程序目录中的设置、日志，
可以与原版或其他目录的预览版共存；测试版暂不提供开机启动，避免覆盖原版启动注册。

## 官网图片与颜色选择

识别到内置映射中的具体型号后，在后台从 Razer / ROG 官方产品页或图库获取对应图片，
电量读取不等待网络。首次需要联网；成功后缓存在 `data/device-images/`，可离线使用。
匹配不到型号、官网下架、页面结构改变或网络失败时显示通用类别图标，
不会把另一款鼠标的图片套用到未知型号。失败后约 24 小时重试。

点击设备图片，或右键托盘选择 **“设备图片与颜色 → 设备”**，可打开颜色/外观窗口。
官网提供可解析配色时可选择黑色、白色等；选择即时生效并按型号保存，重启后恢复。
同型号的 USB / 2.4G / 蓝牙通道共用选择；两台同型号设备暂不能分别选色。
也可以选择“自动”或“通用图标”。此功能只改变软件显示，不修改设备颜色、灯光或固件。
官网没有分色素材时，可能只有“官方外观”，或自动图片包含多个颜色。

已有个人图片可放在 `data/device-images/overrides/<型号键>.png`，自动模式会优先使用；
型号键为 `SHA256(归一化型号)` 前 24 位十六进制，具体算法见 `OfficialProductImages.FileKey`。
个人图片按型号隔离，不会发给其他用户。公开包仅含通用图标，不包含个人图片或官网图片缓存。

程序只请求内置映射中的官网 HTTPS 地址和受限的官方图片域名，不将未知设备名称、
设备路径、序列号、日志或诊断数据发送给图片服务；官网仍会收到普通网络请求信息。
映射包含 70 个型号入口，不能理解为 70 款取图均已验证，更不代表电量协议全部适配。
本轮联网验证了 Basilisk V3 Pro、DeathStalker V2 Pro、Harpe Ace Aim Lab Edition、
Azoth X、Pelta 五款取图，以及 Basilisk 黑/白、Harpe 月光白选色和断网缓存恢复。

## USB 访问被拒绝与可选提权

默认使用普通权限，清单仍为 `asInvoker`，不会在启动或刷新时自动请求 UAC。
Windows 拒绝访问 HID 接口时，设备名称、VID/PID 和已知连接方式继续保留，
状态显示“访问被拒绝（Windows 错误 5）”，与“未连接”“协议未适配”分别显示。
USB 已枚举不等于读到了电量；2.4G 接收器存在也不代表其无线设备一定在线。
权限失败后清空显示的电量、充电状态和 DPI，不把上次读数当作实时读数。

需要时，右键托盘图标，或在详情页“设置”菜单选择 **“以管理员权限重启…”**。
此项仅在检测到拒绝访问、且当前程序尚未提权时可用；点击后才由 Windows 请求 UAC。
取消 UAC 后当前实例继续运行；成功启动提权实例后，原实例退出，新实例等待其退出后
使用同一目录重新读取设置。不会修改其他安装目录或强制所有用户提权。
请仅对你信任的运行包确认 UAC；管理员权限不保证解决所有驱动、固件或协议问题。

本机 Viper V3 Pro 的 USB 主接口为 `VID_1532&PID_00C0 / MI_00`。
普通权限返回错误 5；此前诊断材料确认管理员权限能够读到电量、充电和 1600 DPI。
退出雷蛇界面后仍会拒绝访问，因此本项目不把错误 5 直接解释为“雷蛇软件占用”。

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

此前本机已读到：Viper V3 Pro 2.4G 电量和 1600 DPI、Azoth X 蓝牙电量。
既有版本曾验证上述鼠标、BlackShark V3 Pro 同时有线读取，以及 Azoth X USB 电量。
preview.2 新构建普通权限实测：Viper V3 Pro USB 保留识别并报告错误 5；
BlackShark V3 Pro 2.4G 读到 51%，Azoth X 蓝牙 GATT 读到 81%。
该轮未实际操作 UAC 或以新版提权读取；旧版管理员诊断不能替代新版提权实测。
新增适配型号没有逐台实测，USB / 2.4G / 蓝牙协议与状态回归使用受控测试补充验证。

## 已知限制

- 未知型号仅识别名称与 VID/PID，标注未适配，不发送猜测的私有命令。
- 纯有线、没有电池的外设不会有电量；部分有电池设备也不开放可读协议。
- 蓝牙充电状态只有设备公开相应特征时才显示，否则为未知。
- 系统缓存明确标注；灰色百分比表示过期或缓存；权限失败直接显示 `--`，其他失败保留的旧读数会标记为过期。
- USB 与接收器同时枚举时，可能显示两个通道；不按名称强行合并，以免吞掉第二台同型号设备。
- 经典蓝牙目前只显示连接状态。BLE 缓存按设备名查找，同名设备的缓存可能无法可靠区分。
- 厂商软件占用、设备休眠、固件差异或共享接收器可能造成超时；约 30 秒刷新，失败时退避。
- 查询不改 DPI、灯光、音效或固件。部分 PA 耳机协议会临时切换查询模式，结束或超时后尝试恢复。

## 日志与诊断

`data/` 保存设置和当前读数，`logs/` 保存轮换日志与崩溃文件，
`diagnostics/` 保存主动导出的诊断 ZIP。应用不自动上传日志。
诊断中保留设备名称、匿名关联 ID、VID/PID、原生 Windows 错误码与失败原因；原始 HID 路径不写入发现结果。
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

可选联网检查：`dotnet run --project src/tests/PeriDock.Tests.csproj -c Release -- --official-images <可写缓存目录>`。
普通回归检查无需联网；UI 检查包含颜色选择、保存提示及图片在现有界面中的更新。

UI 检查使用模拟数据。公开包不含个人设置、实时状态、日志、设计参考图或 SDK。
发行包包含 LICENSE、THIRD-PARTY.md 与 docs/licenses/，自包含运行时许可随包提供。
原创代码采用 MIT，第三方来源见 [THIRD-PARTY.md](THIRD-PARTY.md)。
公开包内置通用设备图标，运行时可从官网获取产品图片；图片、ASUS、ROG、Razer 与产品名称归各自权利人所有，
厂商图片不因本项目 MIT 许可而改变授权。
本项目是非官方工具，与厂商无隶属关系。
