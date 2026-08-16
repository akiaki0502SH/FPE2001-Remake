namespace Fpe2001Remake.BinaryEditor.Core;

/// <summary>书签（规格 §6：标签/注释、颜色）。</summary>
public sealed record Bookmark(ulong Offset, string Label, string? ColorKey);
