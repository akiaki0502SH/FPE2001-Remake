from pathlib import Path
from shutil import copy2

from docx import Document


ROOT = Path(r"C:\Users\猪\OneDrive\Documents\ChatGPT\FPE2001")
DELIVERABLES = ROOT / "deliverables"


def iter_paragraphs(document: Document):
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


def replace_runs(paragraphs, old: str, new: str) -> None:
    for paragraph in paragraphs:
        for run in paragraph.runs:
            if old in run.text:
                run.text = run.text.replace(old, new)


def replace_current_title(document: Document, old: str, new: str) -> None:
    # The first title and executive-summary paragraphs carry the active version.
    for index in (2, 5):
        if index < len(document.paragraphs):
            replace_runs([document.paragraphs[index]], old, new)
    # Keep headers/footers aligned with the active document version.
    replace_runs(iter_paragraphs(document), old, new)


def append_section(document: Document, title: str, summary: str, sections: list[tuple[str, list[str]]]) -> None:
    document.add_page_break()
    document.add_paragraph(title, style="Heading 1")
    document.add_paragraph(summary)
    for heading, bullets in sections:
        document.add_paragraph(heading, style="Heading 2")
        for bullet in bullets:
            document.add_paragraph(bullet, style="List Bullet")


def create_uiux() -> None:
    source = DELIVERABLES / "FPE2001-Remake_UIUX实施规格_v2.5.docx"
    target = DELIVERABLES / "FPE2001-Remake_UIUX实施规格_v2.6.docx"
    copy2(source, target)
    document = Document(target)
    replace_current_title(document, "v2.5", "v2.6")
    append_section(
        document,
        "v2.6 窗口级边界与透明阴影",
        "本版本在 Deep Boundary Blue 视觉层之上增加窗口级边界，保持九模块横向布局、页面信息架构、命令绑定、快捷键和窗口控制逻辑不变。",
        [
            (
                "视觉规格",
                [
                    "整个程序界面由 WindowFrame 统一包裹，使用 1 DIP（Win11 100% 缩放下约 1px）的 WindowFrameBrush 边框。",
                    "边框外保留 2 DIP 空间，使用 WindowFrameShadow 透明阴影：BlurRadius 4、ShadowDepth 0、Opacity 0.18、颜色 #233447，形成均匀低对比外沿。",
                    "窗口内容 Margin=2、边框 CornerRadius 沿用 WindowRadius=10；内容裁剪在边框内，避免标题栏、模块带和状态栏越界。",
                ],
            ),
            (
                "交互与兼容",
                [
                    "窗口按钮仍固定在标题栏最右端，WindowChrome 命中区、最小化、最大化/还原和关闭命令不改变。",
                    "边框与阴影只属于视觉壳层，不覆盖控件命中区域，不改变内容区尺寸语义和九模块操作逻辑。",
                    "UseLayoutRounding、SnapsToDevicePixels 和 PerMonitorV2 DPI 继续启用，125%/150%/200% 缩放下不得出现半像素毛边或文字裁切。",
                ],
            ),
            (
                "验收标准",
                [
                    "运行窗口四边均可看到连续 1px 边框，四角与 WindowChrome 圆角一致。",
                    "边框外侧出现约 2 DIP 的低透明度阴影，阴影不遮挡窗口按钮、模块标签或状态栏文字。",
                    "最小化、最大化/还原、关闭、拖拽标题栏和调整窗口大小均保持原行为。",
                ],
            ),
        ],
    )
    document.core_properties.title = "FPE2001-Remake UI/UX 实施规格 v2.6"
    document.core_properties.subject = "窗口级 1px 边框与 2px 透明阴影规范"
    document.save(target)


def create_code_spec() -> None:
    source = DELIVERABLES / "FPE2001-Remake_代码实施规格包_v2.2.docx"
    target = DELIVERABLES / "FPE2001-Remake_代码实施规格包_v2.3.docx"
    copy2(source, target)
    document = Document(target)
    replace_current_title(document, "v2.2", "v2.3")
    append_section(
        document,
        "v2.3 窗口级边框与阴影实施规格",
        "窗口壳层在不改变既有页面结构和输入命中的前提下，为全部 UI 内容增加统一 1px 边框及外侧 2px 透明阴影。",
        [
            (
                "XAML 结构",
                [
                    "MainWindow 的唯一根内容改为 Border(WindowFrame, Margin=2, BorderThickness=1)，其子元素仍为原有四行 Grid。",
                    "WindowFrame 使用 WindowFrameBrush、WindowRadius、ClipToBounds=True 和 SnapsToDevicePixels=True；原标题栏、模块带、ContentControl、状态栏保持原顺序。",
                    "WindowChrome CaptionHeight、ResizeBorderThickness、CornerRadius 和自定义窗口按钮事件不改动。",
                ],
            ),
            (
                "主题资源",
                [
                    "Colors.xaml 新增 WindowFrameBrush=#75869A。",
                    "新增 WindowFrameShadow：DropShadowEffect BlurRadius=4、ShadowDepth=0、Opacity=0.18、Color=#233447；其可见外沿约为 2 DIP。",
                    "WindowFrameShadow 只挂在窗口级 Border，不复用 CardShadow/ButtonShadow，避免按钮或卡片阴影叠加扩大。",
                ],
            ),
            (
                "测试与验收",
                [
                    "Release 构建必须通过且无 XAML 编译警告；启动冒烟测试确认窗口可创建。",
                    "UI Automation 验证最小化、最大化/还原、关闭按钮仍可定位；窗口大小变化后四边边框仍存在。",
                    "在 100%/125%/150%/200% DPI 下检查边框连续、阴影不裁切、文字与窗口按钮不被遮挡。",
                ],
            ),
        ],
    )
    document.core_properties.title = "FPE2001-Remake 代码实施规格包 v2.3"
    document.core_properties.subject = "窗口级边框与透明阴影实施契约"
    document.save(target)


def create_architecture() -> None:
    source = DELIVERABLES / "FPE2001-Remake_Win11重构分析与架构设计_v2.2.docx"
    target = DELIVERABLES / "FPE2001-Remake_Win11重构分析与架构设计_v2.3.docx"
    copy2(source, target)
    document = Document(target)
    replace_current_title(document, "v2.2", "v2.3")
    append_section(
        document,
        "v2.3 WindowFrame 壳层边界",
        "窗口级边框和阴影属于 UI Shell 的装饰性边界，不进入领域层、扫描层、内存访问层或页面业务状态。",
        [
            (
                "分层决策",
                [
                    "WindowFrame、WindowFrameBrush 和 WindowFrameShadow 只位于 Fpe2001Remake.UI 的 Shell/Theme 资源中。",
                    "边框外 Margin=2 为空间分配，阴影不参与布局测量，不改变页面 ViewModel 或命令契约。",
                    "WindowChrome 继续负责非客户区拖拽与调整大小；自定义按钮继续调用 SystemCommands。",
                ],
            ),
            (
                "Win11 兼容性",
                [
                    "1 DIP 边框与 SnapsToDevicePixels/UseLayoutRounding 组合，避免高 DPI 下出现不连续线条。",
                    "DropShadowEffect 使用低透明度和零偏移，边框外约 2 DIP 的阴影在浅色背景上提供层次，但不形成高对比光晕。",
                    "圆角、阴影和边框均由 WindowFrame 统一管理，避免页面局部控件在 DPI 或窗口调整大小时出现错位。",
                ],
            ),
        ],
    )
    document.core_properties.title = "FPE2001-Remake Win11 重构分析与架构设计 v2.3"
    document.core_properties.subject = "WindowFrame 边界层与 Win11 DPI 兼容性"
    document.save(target)


if __name__ == "__main__":
    create_uiux()
    create_code_spec()
    create_architecture()
    print("created v2.6 UI/UX, v2.3 code specification, and v2.3 architecture documents")
