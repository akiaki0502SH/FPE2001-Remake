# FPE2001-Remake

FPE2001（整人专家）的 Windows 11 现代化重制。以原版九模块、模块顺序、快捷键和文件十六进制编辑能力为功能基线；底层整体重写，不运行/复用旧版二进制。

- **规格**：架构与代码实施规格 v2.1、UI/UX 实施规格 v2.3、UI Design Kit v2.1
- **技术栈**：.NET 10 LTS、C# 14、WPF、x64、SQLite、命名管道
- **命名空间**：`Fpe2001Remake.*`；产品/安装/数据目录：`FPE2001-Remake`
- **安全边界**：本地离线单机；无内核驱动、DLL 注入、反作弊绕过、隐藏或联网对战支持

## 九模块（原版顺序）

| # | 模块 | 原版 | 阶段 |
|---|------|------|------|
| 1 | 扫描 | TabScan | P1 ✅ |
| 2 | 地址表 | TabTabs | P1 ✅ |
| 3 | 十六进制 | TabEditor | P2 ✅ |
| 4 | 文件 | TabSelect | P3 ✅ |
| 5 | 图片 | TabGPE + SPE | P4 ✅ |
| 6 | 宏 | TabMacKey + tt.dll | P4 ✅ |
| 7 | 速度 | TabSpeed | P5 ✅ |
| 8 | 设置 | TabOthers | P4 ✅ |
| 9 | 关于 | TabAbout | P0 ✅ |

## 目录结构

```
src/
  Fpe2001Remake.Domain        # 领域模型：LogicalAddress/ByteRange/…（64 位）
  Fpe2001Remake.Contracts     # 公共契约：IByteSource/IScanEngine/…（先固定契约）
  Fpe2001Remake.Application   # 应用服务实现（编排、跨模块跳转）
  Fpe2001Remake.UI            # WPF 九模块壳层与清透蓝 v2.3 视觉主题
  # P1+：Memory.Win32 / Scan / AddressBook / BinaryEditor.Core / ByteSources.* / FileWorkspace /
  #       MediaWorkspace / Automation / SpeedControl / Storage / LegacyImport /
  #       AdapterSdk / AdapterHost / Broker
tests/
  Unit / Contract / Integration / EndToEnd
  SyntheticTarget.x64        # 合成目标进程（已知内存布局，供扫描测试）
adapters/
  GenericWin32 / LegacyIni / Libretro / <Emulator>   # P5/P6
```

## 构建与测试

```bash
dotnet build FPE2001-Remake.slnx -c Release
dotnet test FPE2001-Remake.slnx -c Release
```

Release win-x64 构建必须无警告；地址/文件偏移/领域长度一律 `ulong`（禁止 32 位降级）。

Windows 11 上可在仓库根目录双击 `run-fpe2001.cmd`。扫描流程联调可运行 `run-test.cmd`，它会同时启动合成目标进程。

## 设计与实施文档

- [Win11 重构分析与架构设计 v2.1](docs/specifications/FPE2001-Remake_Win11重构分析与架构设计_v2.1.docx)
- [代码实施规格包 v2.1](docs/specifications/FPE2001-Remake_代码实施规格包_v2.1.docx)
- [UI/UX 实施规格 v2.3](docs/specifications/FPE2001-Remake_UIUX实施规格_v2.3.docx)
- [UI Design Kit v2.1](docs/design-kit-v2.1/README.md)
- [UI 实现偏差与验收记录](docs/UI-DEVIATIONS.md)

## 实施阶段

| 阶段 | 范围 | 出口标准 |
|------|------|----------|
| P0 | 工程、九模块壳层、Domain、合成目标 | x64 构建、导航与状态夹具、地址/偏移往返 |
| P1 | Memory/Scan/AddressBook/Freeze | 扫描→结果→地址表→写入/全停闭环 |
| P2 | BinaryEditor.Core + File/Process 字节源 | HEX-001~004 |
| P3 | FileWorkspace + LegacyImport | FILE-001~003；旧配置预览 |
| P4 | Media + Automation + Settings | 图片格式、宏急停、设置迁移 |
| P5 | AdapterHost + IPC + Speed | 契约、崩溃隔离、无能力状态 |
| P6 | 首个模拟器适配 | 精确版本、3 个已知地址、能力按需 |

UI 实现与 Design Kit 的差异记录在 [docs/UI-DEVIATIONS.md](docs/UI-DEVIATIONS.md)。
