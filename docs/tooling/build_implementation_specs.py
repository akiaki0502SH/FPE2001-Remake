#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont
from docx import Document
from docx.enum.text import WD_ALIGN_PARAGRAPH
from docx.oxml import OxmlElement
from docx.oxml.ns import qn
from docx.shared import Inches, Pt

import build_refactor_doc as base


ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = ROOT / "deliverables"
WORK_DIR = ROOT / "artifacts_work" / "implementation_specs"
CODE_OUT = OUT_DIR / "FPE_Next_代码实施规格包_v1.0.docx"
UI_OUT = OUT_DIR / "FPE_Next_UIUX实施规格_v1.0.docx"


def keep_with_next(paragraph):
    paragraph.paragraph_format.keep_with_next = True


def set_header_footer(doc: Document, short_title: str):
    for section in doc.sections:
        header = section.header
        p = header.paragraphs[0]
        p.text = ""
        p.alignment = WD_ALIGN_PARAGRAPH.RIGHT
        p.paragraph_format.space_after = Pt(0)
        r = p.add_run(short_title)
        base.set_run_font(r, base.FONT, 8.5, color=base.MUTED)

        footer = section.footer
        p0 = footer.paragraphs[0]
        p0._element.getparent().remove(p0._element)
        table = footer.add_table(rows=1, cols=2, width=Inches(6.5))
        base.set_table_geometry(table, [7000, 2360], indent=0)
        base.remove_table_borders(table)
        left = table.cell(0, 0).paragraphs[0]
        left.alignment = WD_ALIGN_PARAGRAPH.LEFT
        lr = left.add_run(short_title + " · v1.0")
        base.set_run_font(lr, base.FONT, 8.5, color=base.MUTED)
        right = table.cell(0, 1).paragraphs[0]
        right.alignment = WD_ALIGN_PARAGRAPH.RIGHT
        rr = right.add_run("第 ")
        base.set_run_font(rr, base.FONT, 8.5, color=base.MUTED)
        base.add_page_field(right)
        rr2 = right.add_run(" 页")
        base.set_run_font(rr2, base.FONT, 8.5, color=base.MUTED)


def cover(doc, kicker: str, title: str, subtitle: str, meta, conclusion: str):
    base.add_para(doc, kicker, size=10, bold=True, color=base.GOLD,
                  align=WD_ALIGN_PARAGRAPH.CENTER, before=58, after=22)
    base.add_para(doc, "FPE Next", size=32, bold=True, color=base.INK,
                  align=WD_ALIGN_PARAGRAPH.CENTER, after=7)
    base.add_para(doc, title, size=22, bold=True, color=base.BLUE,
                  align=WD_ALIGN_PARAGRAPH.CENTER, after=12)
    base.add_para(doc, subtitle, size=13, color=base.MUTED,
                  align=WD_ALIGN_PARAGRAPH.CENTER, after=52)
    table = doc.add_table(rows=len(meta), cols=2)
    base.set_table_geometry(table, [2300, 7060], indent=120)
    base.set_table_borders(table, color="D8DEE5", size=5)
    for i, (label, value) in enumerate(meta):
        base.set_cell_shading(table.cell(i, 0), base.LIGHT_BLUE)
        p1 = table.cell(i, 0).paragraphs[0]
        base.set_run_font(p1.add_run(label), base.FONT, 10, bold=True, color=base.INK)
        p2 = table.cell(i, 1).paragraphs[0]
        base.set_run_font(p2.add_run(value), base.FONT, 10, color="20262D")
    base.add_para(doc, conclusion, size=11.5, bold=True, color=base.DARK_BLUE,
                  align=WD_ALIGN_PARAGRAPH.CENTER, before=38, after=8)
    base.add_para(doc, "本规格与《FPE2001 Win11 重构分析与架构设计》配套使用；冲突时以本规格的可测试契约为准。",
                  size=9.8, color=base.MUTED, align=WD_ALIGN_PARAGRAPH.CENTER, after=0)
    doc.add_page_break()


def h(doc, text, level=1):
    return base.add_heading(doc, text, level)


def p(doc, text, **kwargs):
    return base.add_para(doc, text, **kwargs)


def bullets(doc, items, bullet_id, level=0):
    for item in items:
        base.add_bullet(doc, item, bullet_id, level)


def numbers(doc, items, number_id):
    for item in items:
        base.add_number(doc, item, number_id)


def code(doc, text):
    return base.add_code(doc, text.strip("\n"))


def page_break(doc):
    doc.add_page_break()


def trim_trailing_empty_paragraphs(doc):
    while doc.paragraphs and not doc.paragraphs[-1].text.strip():
        node = doc.paragraphs[-1]._element
        node.getparent().remove(node)


def add_terminal_paragraph(doc):
    terminal = doc.add_paragraph()
    terminal.paragraph_format.space_before = Pt(0)
    terminal.paragraph_format.space_after = Pt(0)
    terminal.paragraph_format.line_spacing = Pt(1)
    run = terminal.add_run(" ")
    base.set_run_font(run, base.FONT, 1)
    run.font.hidden = True


def add_contents(doc, items, number_id):
    h(doc, "阅读导航", 1)
    p(doc, "建议实现模型严格按章节顺序工作：先固定契约和测试，再写 UI；不要在首个里程碑中引入具体模拟器偏移。",
      color=base.MUTED, after=10)
    for item in items:
        base.add_number(doc, item, number_id)
    page_break(doc)


def build_code_spec():
    doc = Document()
    base.configure_styles(doc)
    bullet_id, decimal_ids = base.add_num_definitions(doc)
    set_header_footer(doc, "FPE Next 代码实施规格包")
    cover(
        doc,
        "IMPLEMENTATION SPECIFICATION PACKAGE",
        "代码实施规格包",
        "可由其他模型分阶段生成代码、测试与工程骨架的执行级规范",
        [
            ("目标平台", "Windows 11 x64；.NET 10 LTS；WPF；C# 14"),
            ("实现范围", "P0-P3：通用 Win32 扫描器、64 位地址、冻结写入、适配器协议与诊断"),
            ("安全边界", "仅本地/离线/单机；不包含内核驱动、DLL 注入、反作弊绕过"),
            ("文档版本", "v1.0 · 2026-08-15"),
        ],
        "交付标准：实现模型应能据此建立仓库、逐票编码，并通过明确的自动化验收。",
    )

    add_contents(doc, [
        "实施合同与冻结决策", "仓库与程序集结构", "运行时进程与依赖规则", "领域模型与公共 C# 契约",
        "Win32 内存访问层", "扫描引擎与结果快照", "写入、冻结与回滚", "适配器 SDK 与隔离主机",
        "命名管道 IPC 协议", "SQLite 数据模型与迁移", "日志、诊断与安全", "测试资产与验收矩阵",
        "P0-P3 实施任务单", "交给其他模型的执行提示词", "首个模拟器接入模板与待定项",
    ], decimal_ids[0])

    h(doc, "1. 实施合同与冻结决策")
    base.add_callout(doc, "实施原则", "本文件是“可执行合同”，不是方向性建议。标记为 MUST 的项不得由实现模型自行替换；标记为 TBD-EMU 的项只在选定模拟器后填写。")
    h(doc, "1.1 已冻结的技术决策", 2)
    base.add_table(doc, ["编号", "决策", "强制要求"], [
        ("ADR-001", "桌面技术", ".NET 10 LTS、C# 14、WPF、x64、self-contained；首版仅 win-x64。"),
        ("ADR-002", "进程边界", "UI、Broker、AdapterHost 三类进程；普通情况下不启动 Broker。"),
        ("ADR-003", "内存模型", "所有宿主地址使用 ulong/nuint；UI 统一显示 16 位十六进制。"),
        ("ADR-004", "互操作", "LibraryImport + SafeHandle；禁止 IntPtr.ToInt32 与有符号地址算术。"),
        ("ADR-005", "持久化", "SQLite 存元数据，扫描结果使用磁盘快照；不把百万候选写成 SQLite 行。"),
        ("ADR-006", "适配器", "进程外 AdapterHost；版本探测、能力协商、地址证明和诊断为必选。"),
        ("ADR-007", "安全范围", "不实现驱动、注入、隐藏、反调试规避或联网对战支持。"),
    ], [1150, 1900, 6310], font_size=9.2)
    h(doc, "1.2 完成定义", 2)
    bullets(doc, [
        "MUST：Release x64 构建无警告；所有测试通过；扫描可取消；退出后无孤儿 Broker/AdapterHost。",
        "MUST：x86 与 x64 合成目标均能完成附加、首次扫描、再次扫描、写入与冻结。",
        "MUST：地址超过 0x00000000FFFFFFFF 的测试不能截断；往返序列化值完全一致。",
        "MUST：每个跨进程请求含 correlationId、deadlineUtc 与 protocolVersion；未知消息返回结构化错误。",
        "SHOULD：关键路径具有 EventSource/结构化日志；诊断包不得默认包含读取到的游戏内存内容。",
    ], bullet_id)
    h(doc, "1.3 非目标", 2)
    bullets(doc, [
        "不追求复刻 2001 年界面；保留的是工作流和数据兼容入口。",
        "不保证所有进程均可写；受保护进程、权限不足和架构不兼容必须明确拒绝。",
        "首版不做 ARM64 原生、不做网络同步、不做云端脚本市场。",
        "图片工具、速度控制和全局宏属于 P4 候选，不阻塞核心扫描器发布。",
    ], bullet_id)

    h(doc, "2. 仓库与程序集结构")
    code(doc, r"""
FpeNext/
  FpeNext.slnx
  Directory.Build.props
  Directory.Packages.props
  global.json
  src/
    FpeNext.UI/                 # WPF shell, Views, ViewModels, composition root
    FpeNext.Application/        # use cases, orchestration, DTO mapping
    FpeNext.Domain/             # pure domain types; no WPF/Win32/SQLite references
    FpeNext.Memory.Win32/       # P/Invoke, SafeHandle, region/read/write services
    FpeNext.Scan/               # scan pipeline, comparators, snapshot format
    FpeNext.Storage/            # SQLite repositories and migrations
    FpeNext.Contracts/          # IPC DTOs and version negotiation
    FpeNext.AdapterSdk/         # adapter contracts and manifest model
    FpeNext.AdapterHost/        # isolated adapter process
    FpeNext.Broker/             # optional elevated helper; minimal commands
    FpeNext.LegacyImport/       # .ini/.fpe/.mis import only
  adapters/
    FpeNext.Adapter.GenericWin32/
    FpeNext.Adapter.LegacyIni/
    FpeNext.Adapter.Libretro/
  tests/
    FpeNext.Domain.Tests/
    FpeNext.Scan.Tests/
    FpeNext.Memory.Win32.Tests/
    FpeNext.Storage.Tests/
    FpeNext.Ipc.Tests/
    FpeNext.Adapter.ContractTests/
    FpeNext.EndToEnd.Tests/
    FpeNext.SyntheticTarget.x86/
    FpeNext.SyntheticTarget.x64/
  schemas/ ipc.schema.json adapter-manifest.schema.json
  docs/ adr/ protocol/ emulator-onboarding/
""")
    h(doc, "2.1 依赖方向", 2)
    base.add_table(doc, ["项目", "允许引用", "禁止引用"], [
        ("Domain", "BCL", "WPF、Win32、SQLite、AdapterHost"),
        ("Application", "Domain、Contracts、AdapterSdk 抽象", "具体 View、具体 P/Invoke"),
        ("Memory.Win32", "Domain", "UI、Storage"),
        ("Scan", "Domain、抽象读接口", "WPF、Broker 实现"),
        ("UI", "Application、Contracts", "直接 P/Invoke、直接打开 SQLite"),
        ("Broker", "Contracts、Memory.Win32 的最小子集", "UI 组件、适配器业务逻辑"),
        ("AdapterHost", "Contracts、AdapterSdk", "UI、直接数据库写入"),
    ], [1800, 3300, 4260], font_size=9.2)
    h(doc, "2.2 构建属性", 2)
    code(doc, r"""
<PropertyGroup>
  <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
  <PlatformTarget>x64</PlatformTarget>
  <Nullable>enable</Nullable>
  <ImplicitUsings>enable</ImplicitUsings>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <EnableNETAnalyzers>true</EnableNETAnalyzers>
  <AnalysisLevel>latest-recommended</AnalysisLevel>
  <Deterministic>true</Deterministic>
  <PublishSingleFile>false</PublishSingleFile>
  <SelfContained>true</SelfContained>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
</PropertyGroup>
""")

    h(doc, "3. 运行时进程与权限")
    base.add_table(doc, ["进程", "默认完整性级别", "职责", "允许的外部访问"], [
        ("FpeNext.UI.exe", "medium", "界面、任务编排、数据库入口", "仅连接本用户创建的管道"),
        ("FpeNext.AdapterHost.exe", "medium / low 视策略", "加载单个适配器、翻译地址、健康检查", "指定 PID；不得枚举无关目录"),
        ("FpeNext.Broker.exe", "按需 elevated", "为用户确认的目标打开最小权限句柄并代读/代写", "仅白名单 PID 与请求范围"),
    ], [1700, 1900, 3300, 2460], font_size=9)
    h(doc, "3.1 会话生命周期", 2)
    numbers(doc, [
        "UI 枚举目标并记录 ProcessId、StartTimeUtc、ExecutablePath、MachineType。",
        "选择适配器后先 Probe；评分低于阈值时只能使用通用 Win32 模式。",
        "Attach 前再次读取进程启动时间，防止 PID 复用；不一致则中止。",
        "按需启动 AdapterHost；只有 ERROR_ACCESS_DENIED 且用户确认后才启动 Broker。",
        "Detach 顺序为：停止冻结任务 → 取消扫描 → 释放句柄 → 关闭适配器 → 清理临时快照。",
    ], decimal_ids[1])
    h(doc, "3.2 最小权限", 2)
    bullets(doc, [
        "只读扫描：PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_VM_READ。",
        "写入：在用户执行写入/冻结时追加 PROCESS_VM_WRITE | PROCESS_VM_OPERATION。",
        "禁止默认请求 PROCESS_ALL_ACCESS；Broker 不接受任意句柄复制或任意 DLL 路径。",
        "管道 ACL 限定当前用户 SID；握手额外校验父进程 PID、实例 nonce 与协议版本。",
    ], bullet_id)

    h(doc, "4. 领域模型与公共 C# 契约")
    h(doc, "4.1 地址与进程身份", 2)
    code(doc, r"""
public enum AddressSpace
{
    HostVirtual = 0,
    GuestPhysical = 1,
    GuestVirtual = 2,
    ModuleRelative = 3,
    PointerChain = 4
}

public readonly record struct ProcessIdentity(
    int ProcessId,
    DateTimeOffset StartTimeUtc,
    string ExecutablePath,
    ushort ProcessMachine,
    ushort NativeMachine);

public readonly record struct LogicalAddress(
    AddressSpace Space,
    string DomainId,
    ulong Value,
    Endianness Endianness,
    byte PointerWidthBits);

public sealed record ResolutionProof(
    string Strategy,
    string AdapterId,
    string AdapterVersion,
    string TargetVersion,
    IReadOnlyDictionary<string, string> Evidence);

public sealed record ResolvedAddress(
    LogicalAddress Source,
    ulong HostVirtualAddress,
    ProcessIdentity Process,
    ResolutionProof Proof);
""")
    h(doc, "4.2 读写与扫描接口", 2)
    code(doc, r"""
public interface IMemoryReader
{
    ValueTask<MemoryReadResult> ReadAsync(
        ProcessIdentity process, ulong address, Memory<byte> destination,
        CancellationToken cancellationToken);
}

public interface IMemoryWriter
{
    ValueTask<MemoryWriteResult> WriteAsync(
        ProcessIdentity process, ulong address, ReadOnlyMemory<byte> source,
        WriteVerification verification, CancellationToken cancellationToken);
}

public interface IMemoryRegionProvider
{
    IAsyncEnumerable<MemoryRegion> EnumerateReadableRegionsAsync(
        ProcessIdentity process, AddressRange range,
        [EnumeratorCancellation] CancellationToken cancellationToken);
}

public interface IScanEngine
{
    Task<ScanSnapshotDescriptor> StartAsync(InitialScanRequest request,
        IProgress<ScanProgress> progress, CancellationToken cancellationToken);
    Task<ScanSnapshotDescriptor> RefineAsync(RefineScanRequest request,
        IProgress<ScanProgress> progress, CancellationToken cancellationToken);
}
""")
    h(doc, "4.3 值类型", 2)
    base.add_table(doc, ["ValueKind", "宽度", "比较", "备注"], [
        ("Int8/16/32/64", "1/2/4/8", "等于、不等、增减、范围、变化/未变", "按端序解码"),
        ("UInt8/16/32/64", "1/2/4/8", "同上", "UI 禁止负数"),
        ("Float32/64", "4/8", "近似、范围、增减、变化", "默认 epsilon：绝对+相对"),
        ("Bytes", "1-256", "精确、掩码", "掩码字节用 ??"),
        ("Text", "可配置", "精确、包含", "UTF-8/UTF-16LE；P2"),
    ], [1800, 1250, 3400, 2910], font_size=9.2)
    base.add_callout(doc, "禁止项", "地址、长度、偏移的核心模型不得使用 int/uint；长度可在单次 OS 调用边界转换为 nuint，但转换前必须检查上限。")

    h(doc, "5. Win32 内存访问层")
    h(doc, "5.1 互操作签名基线", 2)
    code(doc, r"""
internal static partial class NativeMethods
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial SafeProcessHandle OpenProcess(
        ProcessAccessRights desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inherit,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nuint VirtualQueryEx(
        SafeProcessHandle process, nuint address,
        out MEMORY_BASIC_INFORMATION64 buffer, nuint length);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ReadProcessMemory(
        SafeProcessHandle process, nuint baseAddress,
        [Out] byte[] buffer, nuint size, out nuint bytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WriteProcessMemory(
        SafeProcessHandle process, nuint baseAddress,
        byte[] buffer, nuint size, out nuint bytesWritten);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWow64Process2(
        SafeProcessHandle process, out ushort processMachine, out ushort nativeMachine);
}
""")
    p(doc, "实现可用 ReadOnlySpan/Span 包装上层 API，但生成的 LibraryImport 存根必须在目标 SDK 上编译验证；若数组签名影响性能，允许在 Memory.Win32 内部改为 unsafe 指针重载，公共接口不变。", color=base.MUTED)
    h(doc, "5.2 区域过滤与读取", 2)
    bullets(doc, [
        "仅 MEM_COMMIT；跳过 PAGE_NOACCESS、PAGE_GUARD；默认接受 READONLY、READWRITE、WRITECOPY、EXECUTE_READ、EXECUTE_READWRITE、EXECUTE_WRITECOPY。",
        "VirtualQueryEx 从 0 开始推进到用户态最大地址；next = BaseAddress + RegionSize 必须 checked，若 next <= current 立即终止并记录错误。",
        "默认块大小 1 MiB，可在 64 KiB-16 MiB 调节；跨块搜索保留 valueWidth-1 字节重叠。",
        "ERROR_PARTIAL_COPY 时以 bytesRead 处理已读部分，再按页面边界细分重试；不可无限重试。",
        "进度以已计划可读字节为分母；区域变化导致分母变化时进度不得倒退。",
    ], bullet_id)
    h(doc, "5.3 错误模型", 2)
    code(doc, r"""
public enum MemoryErrorCode
{
    None, AccessDenied, ProcessExited, ProcessIdentityChanged,
    InvalidAddress, PartialCopy, RegionChanged, ArchitectureUnsupported,
    Cancelled, NativeFailure
}

public sealed record MemoryError(
    MemoryErrorCode Code, int? Win32Error, string Operation,
    ulong? Address, int? RequestedLength, int? CompletedLength,
    bool IsRetryable, string UserMessageKey);
""")

    h(doc, "6. 扫描引擎与结果快照")
    h(doc, "6.1 状态机", 2)
    code(doc, "Idle -> Planning -> Reading -> Comparing -> Persisting -> Completed\n" +
              "                         |            |\n" +
              "                         +----------> Cancelled\n" +
              "Any non-terminal state ----------------> Failed")
    bullets(doc, [
        "同一 ScanSession 只允许一个活动任务；再次点击开始必须被 UI 层禁用并由服务层二次拒绝。",
        "取消是成功的终态，不显示为错误；取消后删除未提交的 .tmp，保留上一个完整快照。",
        "Refine 必须校验目标进程身份、值类型、字节序和上一个快照校验和。",
    ], bullet_id)
    h(doc, "6.2 初扫与再扫", 2)
    base.add_table(doc, ["场景", "输入", "输出规则"], [
        ("精确初扫", "已知值", "按对齐步长查找；默认步长 1，可选自然对齐。"),
        ("未知初扫", "无值", "保存候选地址与基线值；必须有磁盘空间预估和上限提示。"),
        ("精确再扫", "新值", "只读候选地址，匹配则写入新快照。"),
        ("变化再扫", "上次值", "按 Increase/Decrease/Changed/Unchanged 过滤并更新基线。"),
        ("范围再扫", "min/max", "含边界；浮点 NaN 默认不匹配。"),
    ], [1600, 2100, 5660], font_size=9.2)
    h(doc, "6.3 快照二进制格式 FPSN v1", 2)
    base.add_table(doc, ["字段", "大小", "编码/约束"], [
        ("Magic", "4", "ASCII FPSN"), ("Version", "2", "UInt16 LE = 1"),
        ("HeaderLength", "2", "UInt16 LE"), ("SessionId", "16", "Guid bytes"),
        ("ProcessStartTicks", "8", "Int64 LE UTC ticks"), ("ValueKind", "2", "枚举值"),
        ("ValueWidth", "2", "1-256"), ("CandidateCount", "8", "UInt64 LE"),
        ("PayloadLength", "8", "UInt64 LE"), ("PayloadSha256", "32", "原始 32 字节"),
        ("Payload", "N", "按 hostAddress UInt64 LE 升序；每条 address + previousValue"),
    ], [2200, 1300, 5860], font_size=9)
    bullets(doc, [
        "写入先落 .tmp，Flush(true)，校验长度与 SHA-256 后原子改名为 .fpsn。",
        "候选必须按地址升序；重复地址视为损坏；读取器对未知 Version fail closed。",
        "结果网格只加载可视窗口；不得一次把 CandidateCount 转成 ObservableCollection。",
        "默认保留最近 10 个完整快照；配额按会话和全局双重控制。",
    ], bullet_id)

    h(doc, "7. 写入、冻结与回滚")
    h(doc, "7.1 写入语义", 2)
    code(doc, r"""
public enum WriteVerification { None, ReadBackExact }
public enum FreezeConditionKind { Always, Equals, NotEquals, InRange, Changed }

public sealed record WriteRequest(
    Guid RequestId, ResolvedAddress Address, byte[] Value,
    byte[]? ExpectedCurrentValue, WriteVerification Verification,
    bool CaptureRollbackValue);

public sealed record FreezeRule(
    Guid RuleId, ResolvedAddress Address, byte[] TargetValue,
    FreezeConditionKind Condition, byte[]? OperandA, byte[]? OperandB,
    TimeSpan Interval, bool Enabled);
""")
    bullets(doc, [
        "单次写入默认 ReadBackExact；失败不得悄悄重试到其他地址。",
        "若 ExpectedCurrentValue 非空，先比较再写；不匹配返回 Conflict，不做写入。",
        "回滚值只在本次进程身份有效期内使用；进程重启后按钮禁用。",
        "冻结调度采用单个优先队列，不为每条规则创建线程；最短间隔 16 ms，默认 250 ms。",
        "全局停止必须在 500 ms 内阻止新写入；连续 3 次失败自动停用该规则并提示。",
    ], bullet_id)
    h(doc, "7.2 审计事件", 2)
    base.add_table(doc, ["事件", "必须记录", "不得记录"], [
        ("WriteAttempt", "规则/请求 ID、目标身份、逻辑与宿主地址、字节数、结果", "用户读取到的完整邻近内存"),
        ("FreezeStateChanged", "启停原因、失败计数、下次计划时间", "无关进程信息"),
        ("Rollback", "原请求 ID、回滚结果、校验结果", "诊断包中默认包含回滚字节"),
    ], [1900, 4700, 2760], font_size=9)

    h(doc, "8. 适配器 SDK 与隔离主机")
    h(doc, "8.1 SDK 契约", 2)
    code(doc, r"""
public interface IEmulatorAdapter : IAsyncDisposable
{
    AdapterDescriptor Descriptor { get; }
    ValueTask<ProbeResult> ProbeAsync(ProcessIdentity process, CancellationToken ct);
    ValueTask<AttachResult> AttachAsync(ProcessIdentity process, CancellationToken ct);
    ValueTask<IReadOnlyList<MemoryDomain>> EnumerateDomainsAsync(CancellationToken ct);
    ValueTask<ResolvedAddress> ResolveAsync(LogicalAddress address, CancellationToken ct);
    ValueTask<MemoryReadResult> ReadAsync(LogicalAddress address, Memory<byte> destination, CancellationToken ct);
    ValueTask<MemoryWriteResult> WriteAsync(LogicalAddress address, ReadOnlyMemory<byte> source, CancellationToken ct);
    ValueTask<AdapterHealth> HealthCheckAsync(CancellationToken ct);
    ValueTask<DiagnosticsDescriptor> CreateDiagnosticsAsync(DiagnosticsOptions options, CancellationToken ct);
}

public interface IEmulatorExecutionControl
{
    ValueTask PauseAsync(CancellationToken ct);
    ValueTask ResumeAsync(CancellationToken ct);
}
""")
    h(doc, "8.2 能力与探测", 2)
    bullets(doc, [
        "ProbeResult 包含 Score(0-100)、TargetVersion、Architecture、Evidence、Warnings；不允许仅按窗口标题返回高分。",
        "能力位：Read、Write、Domains、GuestVirtualTranslation、PauseResume、FrameSync、SaveRam、Diagnostics。",
        "Attach 后返回 AdapterSessionId；每次 Resolve 的 Proof 必须包含策略、模块/签名证据和目标版本。",
        "适配器崩溃只结束当前适配器会话；UI 与扫描数据库必须存活并给出可重试提示。",
    ], bullet_id)
    h(doc, "8.3 manifest 示例", 2)
    code(doc, r"""
{
  "schemaVersion": 1,
  "id": "org.fpenext.generic-win32",
  "displayName": "通用 Win32 进程",
  "adapterVersion": "1.0.0",
  "entryAssembly": "FpeNext.Adapter.GenericWin32.dll",
  "supportedProcessMachines": ["I386", "AMD64"],
  "executableMatchers": [{ "fileName": "*", "sha256": [] }],
  "capabilities": ["Read", "Write", "Diagnostics"],
  "minimumHostProtocol": "1.0",
  "trust": { "publisher": "FPE Next", "signatureRequired": true }
}
""")

    h(doc, "9. 命名管道 IPC 协议")
    h(doc, "9.1 帧与信封", 2)
    code(doc, r"""
Frame := UInt32LE payloadLength + UTF8 JSON payload
Maximum payloadLength := 8 MiB

{
  "protocolVersion": "1.0",
  "messageType": "memory.read.request",
  "messageId": "guid",
  "correlationId": "guid-or-null",
  "sessionId": "guid",
  "deadlineUtc": "2026-08-15T10:30:00.000Z",
  "body": { }
}
""")
    h(doc, "9.2 必须实现的消息", 2)
    base.add_table(doc, ["方向", "messageType", "关键字段"], [
        ("UI→Host", "hello.request", "supportedVersions、clientPid、nonce"),
        ("Host→UI", "hello.response", "selectedVersion、hostPid、capabilities、nonceProof"),
        ("UI→Host", "adapter.probe/attach/detach", "processIdentity、adapterId"),
        ("UI→Host", "domain.list / address.resolve", "domainId、logicalAddress"),
        ("UI→Host", "memory.read/write", "resolved proof 或 logicalAddress、bytes"),
        ("双向", "cancel.request / operation.progress", "operationId、stage、completed、total"),
        ("Host→UI", "error.response", "code、messageKey、details、retryable"),
    ], [1450, 2900, 5010], font_size=9.2)
    bullets(doc, [
        "序列化 ulong 地址为十六进制字符串（例如 0x00007FF612341000），禁止 JSON number 精度风险。",
        "字节数组小于 64 KiB 用 Base64；更大数据使用共享临时文件句柄/受控路径描述符，文件有 SHA-256 与长度。",
        "收到 deadline 后不保证完成；服务必须尽快取消并返回 DeadlineExceeded。",
        "协议次版本只允许向后兼容新增可选字段；未知必选能力导致握手失败。",
    ], bullet_id)

    h(doc, "10. SQLite 数据模型与迁移")
    code(doc, r"""
PRAGMA journal_mode=WAL;
PRAGMA foreign_keys=ON;

CREATE TABLE schema_migrations(
  version INTEGER PRIMARY KEY,
  applied_utc TEXT NOT NULL,
  checksum TEXT NOT NULL
);

CREATE TABLE scan_sessions(
  id TEXT PRIMARY KEY,
  process_id INTEGER NOT NULL,
  process_start_utc TEXT NOT NULL,
  executable_path TEXT NOT NULL,
  adapter_id TEXT,
  address_space INTEGER NOT NULL,
  domain_id TEXT NOT NULL,
  value_kind INTEGER NOT NULL,
  endianness INTEGER NOT NULL,
  created_utc TEXT NOT NULL,
  state INTEGER NOT NULL
);

CREATE TABLE scan_snapshots(
  id TEXT PRIMARY KEY,
  session_id TEXT NOT NULL REFERENCES scan_sessions(id) ON DELETE CASCADE,
  sequence_no INTEGER NOT NULL,
  predicate_json TEXT NOT NULL,
  candidate_count INTEGER NOT NULL,
  file_name TEXT NOT NULL,
  file_sha256 TEXT NOT NULL,
  created_utc TEXT NOT NULL,
  UNIQUE(session_id, sequence_no)
);

CREATE TABLE address_entries(
  id TEXT PRIMARY KEY,
  label TEXT NOT NULL,
  logical_address_json TEXT NOT NULL,
  value_kind INTEGER NOT NULL,
  display_format INTEGER NOT NULL,
  freeze_rule_json TEXT,
  comment TEXT NOT NULL DEFAULT '',
  sort_order INTEGER NOT NULL,
  updated_utc TEXT NOT NULL
);

CREATE TABLE adapter_profiles(
  id TEXT PRIMARY KEY,
  adapter_id TEXT NOT NULL,
  target_fingerprint TEXT NOT NULL,
  settings_json TEXT NOT NULL,
  last_verified_utc TEXT,
  UNIQUE(adapter_id, target_fingerprint)
);
""")
    bullets(doc, [
        "迁移只前进不回退；启动时在事务中校验 checksum，失败进入只读恢复模式。",
        "逻辑地址以版本化 JSON 保存；宿主地址不作为跨会话持久标识。",
        "数据库位于 %LocalAppData%\\FpeNext\\data；快照位于同目录 snapshots 子目录。",
        "旧 .fpe/.mis 导入先生成预览，未知字段原样保存到 legacy_metadata_json。",
    ], bullet_id)

    h(doc, "11. 日志、诊断与安全")
    base.add_table(doc, ["类别", "要求"], [
        ("结构化日志", "Serilog 或等价抽象；JSON lines；operationId、sessionId、adapterId、elapsedMs。"),
        ("敏感数据", "默认不记录内存值、ROM 路径完整内容、用户宏按键序列；路径可哈希或仅文件名。"),
        ("诊断包", "manifest、版本、日志、适配器证据、区域统计；用户勾选后才包含配置。"),
        ("崩溃", "进程级未处理异常写最小 dump 为显式可选；无自动上传。"),
        ("签名", "发布构建签名主程序和官方适配器；第三方适配器默认隔离且显示信任状态。"),
    ], [1850, 7510], font_size=9.4)
    h(doc, "11.1 威胁检查", 2)
    bullets(doc, [
        "管道冒充：ACL + PID + nonce + 进程路径校验。",
        "PID 复用：所有请求携带 StartTimeUtc；每次关键操作复验。",
        "地址溢出：所有加法 checked；区域迭代检测不前进。",
        "恶意适配器：进程隔离、资源限额、协议白名单、诊断脱敏。",
        "误写：显式写权限、写前比较、读回校验、全局停止、审计。",
    ], bullet_id)

    h(doc, "12. 测试资产与验收矩阵")
    h(doc, "12.1 合成目标", 2)
    bullets(doc, [
        "x86 与 x64 各一个 console target，启动后输出 PID、进程启动时间和测试区地址。",
        "分配：只读页、读写页、保护页、跨页值、浮点 NaN、UTF-16 字符串、周期变化计数器。",
        "x64 目标必须尝试保留高于 4 GiB 的地址；无法固定时测试可注入 IAddressMath 进行上界验证。",
        "支持命令：reset、mutate、protect、decommit、exit；用于重现区域变化和进程退出。",
    ], bullet_id)
    h(doc, "12.2 自动化验收", 2)
    base.add_table(doc, ["ID", "场景", "通过标准"], [
        ("AT-001", "64 位地址往返", "0x00007FFFABCDEF00 经 Domain/IPC/DB/UI 格式化后数值不变。"),
        ("AT-002", "首次扫描", "在合成目标中找到唯一 Int32 值，结果地址正确。"),
        ("AT-003", "未知值再扫", "变化后候选单调减少；基线更新正确。"),
        ("AT-004", "跨块值", "块边界上的 8 字节值不漏报、不重复。"),
        ("AT-005", "部分读取", "保护页产生可解释警告；其他页仍完成。"),
        ("AT-006", "取消", "1 秒内进入 Cancelled；无损坏快照和孤儿临时文件。"),
        ("AT-007", "写入与回滚", "写后读回一致；回滚恢复原值。"),
        ("AT-008", "冻结", "目标被外部改变后在容差窗口恢复；停止后不再写。"),
        ("AT-009", "PID 复用/重启", "旧会话所有写入被 ProcessIdentityChanged 拒绝。"),
        ("AT-010", "AdapterHost 崩溃", "UI 不退出；会话显示断开且可重新附加。"),
    ], [1200, 2500, 5660], font_size=9)

    h(doc, "13. P0-P3 实施任务单")
    base.add_callout(doc, "执行方式", "每张任务单独提交；先写失败测试，再实现；每票结束更新 docs/adr 或协议示例。禁止一次让模型生成整个产品。")
    h(doc, "P0：工程与 64 位基础（建议 5-8 天）", 2)
    base.add_table(doc, ["票号", "交付", "验收"], [
        ("P0-01", "建立 slnx、项目、统一构建属性、CI", "Debug/Release win-x64 全部构建；依赖方向测试通过。"),
        ("P0-02", "Domain 地址/身份/错误模型", "边界值、checked 溢出、JSON 往返测试。"),
        ("P0-03", "SafeHandle 与 Win32 interop", "x86/x64 目标枚举、打开、关闭无句柄泄漏。"),
        ("P0-04", "合成目标与端到端夹具", "CI 可启动、控制、退出；残留进程检测通过。"),
    ], [1200, 4000, 4160], font_size=9.2)
    h(doc, "P1：扫描 MVP（建议 8-12 天）", 2)
    base.add_table(doc, ["票号", "交付", "验收"], [
        ("P1-01", "区域规划与分块读取", "保护页/部分读取/取消测试通过。"),
        ("P1-02", "整数/浮点比较器", "端序、NaN、epsilon、自然对齐用例通过。"),
        ("P1-03", "FPSN v1 读写器", "损坏/截断/未知版本拒绝；原子提交。"),
        ("P1-04", "首次/再次扫描服务", "AT-002 至 AT-006 通过。"),
        ("P1-05", "SQLite v1 与恢复", "迁移 checksum、WAL、只读恢复测试。"),
    ], [1200, 4000, 4160], font_size=9.2)
    h(doc, "P2：写入、冻结和桌面闭环（建议 7-10 天）", 2)
    base.add_table(doc, ["票号", "交付", "验收"], [
        ("P2-01", "写入/校验/回滚", "AT-007、身份复验和权限拒绝测试。"),
        ("P2-02", "冻结调度器", "AT-008；500 ms 全停；失败熔断。"),
        ("P2-03", "UI 应用服务接线", "目标→扫描→结果→地址表→冻结完整流程。"),
        ("P2-04", "旧配置预览导入", "Fpe.ini 六条记录可预览；不猜测未知字段。"),
    ], [1200, 4000, 4160], font_size=9.2)
    h(doc, "P3：适配器平台（建议 7-12 天）", 2)
    base.add_table(doc, ["票号", "交付", "验收"], [
        ("P3-01", "Contracts + 管道握手", "版本/nonce/超时/未知消息测试。"),
        ("P3-02", "AdapterHost 生命周期", "一实例一适配器；崩溃恢复；资源清理。"),
        ("P3-03", "GenericWin32 适配器", "契约测试全部通过，作为参考实现。"),
        ("P3-04", "LegacyIni 适配器", "只做可验证的基址映射；不支持版本则 fail closed。"),
        ("P3-05", "首个模拟器适配", "按第 15 章资料补全后另立任务组。"),
    ], [1200, 4000, 4160], font_size=9.2)

    h(doc, "14. 交给其他模型的执行提示词")
    h(doc, "14.1 单票实现模板", 2)
    code(doc, r"""
你正在实现 FPE Next。只处理任务票 {TICKET_ID}。

输入：本规格第 {SECTIONS} 节、现有仓库、该票验收测试。
约束：不得改变冻结 ADR；不得引入驱动/注入；不得跳过测试；不得把地址降为 32 位。

按顺序执行：
1. 阅读相关项目与测试，列出计划和风险。
2. 先增加或修正自动化测试，使测试在实现前失败。
3. 完成最小实现；保持公共契约不变。
4. 运行受影响测试和 Release x64 构建。
5. 输出：修改文件、测试结果、未解决问题、是否满足 Definition of Done。

若规格缺少会改变公共契约的信息，停止并标记 SPEC-GAP；不要自行发明。
""")
    h(doc, "14.2 代码审查模板", 2)
    code(doc, r"""
审查 {TICKET_ID} 的变更。以本规格为唯一验收基线，重点检查：
- ulong/nuint 地址是否被截断或做了未检查算术；
- PID 复用、取消、超时、部分读取和资源释放；
- IPC/数据库是否保持向后兼容；
- 是否越权请求 PROCESS_ALL_ACCESS；
- 测试是否真正覆盖验收条件。

按 P0/P1/P2/P3 严重度列出问题，并给出精确文件与行号；无问题时明确写“未发现阻塞项”。
""")

    h(doc, "15. 首个模拟器接入模板与待定项")
    base.add_table(doc, ["字段", "必须提供/验证的内容"], [
        ("模拟器身份", "名称、官网来源、精确版本、exe SHA-256、x86/x64、安装形态。"),
        ("测试资产", "合法 ROM/测试程序、可重复的已知数值、存档、复现步骤。"),
        ("内存域", "GuestPhysical/GuestVirtual、起止范围、字节序、映射/银行切换。"),
        ("优先接口", "官方调试 API、插件、IPC、Libretro 暴露域；不存在时再评估签名。"),
        ("变更容忍", "版本匹配规则、签名验证、探测评分、失败时是否允许手动校准。"),
        ("能力", "读、写、暂停、帧同步、Save RAM、作弊码格式转换。"),
        ("验收", "至少 3 个已知地址、冷启动/加载存档/切换游戏、连续 30 分钟健康检查。"),
    ], [2100, 7260], font_size=9.2)
    h(doc, "15.1 SPEC-GAP 列表", 2)
    bullets(doc, [
        "TBD-EMU-001：首个模拟器与精确版本尚未选定。",
        "TBD-FMT-001：.fpe/.mis/.mi0 二进制格式未完成逆向；首版只能保守导入已验证字段。",
        "TBD-SIGN-001：发布代码签名证书与第三方适配器信任策略需由项目所有者决定。",
        "TBD-PERF-001：默认扫描内存上限和磁盘配额应在第一轮真实机器基准后冻结。",
    ], bullet_id)
    base.add_callout(doc, "可开始实施", "上述 TBD 不阻塞 P0-P3 的通用引擎与适配器平台；只有 P3-05 需要先确定模拟器。", fill="EAF4EE", label_color=base.GREEN)

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    trim_trailing_empty_paragraphs(doc)
    add_terminal_paragraph(doc)
    doc.save(CODE_OUT)
    return CODE_OUT


def font(size, bold=False):
    regular = Path(r"C:\Windows\Fonts\msyh.ttc")
    boldp = Path(r"C:\Windows\Fonts\msyhbd.ttc")
    return ImageFont.truetype(str(boldp if bold and boldp.exists() else regular), size)


def draw_ui_mockups():
    WORK_DIR.mkdir(parents=True, exist_ok=True)
    paths = []

    def txt(d, xy, value, size=20, color="#203040", bold=False, anchor=None):
        d.text(xy, value, font=font(size, bold), fill=color, anchor=anchor)

    def rect(d, box, fill="#FFFFFF", outline="#C6D0DA", width=2, radius=8):
        d.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)

    # Shell + target selection
    img = Image.new("RGB", (1600, 980), "#F5F7FA")
    d = ImageDraw.Draw(img)
    d.rectangle((0, 0, 1600, 58), fill="#FFFFFF")
    txt(d, (28, 18), "FPE Next", 25, "#0B2545", True)
    txt(d, (1450, 18), "设置   帮助", 18, "#516170")
    d.rectangle((0, 58, 225, 930), fill="#F9FBFD")
    items = ["目标", "扫描", "结果与地址表", "内存查看器", "适配器", "诊断"]
    for i, item in enumerate(items):
        y = 92 + i * 62
        if i == 0:
            rect(d, (18, y-10, 207, y+37), "#E8F1FB", "#B8D3EE", 1, 8)
        txt(d, (40, y), item, 19, "#154E7D" if i == 0 else "#435565", i == 0)
    txt(d, (262, 92), "选择目标", 30, "#0B2545", True)
    txt(d, (262, 136), "附加到本机正在运行的游戏或模拟器。", 18, "#657482")
    rect(d, (260, 182, 1535, 246), "#FFFFFF")
    txt(d, (284, 202), "搜索进程、可执行文件或窗口标题", 18, "#8794A0")
    rect(d, (260, 270, 1535, 710), "#FFFFFF")
    d.rectangle((261, 271, 1534, 324), fill="#EDF2F7")
    headers = [(285,"进程"),(620,"PID"),(760,"架构"),(910,"适配器建议"),(1260,"状态")]
    for x,t in headers: txt(d,(x,287),t,17,"#33495C",True)
    rows = [
        ("RetroArch.exe", "13840", "x64", "Libretro bridge · 96", "可附加"),
        ("ePSXe.exe", "8420", "x86", "Legacy INI · 62", "需验证版本"),
        ("SampleGame.exe", "19412", "x64", "通用 Win32 · 40", "只读可用"),
    ]
    for i,row in enumerate(rows):
        y=352+i*82
        if i==0: d.rectangle((270,y-16,1525,y+48),fill="#F1F7FD")
        for (x,_), val in zip(headers,row): txt(d,(x,y),val,18,"#20394E", i==0 and x==285)
    txt(d,(285,670),"提示：选择后会先进行能力探测，不会立即申请管理员权限。",16,"#6A7783")
    rect(d,(1165,760,1535,820),"#1769AA","#1769AA",2,8)
    txt(d,(1350,790),"附加并进入扫描",19,"#FFFFFF",True,"mm")
    d.rectangle((0,930,1600,980),fill="#FFFFFF")
    txt(d,(28,946),"未附加目标",16,"#6B7783")
    path=WORK_DIR/"ui_shell_target.png"; img.save(path); paths.append(path)

    # Scan workspace
    img = Image.new("RGB", (1600, 980), "#F5F7FA")
    d = ImageDraw.Draw(img)
    d.rectangle((0,0,1600,58),fill="#FFFFFF")
    txt(d,(28,18),"FPE Next",25,"#0B2545",True)
    txt(d,(1140,18),"RetroArch.exe  ·  x64  ·  已附加",18,"#2C6E49",True)
    d.rectangle((0,58,225,930),fill="#F9FBFD")
    for i,item in enumerate(items):
        y=92+i*62
        if i==1: rect(d,(18,y-10,207,y+37),"#E8F1FB","#B8D3EE",1,8)
        txt(d,(40,y),item,19,"#154E7D" if i==1 else "#435565",i==1)
    txt(d,(262,92),"扫描工作区",30,"#0B2545",True)
    txt(d,(262,136),"System RAM  ·  GuestPhysical  ·  小端",18,"#657482")
    rect(d,(260,180,710,790),"#FFFFFF")
    txt(d,(285,206),"扫描条件",22,"#20394E",True)
    labels=[("数值类型","32 位有符号整数"),("扫描方式","精确值"),("输入值","100"),("字节序","小端"),("地址范围","0x00000000 - 0x01FFFFFF"),("对齐","1 字节")]
    for i,(lab,val) in enumerate(labels):
        y=262+i*72; txt(d,(285,y),lab,16,"#6A7783"); rect(d,(405,y-13,680,y+34),"#FAFCFE"); txt(d,(425,y),val,17,"#20394E")
    rect(d,(285,710,680,770),"#1769AA","#1769AA")
    txt(d,(482,740),"开始首次扫描",19,"#FFFFFF",True,"mm")
    rect(d,(740,180,1535,790),"#FFFFFF")
    txt(d,(765,206),"候选结果",22,"#20394E",True)
    d.rectangle((741,252,1534,305),fill="#EDF2F7")
    for x,t in [(765,"逻辑地址"),(990,"当前值"),(1130,"上次值"),(1270,"宿主地址"),(1450,"操作")]: txt(d,(x,268),t,16,"#33495C",True)
    for i in range(5):
        y=335+i*69
        vals=[f"0x{0x1000+i*4:08X}",str(100+i%2),str(99+i%2),f"0x00007FF6{0x12341000+i*4:08X}"[-18:],"＋"]
        for (x,_),v in zip([(765,''),(990,''),(1130,''),(1270,''),(1470,'')],vals): txt(d,(x,y),v,16,"#233B4F")
    rect(d,(260,815,1535,902),"#FFF8E8","#E1C66D")
    txt(d,(285,836),"扫描中  42%",18,"#705400",True)
    d.rectangle((420,842,1260,858),fill="#E5E8EC"); d.rectangle((420,842,773,858),fill="#D49B18")
    txt(d,(1285,835),"取消",18,"#8A2E2E",True)
    d.rectangle((0,930,1600,980),fill="#FFFFFF"); txt(d,(28,946),"已读取 512 MiB / 1.2 GiB  ·  发现 24,381 个候选",16,"#4E6070")
    path=WORK_DIR/"ui_scan_workspace.png"; img.save(path); paths.append(path)

    # Address table and freeze
    img = Image.new("RGB", (1600, 980), "#F5F7FA")
    d = ImageDraw.Draw(img)
    d.rectangle((0,0,1600,58),fill="#FFFFFF")
    txt(d,(28,18),"FPE Next",25,"#0B2545",True)
    txt(d,(1130,18),"PCSX2.exe  ·  适配器健康",18,"#2C6E49",True)
    d.rectangle((0,58,225,930),fill="#F9FBFD")
    for i,item in enumerate(items):
        y=92+i*62
        if i==2: rect(d,(18,y-10,207,y+37),"#E8F1FB","#B8D3EE",1,8)
        txt(d,(40,y),item,19,"#154E7D" if i==2 else "#435565",i==2)
    txt(d,(262,92),"结果与地址表",30,"#0B2545",True)
    txt(d,(262,136),"保存、编辑、冻结并监控已确认的地址。",18,"#657482")
    rect(d,(260,180,1535,252),"#FFFFFF")
    txt(d,(285,204),"＋ 添加地址",18,"#1769AA",True); txt(d,(455,204),"导入",18,"#435565"); txt(d,(545,204),"导出",18,"#435565")
    txt(d,(1210,204),"全部停止冻结",18,"#9B1C1C",True)
    rect(d,(260,275,1535,750),"#FFFFFF")
    d.rectangle((261,276,1534,330),fill="#EDF2F7")
    headers=[(282,"名称"),(510,"逻辑地址"),(760,"当前值"),(875,"写入值"),(1010,"冻结"),(1130,"间隔"),(1275,"状态"),(1430,"更多")]
    for x,t in headers: txt(d,(x,292),t,16,"#33495C",True)
    rows=[("生命值","GuestPhysical · 0x0012A4C0","100","999","开启","250 ms","已同步"),
          ("金币","GuestPhysical · 0x0034F128","2450","999999","关闭","—","可写"),
          ("关卡计时","GuestVirtual · 0x80201400","32.54","0","条件","16 ms","等待条件"),
          ("旧地址","HostVirtual · 0x00007FF6...","—","—","停止","—","进程已重启")]
    for i,row in enumerate(rows):
        y=365+i*86
        if i==3:d.rectangle((270,y-18,1525,y+52),fill="#FFF2F1")
        xs=[282,510,760,875,1010,1130,1275]
        for x,v in zip(xs,row): txt(d,(x,y),v,16,"#9B1C1C" if i==3 else "#233B4F",x==282)
        txt(d,(1460,y),"⋯",22,"#506171")
    rect(d,(260,780,1535,900),"#FFFFFF")
    txt(d,(285,804),"选中项详情",19,"#20394E",True)
    txt(d,(285,842),"宿主地址  0x00007FF61234A4C0   ·   解析证据  PCSX2 2.2.0 / System RAM / signature verified",16,"#4F6070")
    txt(d,(285,872),"最近写入  成功，读回一致   ·   失败计数 0   ·   回滚可用",16,"#2C6E49")
    d.rectangle((0,930,1600,980),fill="#FFFFFF"); txt(d,(28,946),"冻结 2 条  ·  写入队列 0  ·  全局停止快捷键 Ctrl+Shift+F12",16,"#4E6070")
    path=WORK_DIR/"ui_address_table.png"; img.save(path); paths.append(path)
    return paths


def add_figure(doc, path: Path, caption: str):
    p0 = doc.add_paragraph()
    p0.alignment = WD_ALIGN_PARAGRAPH.CENTER
    run = p0.add_run()
    run.add_picture(str(path), width=Inches(6.35))
    doc_pr = run._r.find(".//" + qn("wp:docPr"))
    if doc_pr is not None:
        doc_pr.set("title", caption)
        doc_pr.set("descr", caption)
    p0.paragraph_format.space_after = Pt(4)
    cp = p(doc, caption, size=9, italic=True, color=base.MUTED,
           align=WD_ALIGN_PARAGRAPH.CENTER, after=8)
    cp.paragraph_format.keep_with_next = False


def build_ui_spec(mockups):
    doc = Document()
    base.configure_styles(doc)
    bullet_id, decimal_ids = base.add_num_definitions(doc)
    set_header_footer(doc, "FPE Next UI/UX 实施规格")
    cover(
        doc,
        "DESKTOP UI / UX IMPLEMENTATION SPECIFICATION",
        "UI/UX 实施规格",
        "Windows 11 桌面端页面、控件、状态、MVVM 与交互验收",
        [
            ("设计对象", "FPE Next 桌面端；键鼠优先；高 DPI；中文优先，预留本地化"),
            ("实现技术", "WPF / .NET 10 / MVVM；不绑定具体第三方控件库"),
            ("实施范围", "P0-P3 核心工作流；图片、速度、宏工具列为后续模块"),
            ("文档版本", "v1.0 · 2026-08-15"),
        ],
        "交付标准：实现模型无需自行猜测导航、状态、字段或地址显示规则。",
    )
    add_contents(doc, [
        "体验目标与设计原则", "信息架构与导航", "视觉令牌与窗口规则", "应用壳与全局组件",
        "目标选择页", "扫描工作区", "结果与地址表", "内存查看器", "适配器中心",
        "诊断与设置", "对话框、通知与错误", "MVVM 映射", "键盘、无障碍与本地化",
        "性能与大数据交互", "响应式/高 DPI", "页面状态矩阵与验收", "P4 旧功能迁移",
    ], decimal_ids[0])

    h(doc, "1. 体验目标与设计原则")
    base.add_callout(doc, "核心体验", "用户始终能回答四个问题：当前附加到谁、操作的是哪种地址、任务进行到哪里、写入是否仍在发生。")
    bullets(doc, [
        "安全可见：写入、冻结、提权、目标重启都必须有持续可见状态，不能只弹一次提示。",
        "地址不混淆：逻辑地址为主，宿主地址为证据；两者使用不同标签并可分别复制。",
        "长任务可控：扫描、导入、诊断均有阶段、进度、取消和可恢复结果。",
        "渐进复杂度：默认只显示常用条件；字节序、对齐、范围、epsilon 放入“高级”。",
        "大数据原生：候选结果和内存网格虚拟化；不把百万行等同普通表格。",
        "离线工具气质：界面克制、信息密度较高、无游戏化装饰和无关动画。",
    ], bullet_id)
    h(doc, "1.1 目标用户", 2)
    base.add_table(doc, ["角色", "主要任务", "设计照顾"], [
        ("普通玩家", "找数值、缩小结果、冻结", "向导式条件、清晰默认值、撤销写入"),
        ("高级用户", "地址表、指针链、字节序、批量操作", "快捷键、密集表格、精确证据"),
        ("适配器开发者", "探测映射、收集诊断、验证版本", "能力矩阵、日志、复制诊断"),
    ], [1800, 3200, 4360], font_size=9.4)

    h(doc, "2. 信息架构与导航")
    base.add_table(doc, ["一级导航", "路由", "P 级", "进入条件"], [
        ("目标", "/targets", "P0", "始终可用"),
        ("扫描", "/scan", "P1", "已附加且具有 Read"),
        ("结果与地址表", "/addresses", "P2", "地址表始终可看；实时值需已附加"),
        ("内存查看器", "/memory", "P2", "已附加且具有 Read"),
        ("适配器", "/adapters", "P3", "始终可用"),
        ("诊断", "/diagnostics", "P3", "始终可用"),
        ("设置", "/settings", "P0", "右上角齿轮；非主导航项"),
    ], [1900, 1900, 900, 4660], font_size=9.3)
    bullets(doc, [
        "主导航固定左侧；宽度 224 px。窗口宽度小于 1100 px 时折叠为 56 px 图标栏。",
        "切换页面不取消扫描；活动任务在底部状态栏持续显示，可点击返回任务页。",
        "目标退出后仍允许查看历史结果与地址表，但所有实时操作禁用并解释原因。",
        "深链保存当前会话内路由，不把宿主地址写入 URL/持久导航状态。",
    ], bullet_id)

    h(doc, "3. 视觉令牌与窗口规则")
    h(doc, "3.1 颜色、字体、间距", 2)
    base.add_table(doc, ["令牌", "值", "用途"], [
        ("Color.Background", "#F5F7FA", "应用背景"),
        ("Color.Surface", "#FFFFFF", "卡片、表格、命令栏"),
        ("Color.Primary", "#1769AA", "主操作、选中导航"),
        ("Color.PrimarySoft", "#E8F1FB", "选中背景"),
        ("Color.Success", "#2C6E49", "已附加、读回一致、适配器健康"),
        ("Color.Warning", "#9A6A00", "部分读取、需版本确认、磁盘预警"),
        ("Color.Danger", "#9B1C1C", "停止冻结、写入失败、身份变化"),
        ("Typography.UI", "Segoe UI Variable / Microsoft YaHei UI", "界面文本；12-14 px"),
        ("Typography.Mono", "Cascadia Mono / Consolas", "地址、字节、数值；12-13 px"),
        ("Spacing", "4 / 8 / 12 / 16 / 24 / 32", "只使用该阶梯"),
        ("Radius", "6 px 控件 / 8 px 卡片", "避免过度圆角"),
    ], [2500, 2600, 4260], font_size=9.2)
    h(doc, "3.2 窗口与密度", 2)
    bullets(doc, [
        "默认窗口 1440×900；最小 1024×700；首次启动居中，之后恢复屏幕内可见的位置和尺寸。",
        "标题栏采用 Windows 11 原生窗口行为；最大化、Snap Layout、高对比度和系统缩放必须可用。",
        "列表行高：紧凑 32 px、标准 40 px；默认标准。工具栏按钮至少 32×32 px。",
        "正文不低于 12 px；重要状态不只靠颜色，必须配文本或图标。",
        "动画 120-180 ms；尊重系统“关闭动画”。扫描进度不用循环炫光。",
    ], bullet_id)

    h(doc, "4. 应用壳与全局组件")
    add_figure(doc, mockups[0], "图 1　应用壳与目标选择页（实现参考，像素值以本规格令牌为准）")
    h(doc, "4.1 布局区域", 2)
    base.add_table(doc, ["区域", "尺寸/位置", "内容"], [
        ("标题栏", "高 56 px，顶部", "产品名；当前目标胶囊；设置、帮助、窗口按钮"),
        ("导航栏", "宽 224 px，左侧", "一级页面；禁用项仍显示并提供原因 tooltip"),
        ("内容区", "其余空间；内边距 24 px", "页面标题、命令栏、内容、局部状态"),
        ("状态栏", "高 48 px，底部", "连接、活动任务、候选数、冻结数、全局停止提示"),
    ], [1800, 2300, 5260], font_size=9.3)
    h(doc, "4.2 全局目标胶囊", 2)
    bullets(doc, [
        "显示：进程名、PID（次要）、架构、适配器名、连接状态。",
        "点击打开会话抽屉：进程路径、启动时间、内存域、能力、解析证据、断开按钮。",
        "未附加时显示“选择目标”；目标退出时红色“已退出”，并提供重新探测。",
        "切换目标前若有冻结规则，必须显示将停止的规则数量并要求确认。",
    ], bullet_id)
    h(doc, "4.3 全局停止", 2)
    base.add_callout(doc, "强制规则", "Ctrl+Shift+F12 与状态栏“全部停止冻结”必须始终可用；触发后立即改变视觉状态，不等待后台确认。")

    h(doc, "5. 目标选择页 /targets")
    h(doc, "5.1 控件清单", 2)
    base.add_table(doc, ["ID", "控件", "绑定", "行为"], [
        ("TGT-01", "搜索框", "Targets.FilterText", "300 ms 防抖；匹配进程名、PID、窗口标题。"),
        ("TGT-02", "刷新按钮", "RefreshTargetsCommand", "扫描中禁用；保留当前筛选。"),
        ("TGT-03", "进程表", "Targets.Items / SelectedTarget", "单选、排序、虚拟化；默认按适配评分降序。"),
        ("TGT-04", "适配器选择", "SelectedAdapter", "默认最高可信项；可改为通用 Win32。"),
        ("TGT-05", "附加按钮", "AttachCommand", "先 Probe；必要时二阶段权限提示。"),
        ("TGT-06", "手动打开", "BrowseExecutableCommand", "只用于启动/定位，不缓存凭据。"),
    ], [1200, 1900, 2600, 3660], font_size=9.1)
    h(doc, "5.2 页面状态", 2)
    base.add_table(doc, ["状态", "页面表现", "可执行操作"], [
        ("Loading", "表格骨架行，刷新旋转图标", "取消刷新"),
        ("Empty", "“未发现可附加进程”+刷新+打开程序", "刷新、打开程序"),
        ("Selected", "右侧/底部显示能力预览", "附加"),
        ("Probing", "行内阶段：识别版本/架构/接口", "取消"),
        ("AccessDenied", "说明只读/写入所需权限", "只读重试、经确认提权"),
        ("Unsupported", "显示版本证据与诊断入口", "通用模式、导出诊断"),
    ], [1600, 4800, 2960], font_size=9.2)

    h(doc, "6. 扫描工作区 /scan")
    add_figure(doc, mockups[1], "图 2　扫描工作区：左侧条件、右侧虚拟化结果、底部持续任务状态")
    h(doc, "6.1 布局与步骤", 2)
    bullets(doc, [
        "左栏宽 360-420 px：内存域、类型、扫描方式、输入值；高级选项可折叠。",
        "右区为候选结果预览；只展示窗口化数据，不显示传统分页器。",
        "首次扫描完成后主按钮改为“再次扫描”；旁边提供“新建扫描”并确认丢弃未保存状态。",
        "扫描历史用水平步骤条显示候选数变化；点击历史只读查看，继续扫描必须从最新快照。",
    ], bullet_id)
    h(doc, "6.2 字段规格", 2)
    base.add_table(doc, ["字段", "控件/格式", "校验与默认值"], [
        ("内存域", "ComboBox", "适配器提供；默认 System RAM，否则 HostVirtual。"),
        ("数值类型", "分组 ComboBox", "默认 Int32；最近使用可置顶。"),
        ("扫描方式", "Segmented/Combo", "首次：精确/未知/范围；再次：精确/增/减/变化/未变/范围。"),
        ("输入值", "NumericTextBox", "按类型解析；接受十进制和 0x 前缀；错误就地显示。"),
        ("浮点容差", "两个 NumericTextBox", "绝对/相对；只在 Float 显示。"),
        ("字节序", "ComboBox", "来自域；用户覆盖时显示警告徽标。"),
        ("地址范围", "两个 AddressTextBox", "逻辑地址；上界≥下界；格式化为域宽度。"),
        ("对齐", "1/2/4/8/自然", "默认 1；自然对齐按值宽。"),
    ], [1900, 2600, 4860], font_size=9.1)
    h(doc, "6.3 任务与错误", 2)
    bullets(doc, [
        "扫描状态栏显示阶段、百分比、已读/总字节、候选数、耗时和取消。总量未知时显示已读字节，不伪造百分比。",
        "取消后保留上个完整结果，页面顶部显示“本次扫描已取消”。",
        "部分读取使用黄色非阻塞提示：“跳过 12 个不可读区域”；可打开详情。",
        "磁盘空间不足在启动前阻止未知值扫描，并给出估算与快照清理入口。",
        "目标退出立即停止任务，错误标题为“目标进程已结束”，不显示原始 Win32 异常堆栈。",
    ], bullet_id)

    h(doc, "7. 结果与地址表 /addresses")
    add_figure(doc, mockups[2], "图 3　地址表与冻结状态：逻辑地址优先，宿主地址和解析证据位于详情区")
    h(doc, "7.1 列定义", 2)
    base.add_table(doc, ["列", "宽度策略", "编辑/显示规则"], [
        ("名称", "180-260 px", "可内联编辑；空值显示“未命名地址”。"),
        ("逻辑地址", "220-300 px", "等宽；域标签+地址；复制完整值。"),
        ("当前值", "100-140 px", "定时刷新；失败显示 — 和 tooltip。"),
        ("写入值", "110-160 px", "类型感知编辑；Enter 提交，Esc 取消。"),
        ("冻结", "90 px", "Off/Always/Conditional；不是单纯复选框。"),
        ("间隔", "90 px", "16 ms-60 s；默认 250 ms。"),
        ("状态", "120-180 px", "已同步/等待条件/失败/目标已退出。"),
        ("更多", "56 px", "编辑、复制、查看内存、回滚、删除。"),
    ], [1600, 1800, 5960], font_size=9.1)
    h(doc, "7.2 添加/编辑地址抽屉", 2)
    bullets(doc, [
        "字段：名称、地址空间、域、地址、值类型、端序、显示格式、注释、刷新间隔。",
        "地址空间为 ModuleRelative/PointerChain 时切换为专用编辑器，不要求用户拼字符串。",
        "保存前执行 Resolve；显示宿主地址和 ResolutionProof 摘要。解析失败时允许“仅保存，不启用实时读取”。",
        "删除有冻结的地址前先停用规则；删除可在当前会话撤销。",
    ], bullet_id)
    h(doc, "7.3 冻结编辑", 2)
    base.add_table(doc, ["条件", "操作数", "含义"], [
        ("始终", "无", "每到间隔检查并写入目标值。"),
        ("当前值等于", "A", "仅 current == A 时写目标值。"),
        ("当前值不等于", "A", "仅 current != A 时写目标值。"),
        ("当前值在范围", "A/B", "含边界。"),
        ("当前值变化", "上次读值", "检测变化后写入。"),
    ], [2200, 2200, 4960], font_size=9.3)
    base.add_callout(doc, "危险操作", "批量写入、启用多条冻结、切换目标和全部停止使用明确动词；不得把“关闭窗口”等同于停止冻结。", fill="FFF2F1", label_color=base.RED)

    h(doc, "8. 内存查看器 /memory")
    h(doc, "8.1 页面结构", 2)
    bullets(doc, [
        "顶部：地址空间、域、地址输入、前往、返回/前进历史、刷新、暂停刷新。",
        "中央：16 字节/行；左侧地址、中间十六进制字节、右侧 ASCII/UTF-8 预览。",
        "右侧检查器：所选字节按 Int/UInt/Float、大小端、UTF-16 显示；可添加到地址表。",
        "编辑模式默认关闭；开启后显示持续黄色边框和“提交 N 个字节/放弃”命令。",
    ], bullet_id)
    h(doc, "8.2 交互规则", 2)
    base.add_table(doc, ["操作", "规则"], [
        ("地址输入", "接受 0x 前缀；自动补齐但不截断；域改变后重新解释。"),
        ("选择", "Shift 扩展，Ctrl 追加；最大编辑选择 4 KiB。"),
        ("读取失败", "不可读字节显示 ??；行可部分成功。"),
        ("写入", "批量提交前展示起址、长度、差异；成功后读回。"),
        ("滚动", "按需读取；快速滚动时取消过时请求。"),
        ("跳转", "从结果/地址表进入时保留来源，可一键返回。"),
    ], [2300, 7060], font_size=9.3)

    h(doc, "9. 适配器中心 /adapters")
    base.add_table(doc, ["区域", "内容"], [
        ("已安装", "名称、版本、发布者、签名状态、协议版本、启用状态。"),
        ("能力", "Read/Write/Domains/Translate/Pause/FrameSync/Diagnostics 矩阵。"),
        ("兼容性", "目标版本、exe SHA、最近验证时间、探测证据。"),
        ("健康", "Host PID、最近心跳、延迟、崩溃次数、重启。"),
        ("开发者模式", "加载未签名适配器需显式开关和每次警告；默认关闭。"),
    ], [2300, 7060], font_size=9.3)
    bullets(doc, [
        "首版不实现在线适配器商店；安装来自本地受控包。",
        "未签名与签名无效必须区分；界面不可用“未知”掩盖验证失败。",
        "适配器不兼容时显示 Evidence，不推荐用户反复以管理员权限重试。",
    ], bullet_id)

    h(doc, "10. 诊断与设置")
    h(doc, "10.1 诊断页", 2)
    bullets(doc, [
        "会话概览：目标身份、架构、访问权限、适配器、内存域、快照和冻结状态。",
        "事件时间线：附加、探测、扫描、写入、断开；可按严重度和 operationId 筛选。",
        "导出诊断：先显示包含项，默认排除内存值和完整路径；生成后显示保存位置与 SHA-256。",
        "协议查看器仅开发者模式可见，默认折叠 Base64/二进制载荷。",
    ], bullet_id)
    h(doc, "10.2 设置分组", 2)
    base.add_table(doc, ["分组", "设置"], [
        ("通用", "语言、主题（系统/浅色/深色）、密度、启动时检查更新（默认关闭可选）。"),
        ("扫描", "块大小、线程数上限、默认对齐、未知值扫描确认阈值。"),
        ("存储", "快照目录、配额、保留数量、立即清理。"),
        ("写入", "默认读回校验、默认冻结间隔、全局停止快捷键。"),
        ("隐私", "日志级别、诊断脱敏、崩溃转储。"),
        ("开发者", "未签名适配器、协议日志、手动校准；显示红色风险带。"),
    ], [2000, 7360], font_size=9.2)

    h(doc, "11. 对话框、通知与错误")
    h(doc, "11.1 使用何种反馈", 2)
    base.add_table(doc, ["类型", "使用场景", "禁止"], [
        ("内联校验", "输入值、地址、范围、间隔", "用消息框报告字段错误"),
        ("Toast", "保存成功、复制成功、非阻塞警告", "关键写入失败只显示 3 秒"),
        ("Banner", "目标退出、部分读取、适配器断开", "每次轮询重复新增"),
        ("Modal", "提权、批量写入、切换目标将停止冻结", "普通导航确认"),
        ("任务面板", "扫描、导入、诊断导出", "无取消、无阶段的无限进度"),
    ], [1500, 4200, 3660], font_size=9.2)
    h(doc, "11.2 错误文案模板", 2)
    code(doc, "标题：无法读取目标内存\n" +
              "说明：目标进程拒绝了读取请求，扫描尚未开始。\n" +
              "建议：先以只读模式重试；仅在你信任该程序时申请管理员权限。\n" +
              "详情：错误代码 AccessDenied（Win32 5） · operationId 可复制\n" +
              "操作：[只读重试] [申请权限] [打开诊断] [取消]")
    bullets(doc, [
        "用户文案不得只显示 HRESULT、Win32 文本或堆栈；技术详情放可展开区。",
        "错误操作按钮不超过三个主选项；默认焦点放在最安全的恢复操作。",
        "同一根因 5 秒内合并计数，避免冻结失败产生通知风暴。",
    ], bullet_id)

    h(doc, "12. MVVM 映射")
    base.add_table(doc, ["View", "ViewModel", "关键 Commands", "关键 Services"], [
        ("ShellView", "ShellViewModel", "Navigate, Detach, GlobalStop", "INavigationService, ISessionService"),
        ("TargetsView", "TargetsViewModel", "Refresh, Probe, Attach", "IProcessCatalog, IAdapterCatalog"),
        ("ScanView", "ScanViewModel", "Start, Refine, Cancel, New", "IScanApplicationService"),
        ("AddressesView", "AddressesViewModel", "Add, Write, Freeze, Rollback", "IAddressBookService, IFreezeService"),
        ("MemoryView", "MemoryViewModel", "GoTo, Refresh, Commit, AddAddress", "IMemoryViewerService"),
        ("AdaptersView", "AdaptersViewModel", "Install, Enable, RestartHost", "IAdapterManager"),
        ("DiagnosticsView", "DiagnosticsViewModel", "Filter, Export", "IDiagnosticsService"),
        ("SettingsView", "SettingsViewModel", "Save, Reset, ClearSnapshots", "ISettingsService"),
    ], [1500, 2100, 3150, 2610], font_size=8.8)
    h(doc, "12.1 ViewModel 约束", 2)
    bullets(doc, [
        "不得把 Process、SafeHandle、DbConnection、NamedPipeClientStream 暴露为可绑定属性。",
        "所有长命令实现取消、CanExecute 和重复点击保护；异常由统一 OperationResult 映射。",
        "集合只持有可视页/窗口；结果源实现 IItemsRangeInfo 或等价增量接口。",
        "导航不通过 code-behind 访问全局单例；窗口服务只处理系统对话框和外壳。",
        "设计时数据与运行时服务分离，线框中的所有状态均应可在 Story/Design fixture 中重现。",
    ], bullet_id)
    h(doc, "12.2 关键可绑定状态", 2)
    code(doc, r"""
public sealed partial class ScanViewModel : ObservableObject
{
    [ObservableProperty] private ScanPhase _phase;
    [ObservableProperty] private ScanCriteriaViewModel _criteria = new();
    [ObservableProperty] private ScanProgressViewModel? _progress;
    [ObservableProperty] private ulong _candidateCount;
    [ObservableProperty] private bool _hasReadableTarget;

    public IAsyncRelayCommand StartOrRefineCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IAsyncRelayCommand NewScanCommand { get; }
}
""")
    p(doc, "CommunityToolkit.Mvvm 可使用但不是强制；如果不用，接口行为与线程切换规则必须等价。UI 集合更新只能在 Dispatcher 上，扫描计算不得在 UI 线程运行。", color=base.MUTED)

    h(doc, "13. 键盘、无障碍与本地化")
    h(doc, "13.1 快捷键", 2)
    base.add_table(doc, ["快捷键", "作用域", "行为"], [
        ("Ctrl+1…6", "全局", "切换六个一级页面"),
        ("Ctrl+P", "全局", "聚焦目标搜索/快速附加"),
        ("Ctrl+Enter", "扫描页", "开始或再次扫描"),
        ("Esc", "活动任务/编辑", "先取消编辑，再询问取消长任务"),
        ("Ctrl+G", "内存页", "聚焦地址输入"),
        ("F2", "地址表", "编辑名称"),
        ("Ctrl+Shift+F12", "全局", "全部停止冻结；不可重绑定为空"),
    ], [2100, 1800, 5460], font_size=9.3)
    h(doc, "13.2 无障碍", 2)
    bullets(doc, [
        "所有图标按钮具有 AutomationProperties.Name；状态图标同时有文本。",
        "键盘焦点顺序与视觉顺序一致；虚拟化行恢复焦点时按稳定 ID，而非行索引。",
        "支持 200% 缩放，无横向裁切关键命令；高对比度下不依赖自定义填充色。",
        "扫描进度变化不每次触发读屏播报；阶段变化或每 10% 里程碑播报一次。",
        "地址按字符组可读标签提供自动化文本，例如“十六进制地址 0000 7FFF …”。",
    ], bullet_id)
    h(doc, "13.3 本地化", 2)
    bullets(doc, [
        "所有用户文本进入 .resx；禁止在 ViewModel 拼接中文句子。",
        "数值解析支持当前区域的小数分隔符，但十六进制始终使用 ASCII 0x。",
        "地址、哈希、协议版本不做本地化；时间显示本地，存储统一 UTC。",
        "列宽以中文和英文两套资源测试；按钮允许自然扩展。",
    ], bullet_id)

    h(doc, "14. 性能与大数据交互")
    base.add_table(doc, ["指标", "目标"], [
        ("冷启动", "常规开发机 < 2.5 s 到可交互外壳；不等待进程枚举完成。"),
        ("目标列表", "1,000 进程项筛选 < 100 ms。"),
        ("结果滚动", "百万候选数据源下维持交互；UI 内存不随总候选线性增长。"),
        ("进度更新", "节流到 5-10 Hz；后台每块进度不得直接触发 UI 重排。"),
        ("当前值刷新", "仅刷新可视地址行；离屏行暂停。"),
        ("诊断日志", "增量加载；默认最近 2,000 条。"),
    ], [2300, 7060], font_size=9.3)
    bullets(doc, [
        "排序/筛选百万结果必须由快照查询层执行，不允许 LINQ 全量 materialize。",
        "快速滚动或切页时取消过时读取，响应结果必须校验 generationId。",
        "UI 线程单帧工作预算 16 ms；超过 50 ms 的操作必须异步并显示任务状态。",
    ], bullet_id)

    h(doc, "15. 响应式与高 DPI")
    base.add_table(doc, ["宽度", "行为"], [
        (">= 1366 px", "完整导航；扫描左右栏；地址详情可停靠底部。"),
        ("1100-1365 px", "完整导航；压缩表格次要列；详情抽屉覆盖。"),
        ("1024-1099 px", "导航折叠；扫描条件成为可收起侧栏；隐藏宿主地址列。"),
        ("< 1024 px", "不支持；窗口限制最小宽度并保持系统可移动。"),
    ], [2300, 7060], font_size=9.3)
    bullets(doc, [
        "使用 WPF Device Independent Pixel；不要按物理像素硬编码绘制内存网格。",
        "每个显示器 DPI 感知；跨屏移动后重新测量但保留逻辑窗口尺寸。",
        "线框图是 1600×980 参考画布，不代表必须固定像素。",
    ], bullet_id)

    h(doc, "16. 页面状态矩阵与验收")
    base.add_table(doc, ["AT-UI", "场景", "通过标准"], [
        ("UI-001", "无目标启动", "只有目标/适配器/诊断/设置可用；禁用项说明原因。"),
        ("UI-002", "附加只读目标", "扫描可用，写入/冻结禁用且无提权骚扰。"),
        ("UI-003", "百万候选", "结果可滚动、取消、复制；UI 内存稳定。"),
        ("UI-004", "扫描取消", "1 秒内界面进入已取消；上个快照仍可用。"),
        ("UI-005", "部分读取", "非阻塞警告有数量与详情；结果仍展示。"),
        ("UI-006", "目标重启", "旧地址显示失效，冻结停止，写入按钮禁用。"),
        ("UI-007", "适配器崩溃", "页面不崩溃；banner、诊断与重启入口可用。"),
        ("UI-008", "200% DPI", "主要命令、输入和错误无裁切，网格仍可用。"),
        ("UI-009", "键盘全流程", "不使用鼠标完成附加→扫描→添加地址→全停。"),
        ("UI-010", "高对比度", "所有状态可识别，焦点清晰。"),
    ], [1300, 2300, 5760], font_size=9.0)
    h(doc, "16.1 页面级完成定义", 2)
    bullets(doc, [
        "具备 Loading、Empty、Ready、Running、Cancelled、Failed、Disconnected 至少七类设计时状态。",
        "所有按钮具有 CanExecute、快捷键、自动化名称、忙碌状态和错误映射。",
        "所有表格定义列宽策略、虚拟化、排序、空值、复制格式和 tooltip。",
        "关键流程有 ViewModel 单元测试和至少一个端到端自动化测试。",
        "实现截图与本规格线框对比，差异记录到 UI-DEVIATIONS.md。",
    ], bullet_id)

    h(doc, "17. P4 旧功能迁移")
    base.add_table(doc, ["旧功能", "新版位置", "状态/规则"], [
        ("Scan", "扫描工作区", "P1-P2，完整迁移并现代化。"),
        ("Address/Lock", "结果与地址表", "P2，增加条件冻结、读回与全停。"),
        ("Editor", "内存查看器", "P2，默认只读，显式编辑。"),
        ("Select", "目标页/导入", "文件浏览器不复刻；使用系统选择器。"),
        ("GPE/SPE 图片", "可选工具模块", "P4；与核心内存引擎解耦。"),
        ("Macro/Hotkey", "自动化模块", "P4；避免全局钩子，优先 RegisterHotKey/显式作用域。"),
        ("Speed", "模拟器能力面板", "P4；只有适配器声明能力时显示。"),
        ("Others", "设置/诊断", "拆分到语义分组，不保留杂项页。"),
    ], [1800, 2300, 5260], font_size=9.1)
    base.add_callout(doc, "实施顺序", "先完成应用壳、目标、扫描和地址表；内存查看器随后；适配器中心与诊断在进程隔离落地时接入。P4 不得阻塞 MVP。", fill="EAF4EE", label_color=base.GREEN)

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    trim_trailing_empty_paragraphs(doc)
    doc.save(UI_OUT)
    return UI_OUT


def main():
    mockups = draw_ui_mockups()
    code_path = build_code_spec()
    ui_path = build_ui_spec(mockups)
    print(code_path)
    print(ui_path)


if __name__ == "__main__":
    main()
