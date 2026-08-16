namespace Fpe2001Remake.Domain;

/// <summary>来源种类 + 64 位偏移的定位点（规格 §3）。</summary>
public readonly record struct BytePosition(ByteSourceKind SourceKind, ulong Offset);
