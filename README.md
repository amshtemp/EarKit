<p align="center">
  <img src="src/EarKit/Assets/app.png" width="112" alt="EarKit">
</p>

<h1 align="center">EarKit</h1>

<p align="center">
  在 Windows 上控制你的 EarFun 耳机：降噪、均衡器、按键、多设备连接，一个托盘图标全搞定。
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/ChuiFeng-wind/EarKit?include_prereleases&label=下载" alt="Release"></a>
  <img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4" alt="Windows 10 | 11">
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-green" alt="MIT"></a>
</p>

> [!IMPORTANT]
> **EarKit 是非官方的第三方开源项目**，与 EarFun 公司无关，未获其认可或赞助。“EarFun”及其产品名称是其各自所有者的商标，本项目提及它们仅用于说明兼容性。请勿就本软件的问题联系 EarFun 官方客服。

---

## 📸 截图

<p align="center">
  <img src="docs/screenshots/overview.png" width="760" alt="概览">
</p>

| 降噪 | 均衡器 |
|:---:|:---:|
| <img src="docs/screenshots/noise.png" alt="降噪"> | <img src="docs/screenshots/equalizer.png" alt="均衡器"> |
| **按键** | **耳机设置** |
| <img src="docs/screenshots/keys.png" alt="按键"> | <img src="docs/screenshots/device.png" alt="耳机设置"> |

## ✨ 功能

| 页面 | 能做什么 |
|---|---|
| **概览** | 左耳 / 右耳 / 充电盒电量，连接状态，固件版本 |
| **降噪** | 降噪 / 通透 / 关闭；降噪子模式（均衡、自适应、抗风噪等）；通透模式（默认 / 自然） |
| **均衡器** | 官方预设、拖动调节、实时曲线；保存和管理自定义预设 |
| **按键** | 自定义左右耳的单击、双击、三击、长按功能 |
| **多设备连接** | 查看已连接的手机 / 电脑，切换、断开，进入配对模式；LDAC 冲突提示 |
| **耳机设置** | 游戏模式、音质（LDAC / aptX / 自适应 / 无损）、提示音量、查找耳机、麦克风、佩戴检测、自动关机等（视型号而定） |
| **应用设置** | 深色 / 浅色 / 跟随系统，云母 / 亚克力背景与不透明度，开机自启，托盘，通知，快捷键 |
| **调试日志** | 查看收发的原始数据，手动发送命令，导出日志 |

**常驻托盘**

- 关闭窗口后最小化到托盘，鼠标悬停即可看到电量和当前降噪模式
- 右键菜单快速切换降噪模式、游戏模式，或重新连接
- 全局快捷键 <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>N</kbd> 循环切换降噪模式（可自定义）
- 耳机连上电脑后自动连接，电量低时提醒

## 🎧 支持的型号

共 23 款，分别使用高通 GAIA 协议和 EF 协议，程序会自动识别型号。

| 高通 GAIA 协议 | EF 协议 |
|---|---|
| Air Pro 4 ✅ 已实测 | Air Pro 4i |
| Air Pro 4+ | Air 2 NC / Air 2 NC (VDL) |
| Air Pro 4+ AI | Air Life |
| Air Pro 5 | OpenJump / OpenJump (新款) |
| Air Pro 3 / Air Pro 3 (3072) | Clip / Clip 2 |
| Free Pro 3 | Wave Life |
| Air S / Air S (TW201) | Tune Pro |
| Free 2S | Wave Pro 2 |
| Wave Pro X | |
| ATVEL Tune S5 ProX | |

除标注外的型号尚未实机测试。如果你在其他型号上用过，无论是否正常，都欢迎在 [Issues](../../issues) 中反馈。

## 📥 安装

1. 前往 [Releases](../../releases/latest) 下载 `EarKit-x.y.z-Setup.exe` 并运行。
2. 安装包没有数字签名，Windows 可能提示“Windows 已保护你的电脑”，点击 **更多信息 → 仍要运行** 即可。
3. 如果电脑缺少 .NET 10 桌面运行时，安装程序会自动安装，无需手动下载。

**系统要求**：Windows 10 2004 及以上（64 位）；云母 / 亚克力背景需要 Windows 11 22H2 及以上。

## 🚀 使用

1. 先在 Windows 的 **设置 → 蓝牙和设备** 中配对并连接耳机。
2. 打开 EarKit，在顶部选择你的耳机，点击 **连接**。
3. 之后 EarKit 会记住这副耳机，下次耳机连上电脑时会自动连接。

## ❓ 常见问题

<details>
<summary><b>连接失败或一直显示“正在连接”</b></summary>

- 耳机的控制通道同一时间通常只能被一台设备占用。请先 **关闭手机上的 EarFun App**，必要时断开手机蓝牙后再试。
- 确认耳机在 Windows 蓝牙设置中显示为“已连接”。
- 仍然不行的话，在 **应用设置 → 连接方式（高级）** 中尝试改用蓝牙 COM 端口连接。
</details>

<details>
<summary><b>识别出的型号不对 / 部分功能不显示</b></summary>

在 **应用设置 → 耳机型号** 中手动选择正确的型号。
</details>

<details>
<summary><b>搜索不到 EarKit / 开始菜单里没有</b></summary>

安装时勾选“开始菜单快捷方式”（默认勾选）。如果当时取消了，重新运行安装程序勾选即可，设置不会丢失。
</details>

<details>
<summary><b>恢复出厂设置、“解锁全部功能”安全吗？</b></summary>

这些操作可能导致耳机设置丢失或功能异常，请谨慎使用，风险自负。“解锁全部功能”会显示当前型号未声明支持的选项，仅供测试。
</details>

## 🗂️ 数据位置

| 内容 | 位置 |
|---|---|
| 设置、自定义均衡器预设 | `%AppData%\EarKit\settings.json` |
| 崩溃日志 | `%AppData%\EarKit\crash.log` |
| 开机自启项 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `EarKit` |

命令行参数 `--tray`：启动后只显示托盘图标，不弹出窗口。

卸载时可以选择是否同时删除上述个人数据。

## 🛠️ 从源码构建

需要 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 和 [NSIS 3](https://nsis.sourceforge.io/)（生成安装包时需要）。

```powershell
git clone https://github.com/ChuiFeng-wind/EarKit.git
cd EarKit

./build.ps1                 # 生成 dist\EarKit-<版本>-Setup.exe
./build.ps1 -NoInstaller    # 只发布程序到 publish\
cd src/EarKit; dotnet run   # 直接调试运行
```

推送 `v*` 标签后，GitHub Actions 会自动构建安装包并创建 Release 草稿。

**项目结构**

```
src/EarKit/
├─ Core/        型号表、协议编解码（高通 GAIA / EF）、蓝牙 RFCOMM 连接
├─ Services/    设备控制、托盘、全局快捷键、设置、背景效果
├─ Controls/    电量环、EQ 曲线等自绘控件
└─ Pages/       各功能页面
installer/      NSIS 安装脚本与安装许可
```

技术栈：C# · .NET 10 · WPF · [WPF UI](https://github.com/lepoco/wpfui) · [H.NotifyIcon](https://github.com/HavenDV/H.NotifyIcon)

## 🐞 反馈问题

请在 [Issues](../../issues) 中提供：

- 耳机型号和 Windows 版本
- 问题截图
- **调试日志** 页导出的日志
- 如果程序崩溃，附上 `%AppData%\EarKit\crash.log`

## 📄 许可

- EarKit 以 [MIT 许可证](LICENSE) 发布，按“原样”提供，不附带任何担保。
- 第三方组件的许可见 [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt)。
- 安装包内置的 Microsoft .NET 桌面运行时受 [.NET Library License](https://dotnet.microsoft.com/dotnet_library_license.htm) 约束，按原样再分发。
