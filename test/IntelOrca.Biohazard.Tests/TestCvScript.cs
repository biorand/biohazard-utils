using System;
using System.IO;
using System.Linq;
using IntelOrca.Biohazard.Room;
using IntelOrca.Biohazard.Script;
using Xunit;

namespace IntelOrca.Biohazard.Tests
{
    public class TestCvScript
    {
        [Fact]
        public void DisassembleNamedOpcodesAndSubOpcodes()
        {
            var script = Disassemble(
                0x64, 0x07, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00,
                0x69, 0x35, 0x00, 0x00,
                0x04, 0x01, 0x20, 0x00, 0x00, 0x01,
                0x06, 0x00, 0x00, 0x01,
                0x00, 0x00);

            Assert.Contains("player_ctr_pos_set(1, 2, 3)", script);
            Assert.Contains("common_ctr_mtn_md_set(", script);
            Assert.Contains("ck(FG_EVENT", script);
            Assert.Contains("cmpb(VAR_STAGE, CMP_EQ, 1)", script);
        }

        [Fact]
        public void DisassembleStructuredLoopsAndSleep()
        {
            var script = Disassemble(
                0xF8, 0xF9, 0x0A, 0x00,
                0xFA, 0x00, 0x03, 0x00,
                0xFB, 0x00,
                0xFF, 0x00);

            Assert.Contains("sleep(10);", script);
            Assert.Contains("repeat (3)", script);
        }

        [Fact]
        public void DisassembleWhileLoop()
        {
            var script = Disassemble(
                0xFC, 0x06,
                0x04, 0x01, 0x20, 0x00, 0x00, 0x01,
                0xFD,
                0x00, 0x00);

            Assert.Contains("while (ck(FG_EVENT", script);
        }

        [Fact]
        public void CvProcedureCanContainMultipleEventBlocks()
        {
            var script = DisassembleAssembly(0xFF, 0x00, 0xFF, 0x00, 0x00, 0x00);
            Assert.Equal(2, script.Split(new[] { "event_end" }, StringSplitOptions.None).Length - 1);
            Assert.DoesNotContain("db", script);
        }

        [Fact]
        public void CvOpcodeSignaturesMatchInstructionSizes()
        {
            var table = new BioCvConstantTable();
            for (var opcode = 0; opcode < 256; opcode++)
            {
                if (BioCvConstantTable.IsSubOpcodeParent((byte)opcode))
                    continue;

                var size = table.GetInstructionSize((byte)opcode, null);
                var signature = table.GetOpcodeSignature((byte)opcode);
                var colonIndex = signature.IndexOf(':');
                if (colonIndex == -1)
                    continue;

                Assert.Equal(size - 1, GetSignatureLength(signature.Substring(colonIndex + 1)));
            }
        }

        [Fact]
        public void CvSubOpcodeSignaturesMatchInstructionSizes()
        {
            var table = new BioCvConstantTable();
            foreach (var key in BioCvConstantTable.GetSubOpcodeKeys())
            {
                var opcode = (byte)(key >> 8);
                var subOpcode = (byte)key;
                var isVariableSize = key == 0x6923;
                var selectors = isVariableSize ? new byte[] { 0, 0, 1, 0 } : new byte[] { 0, 0 };

                foreach (var selector in selectors)
                {
                    var data = new byte[] { subOpcode, 0, selector, 0, 0 };
                    using var stream = new MemoryStream(data);
                    using var reader = new BinaryReader(stream);
                    var size = table.GetInstructionSize(opcode, reader);
                    Assert.Equal(0, reader.BaseStream.Position);

                    var signature = table.GetSubOpcodeSignature(opcode, subOpcode, size);
                    var colonIndex = signature.IndexOf(':');
                    Assert.True(colonIndex > 0, signature);
                    Assert.Equal(size - 1, GetSignatureLength(signature.Substring(colonIndex + 1)));
                }
            }
        }

        [Fact]
        public void CvAllTopLevelOpcodesDisassemble()
        {
            var table = new BioCvConstantTable();
            for (var opcode = 0; opcode < 256; opcode++)
            {
                var data = CreateOpcodeBytes(table, (byte)opcode, 0);
                if (data == null)
                    continue;

                var script = Disassemble(data);
                Assert.Contains("version 4", script);
            }
        }

        [Fact]
        public void CvAllSubOpcodesDisassemble()
        {
            var table = new BioCvConstantTable();
            foreach (var key in BioCvConstantTable.GetSubOpcodeKeys())
            {
                var opcode = (byte)(key >> 8);
                var subOpcode = (byte)key;
                var data = CreateOpcodeBytes(table, opcode, subOpcode);
                Assert.NotNull(data);

                var script = Disassemble(data);
                Assert.Contains("version 4", script);
            }
        }

        [Fact]
        public void CvSubOpcodeNamesAreUnique()
        {
            var table = new BioCvConstantTable();
            var names = BioCvConstantTable.GetSubOpcodeKeys()
                .Select(x => table.GetSubOpcodeSignature((byte)(x >> 8), (byte)x, 2))
                .Select(x => x.Substring(0, x.IndexOf(':')))
                .ToArray();
            Assert.Equal(names.Length, names.Distinct().Count());
        }

        private static byte[] CreateOpcodeBytes(BioCvConstantTable table, byte opcode, byte subOpcode)
        {
            if (BioCvConstantTable.IsSubOpcodeParent(opcode))
            {
                var buffer = new byte[32];
                buffer[0] = subOpcode;
                using var stream = new MemoryStream(buffer);
                using var reader = new BinaryReader(stream);
                var size = table.GetInstructionSize(opcode, reader);
                var data = new byte[size];
                data[0] = opcode;
                data[1] = subOpcode;
                return data;
            }

            var instructionSize = table.GetInstructionSize(opcode, null);
            if (instructionSize <= 0)
                return null;

            var result = new byte[instructionSize];
            result[0] = opcode;
            return result;
        }

        private static int GetSignatureLength(string signature)
        {
            var length = 0;
            foreach (var c in signature)
            {
                length += c == 'U' || c == 'I' || c == 'L' || c == '~' || c == '@' ? 2 : 1;
            }
            return length;
        }

        private static string Disassemble(params byte[] data)
        {
            var procedureList = CreateProcedureList(data);

            var decompiler = new ScriptDecompiler(false, false);
            decompiler.VisitVersion(BioVersion.BiohazardCv);
            var reader = new ScdReader();
            reader.ReadScript(procedureList.Data, BioVersion.BiohazardCv, BioScriptKind.Main, decompiler);
            return decompiler.GetScript();
        }

        private static string DisassembleAssembly(params byte[] data)
        {
            var procedureList = CreateProcedureList(data);
            var reader = new ScdReader();
            return reader.Diassemble(procedureList, BioVersion.BiohazardCv, BioScriptKind.Main);
        }

        private static ScdProcedureList CreateProcedureList(byte[] data)
        {
            var ms = new MemoryStream();
            var bw = new BinaryWriter(ms);
            bw.Write(4);
            bw.Write(data);
            return new ScdProcedureList(BioVersion.BiohazardCv, ms.ToArray());
        }
    }
}
