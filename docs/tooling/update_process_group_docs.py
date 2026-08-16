"""Create the v2.8/v2.5 process-grouping specification documents.

The existing deliverables are the maintained baseline.  This script makes a
copy of each baseline document, updates only the active version metadata, and
appends the implementation/acceptance section for program-name grouping.
"""

from pathlib import Path
from shutil import copy2

from docx import Document


LOCAL_ROOT = Path(__file__).resolve().parents[1]
if (LOCAL_ROOT / "deliverables").exists():
    ROOT = LOCAL_ROOT
    DELIVERABLES = ROOT / "deliverables"
else:
    # When copied into FPE2001-Remake/docs/tooling, operate on the project
    # specification directory instead of relying on a developer's OneDrive.
    ROOT = Path(__file__).resolve().parents[2]
    DELIVERABLES = ROOT / "docs" / "specifications"


def iter_all_paragraphs(document: Document):
    paragraphs = list(document.paragraphs)
    for table in document.tables:
        for row in table.rows:
            for cell in row.cells:
                paragraphs.extend(cell.paragraphs)
    for section in document.sections:
        paragraphs.extend(section.header.paragraphs)
        paragraphs.extend(section.footer.paragraphs)
        for table in section.header.tables:
            for row in table.rows:
                for cell in row.cells:
                    paragraphs.extend(cell.paragraphs)
        for table in section.footer.tables:
            for row in table.rows:
                for cell in row.cells:
                    paragraphs.extend(cell.paragraphs)
    return paragraphs


def replace_in_paragraph(paragraph, old: str, new: str) -> None:
    for run in paragraph.runs:
        if old in run.text:
            run.text = run.text.replace(old, new)


def update_active_version(document: Document, old: str, new: str) -> None:
    # The cover title and executive-summary line carry the active version.
    for index in (2, 5):
        if index < len(document.paragraphs):
            replace_in_paragraph(document.paragraphs[index], old, new)
    # Keep any header/footer version labels in sync.
    for paragraph in iter_all_paragraphs(document):
        replace_in_paragraph(paragraph, old, new)


def append_section(document: Document, title: str, summary: str, sections: list[tuple[str, list[str]]]) -> None:
    document.add_page_break()
    document.add_paragraph(title, style="Heading 1")
    document.add_paragraph(summary)
    for heading, bullets in sections:
        document.add_paragraph(heading, style="Heading 2")
        for bullet in bullets:
            document.add_paragraph(bullet, style="List Bullet")


def open_versioned_document(source: Path, target: Path) -> Document:
    """Use the previous version when available, otherwise edit the latest output in place."""
    if source.exists() and source.resolve() != target.resolve():
        copy2(source, target)
    return Document(target)


def append_if_missing(document: Document, title: str, summary: str, sections: list[tuple[str, list[str]]]) -> None:
    if not any(title in paragraph.text for paragraph in document.paragraphs):
        append_section(document, title, summary, sections)


def remove_paragraph_text(document: Document, text: str) -> None:
    for paragraph in list(iter_all_paragraphs(document)):
        if paragraph.text.strip() == text:
            paragraph._element.getparent().remove(paragraph._element)


def create_uiux() -> None:
    source = DELIVERABLES / "FPE2001-Remake_UIUX实施规格_v2.7.docx"
    target = DELIVERABLES / "FPE2001-Remake_UIUX实施规格_v2.8.docx"
    document = open_versioned_document(source, target)
    update_active_version(document, "v2.7", "v2.8")
    append_if_missing(
        document,
        "v2.8 进程列表按程序名聚合",
        "当同一程序启动多个实例时，扫描页主列表只保留一行程序名；实例 PID 仍保留并在需要时通过紧凑选择器指定，既减少列表噪声，又不牺牲扫描目标的精确性。",
        [
            (
                "主列表展示",
                [
                    "按 ProcessName（不区分大小写）聚合候选进程，ComboBox 每个程序只显示一行纯程序名，不再在主列表中拼接 PID。",
                    "聚合仅属于 UI 展示层；应用服务仍返回每个进程的 ProcessId/ProcessName，Windows 系统进程过滤规则不改变。",
                    "扫描条件旁的状态说明同时显示“目标程序数”和“进程总数”，让用户知道列表压缩比例和当前过滤范围。",
                ],
            ),
            (
                "多实例选择",
                [
                    "选择包含多个 PID 的程序后，在“目标进程”下拉框右侧显示“实例”PID 选择器；单实例程序不显示该控件。",
                    "刷新列表时尽量恢复原程序和原 PID；若原 PID 已退出，自动回退到该程序当前最小 PID，并在标题栏/状态栏显示最终绑定 PID。",
                    "首次扫描、再次扫描、候选加入地址表均使用当前 SelectedProcessId；程序聚合不改变内存读取、地址空间或模拟器适配器语义。",
                ],
            ),
            (
                "验收标准",
                [
                    "打开目标进程下拉框，列表中同名程序只出现一次，视觉文本不包含“PID”或内部对象信息。",
                    "选择存在多个实例的程序时可看到 PID 选择器，切换 PID 后标题栏、状态栏和扫描目标同步更新。",
                    "刷新或进程退出后不出现失效 PID；空列表、单实例和多实例状态均无布局跳动或遮挡。",
                ],
            ),
        ],
    )
    document.core_properties.title = "FPE2001-Remake UI/UX 实施规格 v2.8"
    document.core_properties.subject = "按程序名聚合进程列表与多实例 PID 选择规范"
    document.save(target)


def create_code_spec() -> None:
    source = DELIVERABLES / "FPE2001-Remake_代码实施规格包_v2.4.docx"
    target = DELIVERABLES / "FPE2001-Remake_代码实施规格包_v2.5.docx"
    document = open_versioned_document(source, target)
    update_active_version(document, "v2.4", "v2.5")
    append_if_missing(
        document,
        "v2.5 进程目标按程序名聚合实施规格",
        "本版本把进程去重限制在 UI ViewModel 与绑定层，保留应用服务的逐进程契约，并新增多实例 PID 选择，确保扫描调用始终获得确定的进程身份。",
        [
            (
                "应用服务契约",
                [
                    "IScanApplicationService.ListTargetsAsync 继续返回 IReadOnlyList<(int ProcessId, string ProcessName)>，不改变 ProcessTargetFilter 的 Windows 系统进程过滤。",
                    "ScanViewModel.RefreshTargetsAsync 使用 StringComparer.OrdinalIgnoreCase 按 ProcessName 分组，组内 PID 去重并按升序保存。",
                    "状态文字使用“目标程序数/进程总数”双计数；刷新后恢复原程序和仍存活的 PID。",
                ],
            ),
            (
                "ViewModel 与绑定",
                [
                    "TargetItem 改为 (ProcessName, IReadOnlyList<int> ProcessIds)，Display 仅返回 ProcessName，并提供 InstanceCount/HasMultipleInstances/Instances。",
                    "ScanViewModel 新增 SelectedProcessId；TargetChanged 事件携带 (TargetItem, int processId)，所有扫描和地址表写入改用该 PID。",
                    "ScanPage.xaml 在多实例时显示实例 ComboBox；目标程序 ComboBox 通过 DisplayMemberPath=Display 只呈现程序名，并将 ComboBoxItem 的 AutomationProperties.Name 绑定到 Display。",
                    "MainViewModel 标题栏与状态栏显示程序名和最终 PID，多实例时补充实例数量，不改变原模块布局和快捷键。",
                ],
            ),
            (
                "测试与验收",
                [
                    "Release 编译必须 0 警告/0 错误；现有单元测试与集成测试保持全量通过。",
                    "UI Automation 检查目标 ComboBox 为程序名去重结果，并验证多实例选择后实例 ComboBox 出现。",
                ],
            ),
        ],
    )
    document.core_properties.title = "FPE2001-Remake 代码实施规格包 v2.5"
    document.core_properties.subject = "按程序名聚合进程列表的 ViewModel 与绑定契约"
    remove_paragraph_text(document, "冒烟覆盖单/多实例、刷新退出与空列表，禁止扫描使用 0 或失效 PID。")
    for paragraph in iter_all_paragraphs(document):
        replace_in_paragraph(
            paragraph,
            "ScanPage.xaml 在多实例时显示实例 ComboBox；目标程序 ComboBox 通过 DisplayMemberPath=Display 只呈现程序名，并提供说明性 ToolTip。",
            "ScanPage.xaml 在多实例时显示实例 ComboBox；目标程序 ComboBox 通过 DisplayMemberPath=Display 只呈现程序名，并将 ComboBoxItem 的 AutomationProperties.Name 绑定到 Display。",
        )
    document.save(target)


def create_architecture() -> None:
    source = DELIVERABLES / "FPE2001-Remake_Win11重构分析与架构设计_v2.4.docx"
    target = DELIVERABLES / "FPE2001-Remake_Win11重构分析与架构设计_v2.5.docx"
    document = open_versioned_document(source, target)
    update_active_version(document, "v2.4", "v2.5")
    append_if_missing(
        document,
        "v2.5 进程身份与展示聚合边界",
        "进程枚举、目标身份和 UI 展示聚合继续分层：程序名用于降低展示复杂度，PID 仍是扫描与内存访问的不可替代身份。",
        [
            (
                "分层决策",
                [
                    "Application 层负责枚举和过滤逐进程候选；UI 层负责按程序名聚合，不把展示模型反向写入扫描引擎或内存适配器。",
                    "TargetItem 保存程序名到 PID 集合的映射；SelectedProcessId 是进入 ScanEngine、AddressBook 和冻结服务的唯一实例选择结果。",
                    "聚合不改变 HostVirtual 地址空间、进程句柄权限、模拟器适配器路由或 64 位地址表示，兼容 Win11 大内存地址场景。",
                ],
            ),
            (
                "生命周期与一致性",
                [
                    "刷新属于一次新的进程快照；优先恢复原程序和仍存在的 PID，PID 失效时回退到同程序的稳定排序实例。",
                    "标题栏/状态栏只反映已选择的程序与 PID，避免用户看到程序名却无法确认实际扫描实例。",
                    "多实例选择器只在必要时出现，单实例保持原有简洁布局，空列表和系统进程隐藏规则沿用既有行为。",
                ],
            ),
            (
                "兼容性与风险控制",
                [
                    "同名进程大小写差异按不区分大小写合并；PID 在每组内去重，避免异常枚举造成重复实例。",
                    "若程序名为空或进程在刷新后退出，UI 不把无效条目传给扫描引擎；底层仍需对句柄打开失败做既有错误处理。",
                    "该改造只影响目标选择器的呈现和选择，不改变文件十六进制编辑、模拟器适配器、地址表和宏模块的操作逻辑。",
                ],
            ),
        ],
    )
    document.core_properties.title = "FPE2001-Remake Win11 重构分析与架构设计 v2.5"
    document.core_properties.subject = "进程身份保留与程序名聚合的分层边界"
    document.save(target)


if __name__ == "__main__":
    create_uiux()
    create_code_spec()
    create_architecture()
    print("created v2.8 UI/UX, v2.5 code specification, and v2.5 architecture documents")
