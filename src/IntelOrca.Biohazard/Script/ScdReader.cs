using System;
using System.Collections.Generic;
using System.IO;
using IntelOrca.Biohazard.Room;

namespace IntelOrca.Biohazard.Script
{
    public class ScdReader
    {
        public int BaseOffset { get; set; }

        public string Diassemble(ScdProcedureList scd, BioVersion version, BioScriptKind kind, bool listing = false)
        {
            var decompiler = new ScriptDecompiler(true, listing);
            decompiler.VisitVersion(version);
            ReadScript(scd.Data, version, kind, decompiler);
            return decompiler.GetScript();
        }

        public string Diassemble(ScdProcedure scd, BioScriptKind kind, bool listing = false)
        {
            var decompiler = new ScriptDecompiler(true, listing);
            decompiler.VisitVersion(scd.Version);
            ReadScript(scd.Data, scd.Version, kind, decompiler);
            return decompiler.GetScript();
        }

        internal void ReadScript(ReadOnlyMemory<byte> data, BioVersion version, BioScriptKind kind, IBioScriptVisitor visitor)
        {
            ReadScript(new SpanStream(data), data.Length, version, kind, visitor);
        }

        internal void ReadScript(Stream stream, int length, BioVersion version, BioScriptKind kind, IBioScriptVisitor visitor)
        {
            var br = new BinaryReader(stream);
            switch (version)
            {
                case BioVersion.Biohazard1:
                    ReadScript1(br, length, kind, visitor, 0);
                    break;
                case BioVersion.Biohazard2:
                    ReadScript2(br, length, kind, visitor);
                    break;
                case BioVersion.Biohazard3:
                    ReadScript3(br, length, kind, visitor);
                    break;
                case BioVersion.BiohazardCv:
                    ReadScriptCv(br, length, kind, visitor);
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        internal void ReadEventScript(ReadOnlyMemory<byte> data, IBioScriptVisitor visitor, int eventIndex)
        {
            var br = new BinaryReader(new SpanStream(data));
            SetScriptBounds(visitor, BaseOffset, data.Length);
            ReadScript1Event(br, data.Length, BioScriptKind.Event, visitor, eventIndex);
        }

        private static void SetScriptBounds(IBioScriptVisitor visitor, int start, int length)
        {
            if (visitor is ScriptDecompiler decompiler)
            {
                decompiler.ScriptStart = start;
                decompiler.ScriptEnd = start + length;
            }
        }

        private void ReadScript1(BinaryReader br, int length, BioScriptKind kind, IBioScriptVisitor visitor, int eventIndex)
        {
            var scriptEnd = kind == BioScriptKind.Event ? length : br.ReadUInt16();
            var constantTable = new Bio1ConstantTable();
            SetScriptBounds(visitor, BaseOffset, length);

            visitor.VisitBeginScript(kind);
            visitor.VisitBeginSubroutine(eventIndex);
            try
            {
                while (br.BaseStream.Position < scriptEnd)
                {
                    var instructionPosition = (int)br.BaseStream.Position;
                    var opcode = br.ReadByte();
                    var instructionSize = constantTable.GetInstructionSize(opcode, br);
                    if (instructionSize == 0)
                        break;

                    br.BaseStream.Position = instructionPosition + 1;
                    var bytes = new byte[instructionSize];
                    bytes[0] = opcode;
                    if (br.Read(bytes, 1, instructionSize - 1) != instructionSize - 1)
                        break;

                    visitor.VisitOpcode(BaseOffset + instructionPosition, new Span<byte>(bytes));
                }
            }
            catch (Exception)
            {
            }
            visitor.VisitEndSubroutine(0);
            visitor.VisitEndScript(kind);
        }

        private void ReadScript1Event(BinaryReader br, int length, BioScriptKind kind, IBioScriptVisitor visitor, int eventIndex)
        {
            var scriptEnd = kind == BioScriptKind.Event ? length : br.ReadUInt16();
            var constantTable = new Bio1ConstantTable();

            visitor.VisitBeginScript(kind);
            visitor.VisitBeginSubroutine(eventIndex);

            // Task state tracking: ops 01/02/03 switch the byte stream to the
            // actor-cmd/tween sub-ISAs until the exit opcode, exactly like
            // scd_event_loop does. Control ops F6-FF keep their event-level
            // encoding in every state.
            Bio1ConstantTable.Re1SubIsa? subIsa = null;
            while (br.BaseStream.Position < scriptEnd)
            {
                var instructionPosition = (int)br.BaseStream.Position;
                var eventOpcode = (Re1EventOpcode)br.ReadByte();

                if (subIsa != null)
                {
                    if ((byte)eventOpcode < 0xF6)
                    {
                        // NB: exit opcodes collide with event-level markers:
                        // actor streams end on 8B, or 80 which resets +0x85 and
                        // falls through into the 8B end (both 1 byte); the
                        // tween ISA exits on byte 01, which is ActorBegin at
                        // the top level
                        var exits =
                            (subIsa == Bio1ConstantTable.Re1SubIsa.Actor && ((byte)eventOpcode == 0x8B || (byte)eventOpcode == 0x80)) ||
                            (subIsa == Bio1ConstantTable.Re1SubIsa.Tween && (byte)eventOpcode == 0x01);
                        if (!exits)
                        {
                            var size = constantTable.GetSubInstructionSize((byte)eventOpcode, br, subIsa.Value);
                            if (size <= 0 || instructionPosition + size > scriptEnd)
                            {
                                // unknown sub-ISA opcode: the VM's data-driven
                                // control flow (do/skip_if) can jump over the
                                // stream's end marker, so resume top-level
                                // parsing from the same byte
                                subIsa = null;
                                br.BaseStream.Position = instructionPosition + 1;
                                goto topLevel;
                            }
                            br.BaseStream.Position--;
                            ReadSingleEventOpcode(br.BaseStream, visitor, size);
                            continue;
                        }
                        // exit opcode: 1 byte, back to the top level
                        br.BaseStream.Position--;
                        ReadSingleEventOpcode(br.BaseStream, visitor, 1);
                        subIsa = null;
                        continue;
                    }

                    // FF terminates a task in any state (1 byte, unlike the
                    // 2-byte top-level evt_finish form); control ops F6-FE
                    // share the top-level encodings
                    if ((byte)eventOpcode == 0xFF)
                    {
                        br.BaseStream.Position--;
                        ReadSingleEventOpcode(br.BaseStream, visitor, 1);
                        subIsa = null;
                        continue;
                    }
                    if (eventOpcode == Re1EventOpcode.Do)
                    {
                        if (!ReadRe1DoPayload(br, scriptEnd, constantTable, visitor, instructionPosition))
                            goto trailing;
                        continue;
                    }
                    var ctrlSize = constantTable.GetInstructionSize((byte)eventOpcode, br, isEventOpcode: true);
                    if (ctrlSize <= 0 || instructionPosition + ctrlSize > scriptEnd)
                        goto trailing;
                    br.BaseStream.Position--;
                    ReadSingleEventOpcode(br.BaseStream, visitor, ctrlSize);
                    continue;
                }

            topLevel:
                switch (eventOpcode)
                {
                    default:
                    {
                        var eventOpcodeLength = constantTable.GetInstructionSize((byte)eventOpcode, br, isEventOpcode: true);
                        if (eventOpcodeLength <= 0 || instructionPosition + eventOpcodeLength > scriptEnd)
                            goto trailing;
                        br.BaseStream.Position--;
                        ReadSingleEventOpcode(br.BaseStream, visitor, eventOpcodeLength);
                        break;
                    }
                    case Re1EventOpcode.ActorBegin:
                    case Re1EventOpcode.TweenBegin:
                    case Re1EventOpcode.TweenBeginAlt:
                    {
                        br.BaseStream.Position--;
                        ReadSingleEventOpcode(br.BaseStream, visitor, 1);
                        subIsa = eventOpcode == Re1EventOpcode.ActorBegin ?
                            Bio1ConstantTable.Re1SubIsa.Actor :
                            Bio1ConstantTable.Re1SubIsa.Tween;
                        break;
                    }
                    case Re1EventOpcode.Block:
                    {
                        if (instructionPosition + 2 > scriptEnd)
                            goto trailing;
                        var len = br.ReadByte();
                        br.BaseStream.Position -= 2;
                        visitor.VisitBeginEventOpcode((int)(BaseOffset + br.BaseStream.Position), br.ReadBytes(2));
                        if (len > 1)
                        {
                            var take = Math.Min(len - 1, scriptEnd - (int)br.BaseStream.Position);
                            var baseOffset = (int)(BaseOffset + br.BaseStream.Position);
                            var blockBytes = br.ReadBytes(take);
                            var blockStream = new SpanStream(blockBytes);
                            ReadRe1OpcodeBlock(blockStream, constantTable, visitor, baseOffset);
                            EmitTrailing(visitor, blockStream, baseOffset, blockBytes);
                        }
                        visitor.VisitEndEventOpcode();
                        break;
                    }
                    case Re1EventOpcode.Single:
                    {
                        if (instructionPosition + 2 > scriptEnd)
                            goto trailing;
                        var opcodeLen = br.ReadByte();
                        br.BaseStream.Position -= 2;
                        visitor.VisitBeginEventOpcode((int)(BaseOffset + br.BaseStream.Position), br.ReadBytes(2));
                        if (opcodeLen >= 2)
                        {
                            var take = Math.Min(opcodeLen - 2, scriptEnd - (int)br.BaseStream.Position);
                            var baseOffset = (int)(BaseOffset + br.BaseStream.Position);
                            var opcodeBytes = br.ReadBytes(take);
                            var blockStream = new SpanStream(opcodeBytes);
                            ReadRe1Opcode(blockStream, constantTable, visitor, baseOffset);
                            EmitTrailing(visitor, blockStream, baseOffset, opcodeBytes);
                        }
                        visitor.VisitEndEventOpcode();
                        break;
                    }
                    case Re1EventOpcode.Disable:
                        br.BaseStream.Position--;
                        visitor.VisitBeginEventOpcode((int)(BaseOffset + br.BaseStream.Position), br.ReadBytes(2));
                        visitor.VisitEndEventOpcode();
                        break;
                    case Re1EventOpcode.ActorMotion:
                    {
                        if (instructionPosition + 2 > scriptEnd)
                            goto trailing;
                        var param = br.ReadByte();
                        var len = 2 + ((param & 0x0F) != 0 ? 8 : 0);
                        if (instructionPosition + len > scriptEnd)
                            goto trailing;
                        br.BaseStream.Position -= 2;
                        visitor.VisitBeginEventOpcode((int)(BaseOffset + br.BaseStream.Position), br.ReadBytes(len));
                        visitor.VisitEndEventOpcode();
                        break;
                    }
                    case Re1EventOpcode.Do:
                    {
                        if (!ReadRe1DoPayload(br, scriptEnd, constantTable, visitor, instructionPosition))
                            goto trailing;
                        break;
                    }
                }
                continue;
            trailing:
                // declared length overruns the script (or unknown size): preserve the
                // remaining bytes verbatim instead of dropping them
                br.BaseStream.Position = instructionPosition;
                visitor.VisitTrailingData(BaseOffset + instructionPosition, br.ReadBytes(scriptEnd - instructionPosition));
            }
            if (br.BaseStream.Position < scriptEnd)
            {
                visitor.VisitTrailingData(BaseOffset + (int)br.BaseStream.Position, br.ReadBytes(scriptEnd - (int)br.BaseStream.Position));
            }
            visitor.VisitEndSubroutine(0);
            visitor.VisitEndScript(kind);
        }

        private static void EmitTrailing(IBioScriptVisitor visitor, SpanStream stream, int baseOffset, byte[] bytes)
        {
            var consumed = (int)stream.Position;
            if (consumed < bytes.Length)
                visitor.VisitTrailingData(baseOffset + consumed, bytes.AsSpan(consumed));
        }

        // FC do: the length byte is the total instruction span and the payload
        // holds the condition opcode(s) evaluated by FD enddo
        private bool ReadRe1DoPayload(BinaryReader br, int scriptEnd, Bio1ConstantTable constantTable, IBioScriptVisitor visitor, int instructionPosition)
        {
            if (instructionPosition + 2 > scriptEnd)
                return false;
            var opcodeLen = br.ReadByte();
            br.BaseStream.Position -= 2;

            visitor.VisitBeginEventOpcode((int)(BaseOffset + br.BaseStream.Position), br.ReadBytes(2));
            if (opcodeLen >= 2)
            {
                var take = Math.Min(opcodeLen - 2, scriptEnd - (int)br.BaseStream.Position);
                var baseOffset = (int)(BaseOffset + br.BaseStream.Position);
                var opcodes = br.ReadBytes(take);
                var opcodeStream = new SpanStream(opcodes);
                while (ReadRe1Opcode(opcodeStream, constantTable, visitor, baseOffset))
                {
                }
                EmitTrailing(visitor, opcodeStream, baseOffset, opcodes);
            }
            visitor.VisitEndEventOpcode();
            return true;
        }

        private void ReadSingleEventOpcode(Stream stream, IBioScriptVisitor visitor, int length)
        {
            var br = new BinaryReader(stream);
            visitor.VisitBeginEventOpcode((int)(BaseOffset + stream.Position), br.ReadBytes(length));
            visitor.VisitEndEventOpcode();
        }

        private void ReadRe1OpcodeBlock(Stream stream, Bio1ConstantTable constantTable, IBioScriptVisitor visitor, int baseOffset)
        {
            try
            {
                var br = new BinaryReader(stream);
                ushort next;
                do
                {
                    if (br.BaseStream.Position == br.BaseStream.Length - 1)
                        break;
                    next = br.ReadUInt16();
                    br.BaseStream.Position -= 2;
                    visitor.VisitBeginEventOpcode((int)(baseOffset + br.BaseStream.Position), br.ReadBytes(2));
                    if (next != 0)
                    {
                        var baseOffset2 = (int)(baseOffset + br.BaseStream.Position);
                        var opcodes = br.ReadBytes(next - 2);
                        var opcodeStream = new SpanStream(opcodes);
                        while (ReadRe1Opcode(opcodeStream, constantTable, visitor, baseOffset2))
                        {
                        }
                        EmitTrailing(visitor, opcodeStream, baseOffset2, opcodes);
                    }
                    visitor.VisitEndEventOpcode();
                } while (next != 0 && (br.BaseStream.Position < stream.Length));
            }
            catch
            {
            }
        }

        private bool ReadRe1Opcode(Stream stream, Bio1ConstantTable constantTable, IBioScriptVisitor visitor, int baseOffset)
        {
            var br = new BinaryReader(stream);
            var instructionPosition = (int)br.BaseStream.Position;
            if (instructionPosition == br.BaseStream.Length)
                return false;

            var opcode = br.ReadByte();
            var instructionSize = constantTable.GetInstructionSize(opcode, br);
            if (instructionSize == 0)
            {
                br.BaseStream.Position = instructionPosition;
                return false;
            }

            br.BaseStream.Position = instructionPosition + 1;
            var bytes = new byte[instructionSize];
            bytes[0] = opcode;
            if (br.Read(bytes, 1, instructionSize - 1) != instructionSize - 1)
            {
                // not enough bytes left for this instruction: rewind so the caller
                // can emit the remainder as trailing data instead of losing it
                br.BaseStream.Position = instructionPosition;
                return false;
            }

            visitor.VisitOpcode(baseOffset + instructionPosition, new Span<byte>(bytes));
            return true;
        }

        private void ReadScript2(BinaryReader br, int length, BioScriptKind kind, IBioScriptVisitor visitor)
        {
            ReadScript23(br, length, kind, visitor, BioVersion.Biohazard2, new Bio2ConstantTable());
        }

        private void ReadScript3(BinaryReader br, int length, BioScriptKind kind, IBioScriptVisitor visitor)
        {
            ReadScript23(br, length, kind, visitor, BioVersion.Biohazard3, new Bio3ConstantTable());
        }

        private void ReadScript23(
            BinaryReader br,
            int length,
            BioScriptKind kind,
            IBioScriptVisitor visitor,
            BioVersion version,
            IConstantTable constantTable)
        {
            visitor.VisitBeginScript(kind);

            var start = (int)br.BaseStream.Position;
            var functionOffsets = new List<int>();
            var firstFunctionOffset = br.ReadUInt16();
            functionOffsets.Add(start + firstFunctionOffset);
            var numFunctions = firstFunctionOffset / 2;
            for (int i = 1; i < numFunctions; i++)
            {
                functionOffsets.Add(start + br.ReadUInt16());
            }
            functionOffsets.Add(start + length);
            for (int i = 0; i < numFunctions; i++)
            {
                visitor.VisitBeginSubroutine(i);

                var functionOffset = functionOffsets[i];
                var functionEnd = functionOffsets[i + 1];
                var functionEndMin = functionOffset;
                var ifStack = 0;
                var isEnd = false;
                br.BaseStream.Position = functionOffset;
                while (br.BaseStream.Position < functionEnd)
                {
                    var instructionPosition = (int)br.BaseStream.Position;
                    var remainingSize = functionEnd - instructionPosition;
                    if (isEnd)
                    {
                        visitor.VisitTrailingData(BaseOffset + instructionPosition, br.ReadBytes(remainingSize));
                        break;
                    }

                    var opcode = br.ReadByte();
                    var instructionSize = constantTable.GetInstructionSize(opcode, br);
                    if (instructionSize == 0 || instructionSize > remainingSize)
                    {
                        instructionSize = Math.Min(16, remainingSize);
                    }

                    var opcodeBytes = new byte[instructionSize];
                    opcodeBytes[0] = opcode;
                    if (br.Read(opcodeBytes, 1, instructionSize - 1) != instructionSize - 1)
                    {
                        throw new Exception("Unable to read opcode");
                    }

                    visitor.VisitOpcode(BaseOffset + instructionPosition, opcodeBytes);

                    if (i == numFunctions - 1)
                    {
                        if (version == BioVersion.Biohazard2)
                        {
                            switch ((OpcodeV2)opcode)
                            {
                                case OpcodeV2.EvtEnd:
                                    if (instructionPosition >= functionEndMin && ifStack == 0)
                                        isEnd = true;
                                    break;
                                case OpcodeV2.IfelCk:
                                    functionEndMin = instructionPosition + BitConverter.ToUInt16(opcodeBytes, 2);
                                    ifStack++;
                                    break;
                                case OpcodeV2.ElseCk:
                                    ifStack--;
                                    functionEndMin = instructionPosition + BitConverter.ToUInt16(opcodeBytes, 2);
                                    break;
                                case OpcodeV2.EndIf:
                                    ifStack--;
                                    break;
                            }
                        }
                        else
                        {
                            switch ((OpcodeV3)opcode)
                            {
                                case OpcodeV3.EvtEnd:
                                    if (instructionPosition >= functionEndMin && ifStack == 0)
                                        isEnd = true;
                                    break;
                                case OpcodeV3.IfelCk:
                                    functionEndMin = instructionPosition + BitConverter.ToUInt16(opcodeBytes, 2);
                                    ifStack++;
                                    break;
                                case OpcodeV3.ElseCk:
                                    ifStack--;
                                    functionEndMin = instructionPosition + BitConverter.ToUInt16(opcodeBytes, 2);
                                    break;
                                case OpcodeV3.EndIf:
                                    ifStack--;
                                    break;
                            }
                        }
                    }
                }

                visitor.VisitEndSubroutine(i);
            }

            visitor.VisitEndScript(kind);
        }

        private void ReadScriptCv(BinaryReader br, int length, BioScriptKind kind, IBioScriptVisitor visitor)
        {
            var constantTable = new BioCvConstantTable();

            visitor.VisitBeginScript(kind);

            var start = (int)br.BaseStream.Position;
            var functionOffsets = new List<int>();
            var firstFunctionOffset = br.ReadInt32();
            functionOffsets.Add(start + firstFunctionOffset);
            var numFunctions = firstFunctionOffset / 4;
            for (int i = 1; i < numFunctions; i++)
            {
                functionOffsets.Add(start + br.ReadInt32());
            }
            functionOffsets.Add(start + length);
            for (int i = 0; i < numFunctions; i++)
            {
                visitor.VisitBeginSubroutine(i);

                var functionOffset = functionOffsets[i];
                var functionEnd = functionOffsets[i + 1];
                var functionEndMin = functionOffset;
                var isEnd = false;
                br.BaseStream.Position = functionOffset;
                while (br.BaseStream.Position < functionEnd)
                {
                    var instructionPosition = (int)br.BaseStream.Position;
                    var remainingSize = functionEnd - instructionPosition;
                    if (isEnd)
                    {
                        visitor.VisitTrailingData(BaseOffset + instructionPosition, br.ReadBytes(remainingSize));
                        break;
                    }

                    var opcode = br.ReadByte();
                    var instructionSize = constantTable.GetInstructionSize(opcode, br);
                    if (instructionSize == 0 || instructionSize > remainingSize)
                    {
                        instructionSize = Math.Min(16, remainingSize);
                    }

                    var opcodeBytes = new byte[instructionSize];
                    opcodeBytes[0] = opcode;
                    if (br.Read(opcodeBytes, 1, instructionSize - 1) != instructionSize - 1)
                    {
                        throw new Exception("Unable to read opcode");
                    }

                    visitor.VisitOpcode(BaseOffset + instructionPosition, opcodeBytes);

                    // if (i == numFunctions - 1)
                    // {
                    //     switch ((OpcodeV2)opcode)
                    //     {
                    //         case OpcodeV2.EvtEnd:
                    //             if (instructionPosition >= functionEndMin && ifStack == 0)
                    //                 isEnd = true;
                    //             break;
                    //         case OpcodeV2.IfelCk:
                    //             functionEndMin = instructionPosition + BitConverter.ToUInt16(opcodeBytes, 2);
                    //             ifStack++;
                    //             break;
                    //         case OpcodeV2.ElseCk:
                    //             ifStack--;
                    //             functionEndMin = instructionPosition + BitConverter.ToUInt16(opcodeBytes, 2);
                    //             break;
                    //         case OpcodeV2.EndIf:
                    //             ifStack--;
                    //             break;
                    //     }
                    // }
                }

                visitor.VisitEndSubroutine(i);
            }

            visitor.VisitEndScript(kind);
        }

        public int MeasureScript(ReadOnlyMemory<byte> data, BioVersion version, BioScriptKind kind)
        {
            var stream = new SpanStream(data);
            ReadScript(stream, data.Length, version, kind, new NullBioScriptVisitor());
            return (int)stream.Position;
        }
    }
}
