# 六品牌协议覆盖（preview.5）

本版新增罗技 Logitech、极度未知 HyperX、迈从 MCHOSE、ATK、狼蛛 AULA、VGN/VXE 的协议入口。
新增 85 个精确 VID/PID 条目，其中 81 个启用对应读取协议、4 个明确保留为未适配。
全目录共 217 个 VID/PID 条目；PID 数量包含接收器和 USB 通道，不等于型号数量。
这六个品牌没有在本机接入实物，以下均为协议接入与离线验证，不能视为全系实机兼容。

## 已接入

| 品牌 | 范围与协议 | 保留的限制 |
| --- | --- | --- |
| Logitech | Bolt、Unifying、Nano、LIGHTSPEED 清单接收器；HID++ 2.0 的 1004/1000/1001/1F20 电量功能；HID++ 1.0 的 0D/07 寄存器；G535 电压寄存器；部分 G 系耳机 | 接收器按 1–6 槽位分别识别，名称功能可读时显示设备名；不同接收器使用独立匿名 ID，不读取序列号。电压和档位标为估计，充电电压不伪造百分比。G PRO X2 的 0AF7 仅接入旧 Centurion 报文，未移植完整桥接发现。 |
| HyperX | Cloud II HP/Kingston、Cloud III/Core/III S、Cloud Alpha Wireless/Alpha 2、Cloud Flight、Pulsefire Haste 2 Wireless 清单 PID | 系列间使用独立报文和字段。仅 Cloud III 有明确的 Windows feature-report 回退；错误 5 不触发传输回退。未知充电状态保持未知。Flight 为电压曲线估计。 |
| MCHOSE | M7/L7 共用接收器、M7 Ultra USB、A7 V2 Ultra、A5 Pro Max、G7、K87、V9 Pro、V9 Turbo+ 清单 PID | M7/L7 共用 PID 不硬认具体型号。G7 保留启动 80%、抖动和异常下跌过滤；重复 80% 的回退标缓存待确认，不能证明真实电量。Turbo+ 充电语义不足，不猜测。 |
| ATK | A9 Ultimate/SE、X1 Pro、Fire X 8K CompX；X1 Ultimate Nearlink USB/接收器；共享 NK BITMOUSE 接收器；V75X USB | 协议和 collection 分开选择；Nearlink 校验序号，BITMOUSE 不把序号当充电标记。NK 接收器不硬认单一型号。 |
| AULA | F75 CompX、F87 Pro V2C 接收器、V9 Max+ USB/接收器 | OEM 共用 PID 同时校验产品名称前缀。F87 不臆造充电状态；V9 接收器可能返回缓存，在线状态待确认、灰色显示，不当实时读数。 |
| VGN / VXE | Dragonfly 4K、R1 SE+、R1 Pro/Pro Max 等清单通道 | 按精确 VID/PID 和 CompX collection 选择，不向所有同 VID 新型号盲发。 |

完整清单见 [device-catalog.json](device-catalog.json)，包含 Reader、连接方式、
protocolAvailable、hardwareVerified 和 productNamePrefix。
hardwareVerified 为 false 表示目录不提供该条目的实机认证；本机历史实测另列在 README/VERIFY。
具有 FF00:1/2 HID++ collection 的其他罗技设备可进行只读功能发现，能否读取取决于实际功能响应。

## 明确未启用的通道

- Logitech PRO X3 LIGHTSPEED 046d:0b45：不能沿用 PRO X2 协议。
- MCHOSE G98 V3 3837:1015：现有迈从命令未得到可用电量，显示待适配。
- ATK V75X 接收器 320f:5088：轮询可能唤醒键盘，尚未移植键盘活动防护，暂只启用 USB 320f:5055。
- AULA F87 Pro V2C USB 38a6:2712：相似应答是配置数据，不是电量，禁止从中提取百分比。
- 其他未列型号、固件或协议变体：识别到品牌时保留设备，不发送猜测的配置/固件命令。

## 连接与隐私

- HID 单次枚举允许的厂商 ID，再按设备与 collection 路由；标准鼠标、键盘接口不作私有电量查询。
- 2.4G 接收器存在不代表无线设备在线。HyperX 可读连接命令用于区分离线，其他协议无可靠依据时保持未知。
- 罗技在当前运行期间记住已识别槽位，休眠无响应时保留身份。重启后若设备一直休眠，可能先只显示接收器，唤醒后再发现。
- USB、2.4G、蓝牙可能各自枚举为一个通道；不按名称强合并两台同型号设备。
- 蓝牙仅扩展品牌名称识别，仍通过 Windows 标准 Battery Service 读取；经典蓝牙或不公开服务的设备不能保证读数。
- 默认管理员启动沿用 preview.4，解决不了未公开协议、固件变化或无电池硬件。
- 图片补充构建增加已核对的新品牌官网映射，范围与空缺见 [OFFICIAL-IMAGES.md](OFFICIAL-IMAGES.md)；不打包厂商照片或个人图片，不扩大电量协议覆盖。
- HID 路径和物理容器只在本机路由，公开发现结果不带原始路径，日志继续过滤路径与个人信息。

## 协议依据与验证

一手上游：MIT 项目 [HeyOkay/HaloBattery](https://github.com/HeyOkay/HaloBattery/tree/73e229cbee2e97fad659fcd96c78afdfa78488fb)，
固定提交 73e229cbee2e97fad659fcd96c78afdfa78488fb。
参考 providers/logitech*.py、compx.py、pulsar.py、atk_*.py、aula.py、lofree.py、mchose*.py、
hyperx*.py 中的精确 ID、collection、只读电量命令和已公开捕获。
MIT 许可保留在 docs/licenses/LICENSE-halobattery.txt。

回归覆盖固定报文、校验和、范围与命令回显、接口选择、多设备隔离、失败清理、权限失败、
充电查询失败时保留本轮有效电量，以及原有 Razer/ROG、USB/2.4G/蓝牙与图片行为。
离线模拟会话不能证明 Windows 驱动、特定固件和实物一定能通信。
交付测试数量、独立构建、原有实物回归和哈希结果见本版 VERIFY 文件。
