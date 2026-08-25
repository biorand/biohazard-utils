using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace IntelOrca.Biohazard.Script
{
    public class ScriptDecompiler : BioScriptVisitor
    {
        private ScriptBuilder _sb = new ScriptBuilder();
        private Stack<(byte, int)> _blockEnds = new Stack<(byte, int)>();
        private bool _endDoWhile;
        private bool _constructingBinaryExpression;
        private int _expressionCount;
        private IConstantTable _constantTable = new Bio1ConstantTable();
        private BioScriptKind _kind;
        private int _lastReturnLine;
        private Re1EventOpcode? _eventOpcode;
        private bool _eventBlockSub;

        public bool AssemblyFormat => _sb.AssemblyFormat;

        /// <summary>
        /// Byte range of the script currently being disassembled. Label targets
        /// outside this range belong to another script and are emitted as raw
        /// operand values instead of unresolvable label references.
        /// </summary>
        public int ScriptStart { get; set; } = int.MinValue;
        public int ScriptEnd { get; set; } = int.MaxValue;
        private bool InScript(int offset) => offset >= ScriptStart && offset < ScriptEnd;

        public ScriptDecompiler(bool assemblyFormat, bool listingFormat)
        {
            _sb.AssemblyFormat = assemblyFormat;
            _sb.ListingFormat = listingFormat;
        }

        public string GetScript()
        {
            return _sb.ToString();
        }

        public override void VisitVersion(BioVersion version)
        {
            base.VisitVersion(version);

            int versionNumber;
            switch (version)
            {
                case BioVersion.Biohazard1:
                    _constantTable = new Bio1ConstantTable();
                    versionNumber = 1;
                    break;
                case BioVersion.Biohazard2:
                    _constantTable = new Bio2ConstantTable();
                    versionNumber = 2;
                    break;
                case BioVersion.Biohazard3:
                    _constantTable = new Bio3ConstantTable();
                    versionNumber = 3;
                    break;
                case BioVersion.BiohazardCv:
                    _constantTable = new BioCvConstantTable();
                    versionNumber = 4;
                    break;
                default:
                    throw new NotSupportedException();
            }
            if (AssemblyFormat)
                _sb.WriteLine(".version " + versionNumber);
            else
                _sb.WriteLine("#version " + versionNumber);
            _sb.WriteLine();
        }

        public override void VisitBeginScript(BioScriptKind kind)
        {
            switch (kind)
            {
                case BioScriptKind.Init:
                    _kind = BioScriptKind.Init;
                    if (AssemblyFormat)
                    {
                        _sb.WriteLine(".init");
                    }
                    break;
                case BioScriptKind.Main:
                    _kind = BioScriptKind.Main;
                    _sb.WriteLine();
                    if (AssemblyFormat)
                    {
                        _sb.WriteLine(".main");
                    }
                    break;
                case BioScriptKind.Event:
                    _kind = BioScriptKind.Event;
                    _eventOpcode = null;
                    _sb.WriteLine();
                    break;
            }
        }

        public override void VisitEndScript(BioScriptKind kind)
        {
            if (AssemblyFormat)
                return;
        }

        public override void VisitBeginSubroutine(int index)
        {
            if (index != 0)
            {
                _sb.WriteLine();
            }
            if (_kind == BioScriptKind.Event)
            {
                if (AssemblyFormat)
                {
                    _sb.WriteLine($".event {GetProcedureName(index)}");
                }
                else
                {
                    _sb.ResetIndent();
                    _sb.WriteLine($"event {GetProcedureName(index)}");
                    _sb.OpenBlock();

                    _blockEnds.Clear();
                }
            }
            else
            {
                if (AssemblyFormat)
                {
                    if (Version != BioVersion.Biohazard1)
                        _sb.WriteLine($".proc {GetProcedureName(index)}");
                }
                else
                {
                    _sb.ResetIndent();
                    _sb.WriteLine($"proc {GetProcedureName(index)}");
                    _sb.OpenBlock();

                    _blockEnds.Clear();
                }
            }
        }

        private string GetProcedureName(int index)
        {
            var kind = _kind;
            if (Version == BioVersion.Biohazard3)
                kind = BioScriptKind.Main;

            var prefix = kind switch
            {
                BioScriptKind.Init => "init",
                BioScriptKind.Main => "main",
                BioScriptKind.Event => "event",
                _ => "unknown"
            };
            if (index == 0 && kind != BioScriptKind.Event)
                return prefix;
            if (kind == BioScriptKind.Main && index == 1)
                return "aot";
            return $"{prefix}_{index:X2}";
        }

        public override void VisitEndSubroutine(int index)
        {
            if (AssemblyFormat)
                return;

            while (_blockEnds.Count != 0)
            {
                CloseCurrentBlock();
            }

            _sb.CloseBlock();
            _sb.RemoveLine(_lastReturnLine);
        }

        public override void VisitBeginEventOpcode(int offset, ReadOnlySpan<byte> opcodeBytes)
        {
            _sb.RecordOpcode(offset, opcodeBytes);
            if (_eventOpcode == Re1EventOpcode.Block)
            {
                _eventBlockSub = true;
                var len = MemoryMarshal.Cast<byte, ushort>(opcodeBytes)[0];
                _sb.WriteStandardOpcode("evt_block_sub", len);
                return;
            }

            var eventOpcode = (Re1EventOpcode)opcodeBytes[0];
            var bytes = opcodeBytes.ToArray();

            var stream = new SpanStream(bytes);
            var br = new BinaryReader(stream);
            DiassembleGeneralOpcode(br, offset, (byte)eventOpcode, opcodeBytes.Length, isEventOpcode: true);
            _eventOpcode = eventOpcode;
        }

        public override void VisitEndEventOpcode()
        {
            if (_eventBlockSub)
            {
                _eventBlockSub = false;
                return;
            }
            _eventOpcode = null;
        }

        public override void VisitOpcode(int offset, Span<byte> opcodeSpan)
        {
            _sb.RecordOpcode(offset, opcodeSpan);

            var opcode = opcodeSpan[0];
            if (_constructingBinaryExpression)
            {
                if (!_constantTable.IsOpcodeCondition(opcode))
                {
                    _constructingBinaryExpression = false;
                    if (_endDoWhile)
                    {
                        _endDoWhile = false;
                        _sb.WriteLine(");");
                    }
                    else
                    {
                        _sb.WriteLine(")");
                        _sb.OpenBlock();
                    }
                }
            }
            else
            {
                // _sb.WriteLabel(offset);
            }

            while (_blockEnds.Count != 0 && _blockEnds.Peek().Item2 <= offset)
            {
                CloseCurrentBlock();
            }

            var opcodeBytes = opcodeSpan.ToArray();
            var br = new BinaryReader(new MemoryStream(opcodeBytes));
            var backupPosition = br.BaseStream.Position;
            if (!AssemblyFormat)
            {
                switch (Version)
                {
                    case BioVersion.Biohazard1:
                        if (VisitOpcode(offset, (OpcodeV1)opcode, br))
                            return;
                        break;
                    case BioVersion.Biohazard2:
                        if (VisitOpcode(offset, (OpcodeV2)opcode, br))
                            return;
                        break;
                    case BioVersion.Biohazard3:
                        if (VisitOpcode(offset, (OpcodeV3)opcode, br))
                            return;
                        break;
                    case BioVersion.BiohazardCv:
                        if (VisitOpcode(offset, opcode, br))
                            return;
                        break;
                }
                br.BaseStream.Position = backupPosition;
            }
            DiassembleGeneralOpcode(br, offset, opcode, opcodeBytes.Length);
        }

        private void CloseCurrentBlock()
        {
            if (_blockEnds.Count != 0)
            {
                _blockEnds.Pop();
                if (!_endDoWhile)
                {
                    _sb.CloseBlock();
                }
            }
        }

        private bool VisitOpcode(int offset, OpcodeV1 opcode, BinaryReader br)
        {
            var sb = _sb;
            br.ReadByte();
            switch (opcode)
            {
                default:
                    return false;
                case OpcodeV1.Nop:
                    break;
                case OpcodeV1.IfelCk:
                {
                    sb.Write("if (");
                    _constructingBinaryExpression = true;
                    _expressionCount = 0;
                    break;
                }
                case OpcodeV1.ElseCk:
                {
                    var blockLen = br.ReadByte();
                    _sb.CloseBlock();
                    _blockEnds.Push(((byte)opcode, offset + blockLen));
                    sb.WriteLine("else");
                    _sb.OpenBlock();
                    break;
                }
                case OpcodeV1.EndIf:
                    _sb.CloseBlock();
                    break;
                case OpcodeV1.End:
                {
                    br.ReadByte();
                    _lastReturnLine = sb.LineCount;
                    sb.WriteLine($"return;");
                    break;
                }
            }
            return true;
        }

        private bool VisitOpcode(int offset, OpcodeV2 opcode, BinaryReader br)
        {
            var sb = _sb;
            br.ReadByte();
            switch (opcode)
            {
                default:
                    return false;
                case OpcodeV2.Nop:
                case OpcodeV2.Nop20:
                    break;
                case OpcodeV2.EvtEnd:
                {
                    br.ReadByte();
                    _lastReturnLine = sb.LineCount;
                    sb.WriteLine($"evt_end(0);");
                    break;
                }
                case OpcodeV2.EvtExec:
                {
                    var p0 = br.ReadByte();
                    var p1 = br.ReadByte();
                    if (p0 == 0xFF && p1 == 0x18)
                    {
                        var p2 = br.ReadByte();
                        sb.WriteLine($"fork {GetProcedureName(p2)};");
                    }
                    else
                    {
                        return false;
                    }
                    break;
                }
                case OpcodeV2.IfelCk:
                {
                    sb.Write("if (");
                    _constructingBinaryExpression = true;
                    _expressionCount = 0;
                    break;
                }
                case OpcodeV2.ElseCk:
                {
                    br.ReadByte();
                    var blockLen = br.ReadUInt16();
                    _sb.CloseBlock();
                    _blockEnds.Push(((byte)opcode, offset + blockLen));
                    sb.WriteLine("else");
                    _sb.OpenBlock();
                    break;
                }
                case OpcodeV2.EndIf:
                    _sb.CloseBlock();
                    break;
                case OpcodeV2.For:
                {
                    br.ReadByte();
                    var blockLen = br.ReadUInt16();
                    var count = br.ReadUInt16();
                    sb.WriteLine($"repeat ({count})");
                    _sb.OpenBlock();
                    break;
                }
                case OpcodeV2.Next:
                    sb.CloseBlock();
                    break;
                case OpcodeV2.While:
                {
                    br.ReadByte();
                    var blockLen = br.ReadUInt16();
                    sb.Write($"while (");
                    // _sb.OpenBlock();
                    _constructingBinaryExpression = true;
                    _expressionCount = 0;
                    break;
                }
                case OpcodeV2.Ewhile:
                    sb.CloseBlock();
                    break;
                case OpcodeV2.Do:
                {
                    br.ReadByte();
                    var blockLen = br.ReadUInt16();
                    _blockEnds.Push(((byte)opcode, offset + blockLen));
                    sb.WriteLine($"do");
                    _sb.OpenBlock();
                    break;
                }
                case OpcodeV2.Edwhile:
                {
                    _endDoWhile = true;
                    CloseCurrentBlock();
                    sb.Unindent();
                    sb.Write("} while (");
                    _constructingBinaryExpression = true;
                    _expressionCount = 0;
                    break;
                }
                case OpcodeV2.Switch:
                {
                    var varw = br.ReadByte();
                    sb.WriteLine($"switch ({GetVariableName(varw)})");
                    _sb.OpenBlock();
                    break;
                }
                case OpcodeV2.Case:
                {
                    br.ReadByte();
                    br.ReadUInt16();
                    var value = br.ReadUInt16();
                    sb.Unindent();
                    sb.WriteLine($"case {value}:");
                    sb.Indent();
                    break;
                }
                case OpcodeV2.Default:
                    sb.Unindent();
                    sb.WriteLine($"default:");
                    sb.Indent();
                    break;
                case OpcodeV2.Eswitch:
                    sb.CloseBlock();
                    break;
                case OpcodeV2.Goto:
                    var a = br.ReadByte();
                    var b = br.ReadByte();
                    var c = br.ReadByte();
                    var rel = br.ReadInt16();
                    var io = offset + rel;
                    sb.WriteLine($"goto {sb.GetLabelName(io)};");
                    sb.InsertLabel(io);
                    break;
                case OpcodeV2.Gosub:
                    var num = br.ReadByte();
                    sb.WriteLine($"{GetProcedureName(num)}();");
                    break;
                case OpcodeV2.Return:
                    sb.WriteLine("return;");
                    break;
                case OpcodeV2.Break:
                    sb.WriteLine("break;");
                    break;
            }
            return true;
        }

        private bool VisitOpcode(int offset, OpcodeV3 opcode, BinaryReader br)
        {
            var sb = _sb;
            br.ReadByte();
            switch (opcode)
            {
                default:
                    return false;
                case OpcodeV3.Nop:
                    break;
                case OpcodeV3.EvtEnd:
                {
                    br.ReadByte();
                    _lastReturnLine = sb.LineCount;
                    sb.WriteLine($"evt_end(0);");
                    break;
                }
                case OpcodeV3.EvtExec:
                {
                    var p0 = br.ReadByte();
                    var p1 = br.ReadByte();
                    if (p0 == 0xFF && p1 == 0x18)
                    {
                        var p2 = br.ReadByte();
                        sb.WriteLine($"fork {GetProcedureName(p2)};");
                    }
                    else
                    {
                        return false;
                    }
                    break;
                }
                case OpcodeV3.IfelCk:
                {
                    sb.Write("if (");
                    _constructingBinaryExpression = true;
                    _expressionCount = 0;
                    break;
                }
                case OpcodeV3.ElseCk:
                {
                    br.ReadByte();
                    var blockLen = br.ReadUInt16();
                    _sb.CloseBlock();
                    _blockEnds.Push(((byte)opcode, offset + blockLen));
                    sb.WriteLine("else");
                    _sb.OpenBlock();
                    break;
                }
                case OpcodeV3.EndIf:
                    _sb.CloseBlock();
                    break;
                case OpcodeV3.For:
                {
                    br.ReadByte();
                    var blockLen = br.ReadUInt16();
                    var count = br.ReadUInt16();
                    sb.WriteLine($"repeat ({count})");
                    _sb.OpenBlock();
                    break;
                }
                case OpcodeV3.EndFor:
                    sb.CloseBlock();
                    break;
                case OpcodeV3.While:
                {
                    br.ReadByte();
                    var blockLen = br.ReadUInt16();
                    sb.Write($"while (");
                    _constructingBinaryExpression = true;
                    _expressionCount = 0;
                    break;
                }
                case OpcodeV3.Ewhile:
                    sb.CloseBlock();
                    break;
                case OpcodeV3.Do:
                {
                    br.ReadByte();
                    var blockLen = br.ReadUInt16();
                    _blockEnds.Push(((byte)opcode, offset + blockLen));
                    sb.WriteLine($"do");
                    _sb.OpenBlock();
                    break;
                }
                case OpcodeV3.Edwhile:
                {
                    _endDoWhile = true;
                    CloseCurrentBlock();
                    sb.Unindent();
                    sb.Write("} while (");
                    _constructingBinaryExpression = true;
                    _expressionCount = 0;
                    break;
                }
                case OpcodeV3.Switch:
                {
                    var varw = br.ReadByte();
                    sb.WriteLine($"switch ({GetVariableName(varw)})");
                    _sb.OpenBlock();
                    break;
                }
                case OpcodeV3.Case:
                {
                    br.ReadByte();
                    br.ReadUInt16();
                    var value = br.ReadUInt16();
                    sb.Unindent();
                    sb.WriteLine($"case {value}:");
                    sb.Indent();
                    break;
                }
                case OpcodeV3.Default:
                    sb.Unindent();
                    sb.WriteLine($"default:");
                    sb.Indent();
                    break;
                case OpcodeV3.Eswitch:
                    sb.CloseBlock();
                    break;
                case OpcodeV3.Goto:
                    var a = br.ReadByte();
                    var b = br.ReadByte();
                    var c = br.ReadByte();
                    var rel = br.ReadInt16();
                    var io = offset + rel;
                    sb.WriteLine($"goto {sb.GetLabelName(io)};");
                    sb.InsertLabel(io);
                    break;
                case OpcodeV3.Gosub:
                    var num = br.ReadByte();
                    sb.WriteLine($"{GetProcedureName(num)}();");
                    break;
                case OpcodeV3.Return:
                    sb.WriteLine("return;");
                    break;
                case OpcodeV3.Break:
                    sb.WriteLine("break;");
                    break;
            }
            return true;
        }

        private bool VisitOpcode(int offset, byte opcode, BinaryReader br)
        {
            var sb = _sb;
            br.ReadByte();
            switch (opcode)
            {
                default:
                    return false;
                case 0x00:
                {
                    br.ReadByte();
                    _lastReturnLine = sb.LineCount;
                    sb.WriteLine($"evt_end(0);");
                    break;
                }
                case 0x01:
                {
                    sb.Write("if (");
                    _constructingBinaryExpression = true;
                    _expressionCount = 0;
                    break;
                }
                case 0x02:
                {
                    var blockLen = br.ReadByte();
                    _sb.CloseBlock();
                    _blockEnds.Push(((byte)opcode, offset + blockLen));
                    sb.WriteLine("else");
                    _sb.OpenBlock();
                    break;
                }
                case 0x03:
                    _sb.CloseBlock();
                    break;
                case 0xF4:
                    break;
            }
            return true;
        }


        private void DiassembleGeneralOpcode(BinaryReader br, int offset, byte opcode, int instructionLength, bool isEventOpcode = false)
        {
            var parameters = new List<object>();
            string opcodeName;

            var originalStreamPosition = br.BaseStream.Position;

            var opcodeRaw = br.ReadByte();
            Debug.Assert(opcodeRaw == opcode);

            br.BaseStream.Position = originalStreamPosition + 1;

            var signature = _constantTable.GetOpcodeSignature(opcode, isEventOpcode);
            var expectedLength = _constantTable.GetInstructionSize(opcode, br, isEventOpcode);
            if (expectedLength == 0 || expectedLength != instructionLength)
            {
                signature = "";
            }

            var colonIndex = signature.IndexOf(':');
            if (colonIndex == -1)
            {
                opcodeName = signature;
                if (opcodeName == "")
                {
                    opcodeName = "unk";
                    parameters.Add(opcode);
                }
                foreach (var b in br.ReadBytes(instructionLength))
                {
                    parameters.Add(b);
                }
            }
            else
            {
                opcodeName = signature.Substring(0, colonIndex);
                var pIndex = 0;
                for (int i = colonIndex + 1; i < signature.Length; i++)
                {
                    var c = signature[i];
                    string? szv;
                    using (var br2 = br.Fork())
                    {
                        br.BaseStream.Position = originalStreamPosition + 1;
                        szv = _constantTable.GetConstant(opcode, pIndex, br);
                    }
                    if (szv != null)
                    {
                        br.BaseStream.Position++;
                        if (c == 'L' || c == '~' || c == 'U' || c == 'I')
                            br.BaseStream.Position++;
                        parameters.Add(szv);
                    }
                    else
                    {
                        switch (c)
                        {
                            case 'l':
                            {
                                var blockLen = br.ReadByte();
                                var labelOffset = offset + blockLen;
                                if (!InScript(labelOffset))
                                {
                                    parameters.Add(blockLen);
                                    break;
                                }
                                _sb.InsertLabel(labelOffset);
                                parameters.Add(_sb.GetLabelName(labelOffset));
                                break;
                            }
                            case '\'':
                            {
                                var blockLen = br.ReadByte();
                                var labelOffset = offset + instructionLength + blockLen;
                                if (!InScript(labelOffset))
                                {
                                    parameters.Add(blockLen);
                                    break;
                                }
                                _sb.InsertLabel(labelOffset);
                                parameters.Add(_sb.GetLabelName(labelOffset));
                                break;
                            }
                            case 'L':
                            case '~':
                            {
                                var blockLen = c == '~' ? (int)br.ReadInt16() : (int)br.ReadUInt16();
                                var labelOffset = offset + instructionLength + blockLen;
                                if (c == '~')
                                    labelOffset = offset + blockLen;
                                if (!InScript(labelOffset))
                                {
                                    parameters.Add(blockLen);
                                    break;
                                }
                                _sb.InsertLabel(labelOffset);
                                parameters.Add(_sb.GetLabelName(labelOffset));
                                break;
                            }
                            case '@':
                            {
                                var blockLen = br.ReadInt16();
                                var labelOffset = offset + blockLen;
                                if (!InScript(labelOffset))
                                {
                                    parameters.Add(blockLen);
                                    break;
                                }
                                _sb.InsertLabel(labelOffset);
                                parameters.Add(_sb.GetLabelName(labelOffset));
                                break;
                            }
                            case 'b':
                            {
                                var temp = br.ReadByte();
                                var bitArray = temp >> 5;
                                var number = temp & 0b11111;
                                parameters.Add($"{bitArray << 5} | {number}");
                                break;
                            }
                            case 'u':
                                parameters.Add(br.ReadByte());
                                break;
                            case 'U':
                                parameters.Add(br.ReadUInt16());
                                break;
                            case 'I':
                                parameters.Add(br.ReadInt16());
                                break;
                            default:
                            {
                                var v = char.IsUpper(c) ? br.ReadUInt16() : br.ReadByte();
                                szv = _constantTable.GetConstant(c, v);
                                parameters.Add(szv ?? (object)v);
                                break;
                            }
                        }
                    }
                    pIndex++;
                }
            }
            if (!AssemblyFormat && _constructingBinaryExpression)
            {
                if (_expressionCount > 0)
                {
                    _sb.Write(" && ");
                }
                _sb.WriteStandardExpression(opcodeName, parameters.ToArray());
                _expressionCount++;
            }
            else
            {
                _sb.WriteStandardOpcode(opcodeName, parameters.ToArray());
            }

            var streamPosition = br.BaseStream.Position;
            if (streamPosition != originalStreamPosition + instructionLength)
                throw new Exception($"Opcode {opcode} not diassembled correctly.");
        }

        public override void VisitTrailingData(int offset, Span<byte> data)
        {
            if (!AssemblyFormat)
                return;

            var sb = new StringBuilder();
            for (int i = 0; i < data.Length; i += 16)
            {
                var slice = data.Slice(i, Math.Min(data.Length - i, 16));

                sb.Clear();
                for (int j = 0; j < slice.Length; j++)
                {
                    sb.AppendFormat("0x{0:X2}, ", slice[j]);
                }
                sb.Remove(sb.Length - 2, 2);
                _sb.CurrentOffset = offset + i;
                _sb.CurrentOpcodeBytes = slice.ToArray();
                _sb.WriteStandardOpcode("db", sb.ToString());
            }
        }

        private string GetVariableName(int id)
        {
            var name = _constantTable.GetNamedVariable(id);
            if (name != null)
                return name;
            return $"var[{id}]";
        }
    }
}
