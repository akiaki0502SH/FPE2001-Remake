namespace Fpe2001Remake.Domain;

/// <summary>地址空间类别（FPE2001-Remake v2.1 规格 §3）。</summary>
public enum AddressSpace
{
    /// <summary>宿主（Windows）虚拟地址。</summary>
    HostVirtual,

    /// <summary>客户机物理地址（模拟器域）。</summary>
    GuestPhysical,

    /// <summary>客户机虚拟地址（模拟器域）。</summary>
    GuestVirtual,

    /// <summary>模块相对地址。</summary>
    ModuleRelative,

    /// <summary>指针链（多级解引用）地址。</summary>
    PointerChain,
}
