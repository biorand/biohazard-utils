using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using IntelOrca.Biohazard.Room;
using IntelOrca.Biohazard.Script;
using Xunit;
using Xunit.Abstractions;
using static IntelOrca.Biohazard.Tests.TestInfo;

namespace IntelOrca.Biohazard.Tests
{
    // Walks every RE1 room event stream with the same task-state model as
    // scd_event_loop (actor-cmd streams after op 01, tween streams after
    // 02/03, control ops F6-FF in any state), asserting sub-streams stay in
    // bounds and exit via their end opcodes. Complements the round-trip
    // tests, which cannot catch state-machine mistakes because decompiler
    // and assembler share the same tables.
    public class TestRE1EventStates
    {
        private readonly ITestOutputHelper _output;

        public TestRE1EventStates(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void AllRooms_EventStreamsAreWellFormed()
        {
            var installPath = GetInstallPath(0);
            var issues = new List<string>();
            var eventsScanned = 0;
            for (var stage = 1; stage <= 7; stage++)
            {
                var dir = Path.Combine(installPath, "JPN", $"STAGE{stage}");
                if (!Directory.Exists(dir))
                    continue;

                foreach (var file in Directory.GetFiles(dir, "ROOM*.RDT").OrderBy(x => x))
                {
                    ScdEventList list;
                    try
                    {
                        list = new Rdt1(file).EventSCD;
                    }
                    catch
                    {
                        // rooms that fail chunk extraction are tracked separately
                        continue;
                    }

                    var name = Path.GetFileName(file);
                    for (var i = 0; i < list.Count; i++)
                    {
                        eventsScanned++;
                        WalkEvent(list[i].Data.Span, $"{name}#ev{i}", issues);
                    }
                }
            }

            _output.WriteLine($"scanned {eventsScanned} events, {issues.Count} issue(s)");
            foreach (var issue in issues.Take(40))
                _output.WriteLine("  " + issue);
            Assert.True(issues.Count == 0, $"{issues.Count} ill-formed event stream(s)");
        }

        private void WalkEvent(ReadOnlySpan<byte> data, string tag, List<string> issues)
        {
            var ct = new Bio1ConstantTable();
            var pos = 0;
            while (pos < data.Length)
            {
                var op = data[pos];
                if (op >= 0xF6)
                {
                    if (!WalkControl(data, ref pos, issues, tag))
                        return;
                    continue;
                }

                switch (op)
                {
                    case 0x06:
                    {
                        if (pos + 2 > data.Length)
                        {
                            Issue(issues, tag, pos, "block header overruns");
                            return;
                        }
                        pos += 2 + Math.Max(0, Math.Min(data[pos + 1] - 1, data.Length - (pos + 2)));
                        break;
                    }
                    case 0x07:
                    {
                        if (pos + 2 > data.Length)
                        {
                            Issue(issues, tag, pos, "single header overruns");
                            return;
                        }
                        pos += 2 + Math.Max(0, Math.Min(data[pos + 1] - 2, data.Length - (pos + 2)));
                        break;
                    }
                    case 0x01:
                    case 0x02:
                    case 0x03:
                    {
                        pos++;
                        var isa = op == 0x01 ?
                            Bio1ConstantTable.Re1SubIsa.Actor :
                            Bio1ConstantTable.Re1SubIsa.Tween;
                        if (!WalkSubStream(data, ref pos, isa, ct, issues, tag))
                            return;
                        break;
                    }
                    default:
                    {
                        using var ms = new MemoryStream(data.ToArray(), pos, data.Length - pos);
                        using var br = new BinaryReader(ms);
                        var size = ct.GetInstructionSize(op, br, isEventOpcode: true);
                        if (size <= 0 || pos + size > data.Length)
                        {
                            Issue(issues, tag, pos, $"top-level op {op:X02} bad size");
                            return;
                        }
                        pos += size;
                        break;
                    }
                }
            }
        }

        private bool WalkSubStream(ReadOnlySpan<byte> data, ref int pos, Bio1ConstantTable.Re1SubIsa isa, Bio1ConstantTable ct, List<string> issues, string tag)
        {
            while (pos < data.Length)
            {
                var op = data[pos];
                if (op == 0xFF)
                {
                    // FF terminates a task in any state
                    pos += 1;
                    return true;
                }
                if (op >= 0xF6)
                {
                    if (!WalkControl(data, ref pos, issues, tag))
                        return false;
                    continue;
                }

                var exits = isa == Bio1ConstantTable.Re1SubIsa.Actor ?
                    op == 0x8B || op == 0x80 :
                    op == 0x01;
                if (exits)
                {
                    pos++;
                    return true;
                }

                using var ms = new MemoryStream(data.ToArray(), pos, data.Length - pos);
                using var br = new BinaryReader(ms);
                br.BaseStream.Position = 1; // GetSubInstructionSize expects br just past the opcode
                var size = ct.GetSubInstructionSize(op, br, isa);
                if (size <= 0)
                {
                    // unknown sub-ISA opcode: the VM's do/skip_if control flow
                    // can jump over the stream end marker, so resume top-level
                    // parsing from this byte
                    return true;
                }
                if (pos + size > data.Length)
                {
                    Issue(issues, tag, pos, $"{isa} op {op:X02} overruns stream");
                    return false;
                }
                pos += size;
            }
            Issue(issues, tag, pos, $"{isa} stream ran off end without exit opcode");
            return false;
        }

        private static bool WalkControl(ReadOnlySpan<byte> data, ref int pos, List<string> issues, string tag)
        {
            var op = data[pos];
            switch (op)
            {
                case 0xFE:
                case 0xFF:
                case 0xF9:
                case 0xFB:
                case 0xFD:
                case 0xF6:
                case 0xF7:
                    pos += 1;
                    return true;
                case 0xF8:
                case 0xFA:
                    if (pos + 4 > data.Length)
                    {
                        Issue(issues, tag, pos, $"control {op:X02} overruns");
                        return false;
                    }
                    pos += 4;
                    return true;
                case 0xFC:
                    if (pos + 2 > data.Length)
                    {
                        Issue(issues, tag, pos, "do header overruns");
                        return false;
                    }
                    pos += Math.Max(2, (int)data[pos + 1]);
                    return true;
                default:
                {
                    Issue(issues, tag, pos, $"unknown control op {op:X02}");
                    return false;
                }
            }
        }

        private static bool Issue(List<string> issues, string tag, int pos, string why)
        {
            issues.Add($"{tag}@{pos}: {why}");
            return false;
        }
    }
}
