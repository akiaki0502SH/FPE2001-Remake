# UI-DEVIATIONS

本文件记录 FPE2001-Remake 实现与 `FPE2001-Remake UI/UX 实施规格 v2.8`（Deep Boundary Blue + Native DWM Frame + 程序名聚合）之间的结构差异（验收项：UIUX 规格 §17）。

> 规则：九模块功能入口不得省略；结构差异必须记录；无法在实现中表达的设计意图标注原因。

## P0（壳层）— 2026-08-16

| 区域 | Design Kit | 实现 | 说明 |
|------|-----------|------|------|
| 标题栏 | 52 DIP，含目标与状态徽章 | 52 DIP，产品名 + 应用图标 + 当前目标 + 目标详情 + 冻结/宏/速度徽章 + 窗口按钮 | v2.2 视觉升级；按钮组固定最右 |
| 模块带 | 76 DIP，图标牌+文字，活动项低饱和填充 | 76 DIP，Segoe MDL2 图标牌 + 文字，选中态 `PrimarySoft` 填充 + 主色边框 | v2.2 增加图标牌、渐变和微阴影 |
| 动作栏 | 48 DIP，文字+图标+F 键提示，危险分组 | 48 DIP，图标+文字+快捷键提示，危险动作红色文字 | v2.2 增加圆角、渐变、hover/pressed/focus 状态 |
| 内容区 | 各模块真实工作区 | 统一占位页：图标 + 标题 + 状态徽章 + 状态说明 + 工作区说明卡片 | P0 壳层阶段；P1 起逐页替换为真实工作区 |
| 状态栏 | 31-36 DIP：目标/来源、进度、急停 | 31 DIP：来源 + 进度 + 模块快捷键 + 全局停止按钮 | 一致 |
| 窗口 | 1440×900 默认、1100×720 最小、200% DPI 无裁切 | 1440×900 / 1100×720、PerMonitorV2 DPI | 一致 |
| 快捷键 | Ctrl+1…9 切换模块；Ctrl+Shift+F12 全局停止 | 已实现（主窗口 PreviewKeyDown） | 一致 |

## P1（扫描/地址表/冻结闭环）— 2026-08-16

| 模块 | 状态 | 说明 |
|------|------|------|
| M01 扫描 | 真实工作区 | 目标进程选择（所有用户态进程）、首次/再次扫描（8/16/32/64 位/浮点/文本、等于/不等于/大于/小于/变化/未变/未知值）、进度、候选表（地址/当前值/上次值/宿主证据）、添加到地址表 |
| M02 地址表 | 真实工作区 | 条目表（冻结状态/描述/地址/类型/当前值/写入值）、立即写入（身份复验+读回）、条件冻结（Always，250ms，失败熔断）、删除/清空 |
| 标题栏/状态栏 | 联动 | 选中目标 → 标题栏显示进程名+PID；状态栏显示目标 |
| 动作栏 | 提取 ActionBar 控件 | 具体页面模板不再丢失动作栏；AutomationProperties.Name 提供无障碍名称 |

### 集成测试证明的闭环（tests/Integration）
1. 首次扫描 UInt64 Magic 值命中 → 2. Unchanged 再次扫描保留 → 3. 立即写入 0 + 读回 → 4. Equal 0 再次扫描命中 → 5. 冻结 counter（Always 0x12345678）生效 → 6. 全局停止后手动写入不被覆盖 → 7. Changed 检测命中改写后的地址。

### 已知限制（自动化环境）
- UIA/SendKeys 无法可靠驱动 WPF ComboBox 选择（虚拟化 + 焦点注入限制）；真实鼠标操作正常，ComboBox 选择待用户实测确认。
- 快捷键 Ctrl+1…9 / Ctrl+Shift+F12 代码路径已实现并编译，键盘实测待用户确认（SendKeys 在无真实焦点下不可靠）。

## P2（统一十六进制编辑器）— 2026-08-16

| 模块 | 状态 | 说明 |
|------|------|------|
| BinaryEditor.Core | 完成 | HexDocument（64KiB 分页缓存 + 区间映射 SegmentMap + 64 位偏移全程 ulong）、覆盖/插入/删除、撤销、书签、SearchEngine（Hex ?? 掩码/文本/整数/浮点，跨块匹配）、TextCodec（ASCII/UTF-8/UTF-16LE/GBK） |
| ByteSources.File | 完成 | FileVersionToken（路径+长度+mtime+FileId）、共享读、安全提交（复验→备份→同目录临时文件→流式应用→原子替换→新 Token） |
| ByteSources.Process | 完成 | 相对视图 0-based、区域读取 UnavailableRanges、等长覆盖（ExpectedCurrentValue+读回）、目标重启提交拒绝 |
| UI 十六进制页 | 完成 | 虚拟化网格（偏移/16 字节 hex/文本，修改高亮）、不可读内存显示 `??`、打开文件/打开进程对话框、编辑模式（覆盖）、撤销、查找面板、原子保存并重载新版本 |
| 验收 | 通过 | HEX-001（5GiB 稀疏文件高偏移编辑提交）、HEX-002（插入/删除组合）、HEX-003（外部修改阻止提交）、HEX-004（进程源约束+重启拒绝）；UIA 实测进程内存视图+搜索定位 Magic |

### 集成测试证明（tests/Integration ByteSourceIntegrationTests）
HEX-001~004 全部通过：>4GB 稀疏文件跳转/覆盖/撤销/另存校验；插入删除组合区间映射；外部修改 VersionTokenMismatch 且原文件不损坏；进程源插入删除禁用 + 进程重启后写入拒绝。

## P3（文件工作区 + 旧配置导入）— 2026-08-16

| 模块 | 状态 | 说明 |
|------|------|------|
| FileScanService | 完成 | 递归枚举（默认不跟随 reparse）、扩展名/大小/时间过滤、Hex ?? 掩码/Text/Integer/Float 谓词、块流式匹配、进度/取消/单文件跳过 |
| LegacyImportService | 完成 | INI/CSV/TSV/Binary 格式检测与字段预览（M08 设置页导入前置） |
| UI 文件页 | 完成 | 目录树（惰性加载）、文件列表、扫描面板（模式/类型/递归）、命中列表（双击跳转十六进制） |
| 验收 | 通过 | 6 个文件扫描测试（递归/非递归/过滤/文本/上限/取消）+ 4 个导入预览测试；UIA 实测 MZ 魔数扫描 101 命中 |

## P4/P5（图片/宏/设置/速度 + AdapterHost IPC）— 2026-08-16

| 模块 | 状态 | 说明 |
|------|------|------|
| M05 图片 | 完成 | 文件夹浏览 + 缩略图网格 + 导出 PNG/JPG（缩放/质量）+ 重命名 + 删除（回收站优先）；WPF 编解码无第三方依赖 |
| M06 宏 | 完成 | 宏 CRUD（%APPDATA% JSON）、步骤编辑（按键/鼠标/延迟/聚焦）、RegisterHotKey 热键、运行/急停、目标窗口聚焦 |
| M08 设置 | 完成 | 扫描/文件与编辑/写入与锁定/兼容与隐私四分组；JSON 持久化；旧配置导入预览（INI/CSV/TSV/Binary） |
| M07 速度 | 完成 | AdapterHost 独立进程（Named Pipe IPC，AdapterMessage 帧）+ 能力检测 + 无能力时页面保留并解释不可用（不尝试 DLL 钩子）+ 倍率滑块/推荐刻度 |
| 验收 | 通过 | 90 测试全绿（70 Unit + 20 Integration）；AdapterHost 每会话独立管道并在测试后清理；UIA 历史实测：图片页按钮/宏页编辑/设置分组/速度页连接 Generic（无能力）+ Demo 适配器 2x 倍率设置 |

### P5 IPC 关键经验
- NamedPipe 两端避免 StreamReader/StreamWriter（缓冲/BOM 陷阱），用原始字节 + \n 帧分割 + correlationId 匹配
- 集成测试启动 AdapterHost 需先显式构建（dotnet test 不构建非依赖 exe 项目）
- AdapterHelloInfo 的 capabilities 用数字而非枚举字符串（JsonException 陷阱）

## v2.2 视觉升级 — 2026-08-16

| 区域 | v2.2 规范 | 当前实现 | 验收 |
|------|-----------|----------|------|
| 标题栏 | 52 DIP，HeaderGradient，图标牌 + 目标状态；窗口按钮固定最右 | `LastChildFill=False` + DockPanel.Dock=Right；按钮 46×44 DIP | 构建通过；需在运行窗口重新截图确认 |
| 模块带 | 76 DIP；36×36 DIP 图标牌、渐变和微阴影 | `ModuleButton` 使用 `IconPlate`、低饱和渐变和选中态白色图标 | 构建通过 |
| 动作/输入控件 | 36–48 DIP；圆角、边框、hover/pressed/focus 状态 | `ActionButton`、`TextBox`、`WindowButton` 已应用状态模板 | 构建通过 |
| 应用图标 | 使用 Design Kit 提供的 FPE2001-Remake.exe 同源图标 | 提取至 `src/Fpe2001Remake.UI/Assets/FPE2001-Remake.ico`，配置 `ApplicationIcon` | 构建通过 |
| 页面层次 | WindowGradient + SurfaceGradient + CardShadow | 壳层和通用占位页已接入 | 构建通过 |

截图验收必须关闭旧版 UI 后重新启动新版程序；不得把浏览器或桌面区域混入截图。

## v2.3 Clear Blue 视觉重设计 — 2026-08-16

### 设计边界

- 参考界面仅作为颜色、按钮、线条、圆角、留白和交互状态的视觉参考。
- 不采用参考界面的左侧导航、首页仪表盘、代理卡片或业务操作逻辑。
- FPE2001-Remake 继续保持“标题栏 → 九模块横向导航 → 模块动作栏 → 工作区 → 状态栏”的既有布局。
- 九模块顺序、页面信息架构、命令绑定、快捷键和窗口控制逻辑全部保持不变。

| 视觉项 | v2.3 规范 | 当前实现 |
|------|-----------|----------|
| 主色 | 清晰但不过亮的工具蓝；用于选中、焦点和主操作 | `Primary #2F7BEA`、`PrimaryDark #1F5FBE`、`PrimarySoft #E6F1FF` |
| 背景层级 | 浅灰工作区 + 白色导航/面板 | `Window #F1F3F5`、`Canvas #F7F8FA`、`Surface #FFFFFF` |
| 线条 | 1 DIP 中性灰分隔线，焦点时切换为蓝色 | `Line #E1E5EA`、`LineStrong #CBD2DA` |
| 按钮 | 白底细边框；主按钮蓝底白字；危险按钮淡红反馈 | Ghost/Primary/Danger 三套状态模板已更新 |
| 模块导航 | 仍为顶部横排；选中态采用浅蓝圆角底和蓝色图标/文字 | `ModuleButton` 保留横向顺序及点击逻辑，仅替换视觉模板 |
| 输入控件 | 白底、灰色细边框、蓝色聚焦边框，不使用厚重阴影 | TextBox/ComboBox 去除默认投影 |
| 窗口按钮 | 仍位于最右端；灰色悬停，关闭按钮淡红悬停 | 保留 `WindowChrome` 命中修复与系统窗口命令 |
| 阴影 | 仅用于必要的浮层和卡片，低透明度 | 阴影透明度降低到 0.06–0.08 |

活动模块状态由 `MainViewModel.CurrentModule` 同步维护到各模块 `IsActive`，确保初始扫描页、鼠标切换和 Ctrl+1…9 快捷键切换后，导航选中态与实际工作区一致。

模块按钮实际高度为 60 DIP，上下外边距各 5 DIP，整体保持在 72 DIP 模块带内；图标、标签和焦点轮廓不得越界或遮挡下方文字。

## 待办

- [ ] UI-01 九模块导航 / DPI / 键盘验收截图对比（Design Kit screens/ 逐页对照）
- [ ] UI-02~UI-06 各阶段实现后补记差异

## v2.4 Deep Boundary Blue 视觉实施 — 2026-08-16

### 实施边界

- 保持 v2.3 已确认的“标题栏 → 九模块横向导航 → 模块动作栏 → 工作区 → 状态栏”布局。
- 保持页面信息架构、命令绑定、快捷键、进程扫描和十六进制编辑逻辑不变。
- 仅调整颜色层级、边界线、按钮反馈、图标底板和阴影强度，解决浅色界面中控件边界不清的问题。

| 视觉项 | v2.4 规范 | 当前实现 |
|------|-----------|----------|
| 窗口/画布 | `#DDE3EA` / `#E9EEF3` | `Theme/Colors.xaml` 的 `WindowBrush` / `CanvasBrush` |
| 内容面板 | 白色表面，与灰蓝工作区形成明确层级 | `SurfaceBrush #FFFFFF`，页面布局未改变 |
| 分隔线 | 普通边界 `#AAB6C4`，强调边界 `#75869A` | DataGrid、工具栏、状态栏和控件统一引用主题资源 |
| 文字 | 主文字 `#18222E`，辅助文字 `#4F5F70` | 全局 `TextBrush` / `MutedBrush` |
| 主操作 | 深蓝 `#245F9E`，按下态 `#174574` | `PrimaryButton`、选中模块和焦点态已更新 |
| 按钮反馈 | hover/pressed 使用灰蓝填充，危险按钮使用低饱和红色 | `GhostButton`、`PrimaryButton`、`DangerButton` 模板已更新 |
| 模块导航 | 顶部横排保持不变；选中态增加深边框和图标底板 | `ModuleButton` 保持 72 DIP 带内布局，标签不遮挡 |
| 窗口控制 | 最小化、最大化/还原、关闭固定最右端 | UI Automation 实测最大化后状态为 `Maximized`，关闭后进程正常退出 |
| 阴影 | 仅保留低透明度立体层次 | `SoftShadow` / `CardShadow` / `ButtonShadow` 透明度提升至 0.11–0.14 |

### 验证记录

- .NET 10 Release 构建：0 警告、0 错误。
- 单元测试：70 通过，0 失败，0 跳过。
- 实际运行窗口截图：`deliverables/FPE2001-Remake_UI_Mockups_v2.4/scan-deep-boundary-blue-implemented-v24.png`。
- 测试程序：`artifacts/ui-deep-boundary-blue-v24/Fpe2001Remake.UI.exe`。

## v2.5 进程列表优化 — 2026-08-16

### 可见性边界

- 扫描页仅展示交互式用户会话中的候选进程；`SessionId == 0` 的服务进程不展示。
- 隐藏已知 Windows 系统进程名（例如 `System`、`svchost`、`lsass`、`csrss`、`dwm`、`winlogon` 等）。
- 当可读取可执行文件路径时，隐藏位于 `%WINDIR%` 目录及其子目录的进程，避免仅依赖名称匹配。
- 进程名或路径读取失败时保留非系统会话候选，避免误伤游戏模拟器；当前 FPE2001-Remake 进程始终排除。
- 十六进制编辑器仍通过 PID 打开目标进程，过滤只影响扫描页下拉列表。

### UI 与验证

- 扫描页状态文本明确提示“Windows 系统进程已隐藏”，空列表时引导用户启动目标程序并刷新。
- `ProcessTargetFilterTests` 覆盖 Session 0、已知系统名、Windows 目录、游戏/模拟器、路径不可读和当前进程六类边界。
- Release 构建：0 警告、0 错误；单元测试：76 通过、0 失败、0 跳过。

## v2.6 窗口级边界与透明阴影 — 2026-08-16

> 已由 v2.7 废止：根 Border 的 `Margin=2` 会暴露窗口背景形成实色环带，WPF `DropShadowEffect` 也不是真正的窗口外阴影。

### 实施记录

- `MainWindow` 根内容由 `WindowFrame` Border 统一包裹，`Margin=2`、`BorderThickness=1`、`CornerRadius=WindowRadius`、`ClipToBounds=True`。
- `Theme/Colors.xaml` 新增 `WindowFrameBrush #75869A`，用于四边连续的 1 DIP 边框。
- 新增 `WindowFrameShadow`：`BlurRadius=4`、`ShadowDepth=0`、`Opacity=0.18`、`Color=#233447`，在边框外形成约 2 DIP 的低透明度阴影。
- `WindowChrome`、标题栏右侧窗口按钮和四行既有布局未改变；边框/阴影只属于 UI Shell，不参与页面命令或 ViewModel 状态。

### 验证记录

- Release 构建：0 警告、0 错误；启动冒烟测试通过。
- 文档验收：UI/UX v2.6、代码实施规格 v2.3、Win11 架构设计 v2.3 已完成回退渲染检查。
- DPI 验收要求：100%、125%、150%、200% 下边框连续，阴影不裁切，文字和窗口按钮不遮挡。

## v2.7 Windows 11 原生 DWM 窗口框架 — 2026-08-16

### 更正与实现

- 删除根级 `WindowFrame` Border 的 `Margin=2`、`ClipToBounds` 和 `WindowFrameShadow`，原四行 Grid 重新直接铺满客户区，窗口外不再出现同色填充带。
- `WindowChrome.GlassFrameThickness` 调整为 `1`；`CaptionHeight=46`、`ResizeBorderThickness=6`、自定义标题栏和窗口按钮逻辑保持不变。
- 新增 `Shell/Windows11WindowFrame.cs`，在 Windows 11 Build 22000+ 通过 `DwmSetWindowAttribute` 启用非客户区绘制、`DWMWCP_ROUND` 圆角和原生边框色。
- 活动窗口使用 `WindowFrameBrush #75869A`，非活动窗口使用 `LineBrush #AAB6C4`；DWM 负责系统/DPI 对齐的边框像素。
- 阴影由 Windows 11 DWM 在 HWND 之外合成，不参与 WPF 布局；宽度和透明度跟随系统、DPI 与激活状态，不再固定模拟为 2 DIP。
- 普通窗口四个圆角完整可见；最大化状态由系统取消圆角，行为与 Clash Verge 和 Windows 11 标准窗口一致。

### 验证记录

- Release 构建：0 警告、0 错误；单元测试：76 通过、0 失败、0 跳过。
- 带窗口外扩区域的实机截图确认：四角连续、外侧无实色环带、DWM 阴影位于窗口矩形之外。
- UI Automation：最小化、最大化、还原和关闭全部通过。
- 测试程序：`artifacts/ui-native-dwm-frame-v27/Fpe2001Remake.UI.exe`。
- 对应文档：UI/UX v2.7、代码实施规格 v2.4、Win11 架构设计 v2.4。

## v2.8 进程列表按程序名聚合 — 2026-08-16

### 实施记录

- 扫描页目标下拉框按 `ProcessName`（OrdinalIgnoreCase）合并，同一程序只显示一行纯程序名，不再把 PID 展开为数十行重复条目。
- `ScanApplicationService` 仍返回逐进程 `(ProcessId, ProcessName)`；`TargetItem` 在 UI 层保存程序名及完整 PID 集合，未改变扫描引擎、地址表或内存适配器契约。
- 多实例程序显示紧凑“实例”PID 选择器，扫描、再次扫描和添加地址表均使用 `SelectedProcessId`；单实例程序保持简洁布局。
- 刷新时优先恢复原程序和仍存在的 PID；状态文案同时显示目标程序数与进程总数，并继续提示 Windows 系统进程已隐藏。

### 验证记录

- UI Automation 展开真实下拉框确认：当前环境 223 个候选进程压缩为 52 个目标程序，列表视觉文本仅为程序名。
- 选择 `asus_framework` 等多实例程序后实例 ComboBox 出现并包含全部 PID；标题栏/状态栏同步最终 PID。
- Release 构建：0 警告、0 错误；单元测试：76 通过；集成测试：20 通过。
- 测试程序：`artifacts/ui-process-groups-v28/Fpe2001Remake.UI.exe`。
- 对应文档：UI/UX v2.8、代码实施规格 v2.5、Win11 架构设计 v2.5。
