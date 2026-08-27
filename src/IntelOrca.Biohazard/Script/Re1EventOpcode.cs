namespace IntelOrca.Biohazard.Script
{
    // Top-level ops dispatch in scd_event_loop @0x42E190; 0x80-0x8B are the
    // state-1 "actor command" stream ops executed by event_state_1_logic.
    internal enum Re1EventOpcode : byte
    {
        // Operations
        Nop,
        ActorBegin,
        TweenBegin,
        TweenBeginAlt,
        WorkSet,
        Fork,
        Block,
        Single,
        Chain,
        Disable,

        // Actor commands (state-1 stream)
        ActorReset = 0x80,
        ActorMotion = 0x81,
        ActorMotionBitClear = 0x82,
        ActorAnimSeq = 0x83,
        ActorAnimFlags = 0x84,
        ActorAnimSet = 0x85,
        ActorIdle = 0x86,
        ActorFlagOp = 0x87,
        ActorParamSet = 0x88,
        ActorActionA = 0x89,
        ActorActionB = 0x8A,
        ActorEnd = 0x8B,

        // Control
        PushCond = 0xF6,
        SkipIf = 0xF7,
        Sleep = 0xF8,
        SleepTick = 0xF9,
        For = 0xFA,
        EndFor = 0xFB,
        Do = 0xFC,
        EndDo = 0xFD,
        Next = 0xFE,
        Finish = 0xFF,
    }
}
