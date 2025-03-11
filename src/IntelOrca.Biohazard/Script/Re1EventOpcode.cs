namespace IntelOrca.Biohazard.Script
{
    internal enum Re1EventOpcode : byte
    {
        // Operations
        Nop,
        Unk01,
        Unk02,
        Unk03,
        WorkSet,
        Fork,
        Block,
        Single,
        Unk08,
        Disable,

        // Mutations
        Unk81 = 0x81,
        Unk85 = 0x85,
        Unk86 = 0x86,

        // Control
        UnkF6 = 0xF6,
        UnkF7 = 0xF7,
        Sleep = 0xF8,
        UnkF9 = 0xF9,
        For = 0xFA,
        EndFor = 0xFB,
        Do = 0xFC,
        EndDo = 0xFD,
        Next = 0xFE,
        Finish = 0xFF,
    }
}
