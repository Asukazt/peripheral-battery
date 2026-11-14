# 新品牌官网图片（preview.5 图片补充构建）

补充构建版本：`0.2.0-preview.5-images.1`。附加在现有 `v0.2.0-preview.5` Pre-release，
原标签和原包保留；补充源码对应新增提交，不能用原标签提交替代。

本次核对 25 组官方来源、39 个精确名称别名、38 张官方图片/配色。
这些数字是图片映射数量，不是新增电量协议或实物认证数量。

## 获取与选择

- 保留 Razer / ROG 的页面解析方式。新品牌从已核对的官网产品页、官方支持目录或官方商城提取图片直链，运行时后台下载并缓存；无需执行官网脚本，也不会用设备名称进行网页搜索。
- 托盘右键“设备图片与颜色 → 设备”，可选已提供的官方配色、自动、本地图片、自定义图片或通用图标；选择按型号保存，USB / 接收器 / 蓝牙后缀共用。
- 确认分色的素材才标黑色、白色等；网站只有一张图片或系列图时标“官方外观”或“系列官方示意图”。不根据 VID/PID 猜颜色，不用其他型号图片冒充；系列图不代表精确子型号和实际配色。
- 下载保持 HTTPS、精确地址白名单、超时、大小/格式/尺寸限制。缓存元数据不能扩展官方地址范围，跳转至未核对的同域路径也会被拒绝。仅接受可解码的 PNG/JPEG，缓存为 PNG。
- 图片读取不阻塞电量；联网失败保留通用/自定义图片，已有缓存可离线使用。内置地址随软件更新维护，官网移除或改址后可能回退，并非自动适配网站未来所有型号。
- 不随公开包分发官网图片缓存、用户设置、私人素材或诊断数据。厂商仍会收到普通 HTTP 请求信息；设备路径、序列号、蓝牙地址、日志等不会发给图片服务。

## 已核对映射

| 精确名称（含别名） | 可选官方外观 | 一手来源 |
| --- | --- | --- |
| Logitech G733 / Logitech G733 Gaming Headset | 丁香紫、黑色、白色、蓝色 | [官方来源](https://www.logitechg.com/en-us/shop/p/g733-rgb-wireless-headset) |
| Logitech G535 | 官方外观 | [官方来源](https://www.logitechg.com/en-us/shop/p/g535-wireless-headset) |
| Logitech G935 | 官方外观 | [官方来源](https://www.logitechg.com/en-us/shop/p/g935) |
| Logitech G PRO X 2 / Logitech G PRO X 2 LIGHTSPEED / Logitech PRO X 2 LIGHTSPEED | 黑色、白色、洋红色 | [官方来源](https://www.logitechg.com/en-us/shop/p/pro-x-2-wireless-headset) |
| Logitech PRO X SUPERLIGHT 2 DEX / Logitech G PRO X SUPERLIGHT 2 DEX | 黑色、白色、粉色 | [官方来源](https://www.logitechg.com/en-us/shop/p/pro-x-superlight-2-dex) |
| Logitech G502 LIGHTSPEED / Logitech G502 LIGHTSPEED Wireless Gaming Mouse | 官方外观 | [官方来源](https://www.logitechg.com/en-us/shop/p/g502-lightspeed-wireless-gaming-mouse) |
| Logitech G305 / Logitech G305 LIGHTSPEED / Logitech G305 LIGHTSPEED Wireless Gaming Mouse | 白色、黑色、丁香紫、蓝色、薄荷绿 | [官方来源](https://www.logitechg.com/en-us/shop/p/g305-lightspeed-wireless-gaming-mouse) |
| Logitech G903 / Logitech G903 LIGHTSPEED / Logitech G903 LIGHTSPEED Wireless Gaming Mouse | 官方外观 | [官方来源](https://www.logitechg.com/en-us/shop/p/g903-hero-wireless-gaming-mouse) |
| Logitech G915 TKL / Logitech G915 TKL LIGHTSPEED | 官方外观 | [官方来源](https://www.logitechg.com/en-us/shop/p/g915-tkl-wireless) |
| Logitech G915 X LIGHTSPEED | 官方外观、白色 | [官方来源](https://www.logitechg.com/en-us/shop/p/g915-x-wireless-mechanical-gaming-keyboard) |
| HyperX Cloud III Wireless | 黑色、黑红色 | [官方来源](https://www.hp.com/ie-en/products/accessories/product-details/2101547733) |
| HyperX Pulsefire Haste 2 Wireless | 黑色 | [官方来源](https://www.hp.com/ie-en/products/accessories/product-details/2101391586) |
| MCHOSE A7 V2 Ultra | A7 V2 系列官方外观 | [官方来源](https://www.maicong.cn/detail?link_product=76b25ab2f52f22da3eb9f9f6d8bf4ab0) |
| MCHOSE M7 Ultra | M7 系列官方外观 | [官方来源](https://www.maicong.cn/detail?link_product=861be2407bc7abebaf1c2ab22d69ba77) |
| MCHOSE G98 V3 | G98 V3 系列官方外观 | [官方来源](https://www.maicong.cn/detail?link_product=5eccebfa66765d69932749dc85fb7143) |
| MCHOSE V9 Turbo+ | V9 Turbo + 系列官方外观 | [官方来源](https://www.maicong.cn/detail?link_product=47b80df16c1a9c579b3ec8f325d28a65) |
| ATK X1 Ultimate | ATK 烈空 X1 ULTIMATE 系列官方外观 | [官方来源](https://www.atkgear.com.cn/mouse/x1-ultimate) |
| ATK X1 Pro | ATK 烈空 X1 系列官方外观 | [官方来源](https://www.atkgear.com.cn/mouse/x1) |
| ATK A9 Ultimate / ATK A9 SE | ATK A9 系列官方外观 | [官方来源](https://www.atkgear.com.cn/product-manual) |
| ATK V75X | V75X 系列官方外观 | [官方来源](https://www.atkgear.com.cn/keyboard/atk-v75x) |
| VXE R1 SE+ / VXE R1 Pro / VXE R1 Pro Max | R1 系列官方示意图 | [官方来源](https://api.vxe.com/v1/instructions?brands=VXE) |
| VGN F1 Moba / VGN Dragonfly F1 Moba | 黑色（F1 Moba） | [官方来源](https://www.vgnlab.com.cn/web/sku-558) |
| AULA F75 | 官方外观 | [官方来源](https://www.aulastar.com/keyboard) |
| AULA F87 Pro V2C | F87 Pro V2 系列官方外观 | [官方来源](https://www.aulastar.com/mechanical-keyboard/480.html) |
| AULA V9 Max+ | V9 系列官方外观 | [官方来源](https://www.aulastar.com/gaming-mouse/489.html) |

ATK / VXE 的支持目录图片来自官方站点使用的 `https://api.vxe.com/v1/instructions`，
ATK 官网公开配置与支持页直接指向该接口及 `cdn.vxe.com`，不是第三方商城图片。
MCHOSE 使用官网公开的产品列表及 `/static/` 图片；HyperX 使用 HP 官方对应 SKU 页。

## 保留的空缺

共享接收器如 Logitech HID++ 接收器、MCHOSE M7/L7、ATK/VXE NK，及合并名称 R1 Pro/Pro Max，无法确认具体产品时仍用通用图标。
当前 `VGN Dragonfly 4K` 通道名无法可靠确认子型号，仍不自动套用 F1 Moba 图；只有精确识别为 F1 Moba 的名称映射到该图。
本表未列的型号、配色、旧产品或新固件名称可能仍无官网图，包括未取得可靠素材的部分 HyperX、MCHOSE、Logitech 型号。
有图片不代表电量协议已启用：例如 G98 V3 的读取限制保持原状。
没有连接六个新品牌实物验证，本次不扩大电量兼容承诺，不宣称全型号通杀。
