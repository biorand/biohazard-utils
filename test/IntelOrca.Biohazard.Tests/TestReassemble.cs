using System;
using System.IO;
using System.Linq;
using IntelOrca.Biohazard.Extensions;
using IntelOrca.Biohazard.Room;
using IntelOrca.Biohazard.Script;
using IntelOrca.Biohazard.Script.Compilation;
using Xunit;
using Xunit.Abstractions;
using static IntelOrca.Biohazard.Tests.TestInfo;

namespace IntelOrca.Biohazard.Tests
{
    public class TestReassemble
    {
        private readonly ITestOutputHelper _output;

        public TestReassemble(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void RE1_100()
        {
            var rdtFileName = "ROOM1000.RDT";
            var rdtFile = GetRdt(BioVersion.Biohazard1, rdtFileName);
            var sPath = Path.ChangeExtension(rdtFileName, ".s");
            var fail = AssertReassembleRdt(rdtFile, sPath);
            Assert.False(fail);
        }

        [Fact]
        public void RE1_Chris() => CheckRDTs(BioVersion.Biohazard1, x => x[x.Length - 5] == '0');

        [Fact]
        public void RE1_Jill() => CheckRDTs(BioVersion.Biohazard1, x => x[x.Length - 5] == '1');

        [Fact]
        public void RE2_Leon() => CheckRDTs(BioVersion.Biohazard2, x => x[x.Length - 5] == '0');

        [Fact]
        public void RE2_Claire() => CheckRDTs(BioVersion.Biohazard2, x => x[x.Length - 5] == '1');

        [Fact]
        public void RE3()
        {
            var installPath = TestInfo.GetInstallPath(2);
            var rofs = new RE3Archive(Path.Combine(installPath, "rofs13.dat"));
            var fail = false;
            foreach (var file in rofs.Files)
            {
                var fileName = Path.GetFileName(file);
                var rdt = rofs.GetFileContents(file);
                var rdtFile = new Rdt2(BioVersion.Biohazard3, rdt);
                var sPath = Path.ChangeExtension(fileName, ".s");
                fail |= AssertReassembleRdt(rdtFile, sPath);
            }
            Assert.False(fail);
        }

        private void CheckRDTs(BioVersion version, Predicate<string> predicate)
        {
            var rdtFileNames = GetAllRdtFileNames(version);
            var fail = false;
            foreach (var rdtFileName in rdtFileNames)
            {
                var rdtId = RdtId.Parse(rdtFileName.Substring(rdtFileName.Length - 8, 3));
                if (rdtId == new RdtId(4, 0x05))
                    continue;
                if (rdtId == new RdtId(6, 0x05))
                    continue;
                if (rdtId.Stage > 6)
                    continue;
                if (!predicate(rdtFileName))
                    continue;

                var rdtFile = GetRdt(version, rdtFileName);
                var sPath = Path.ChangeExtension(rdtFileName, ".s");
                fail |= AssertReassembleRdt(rdtFile, sPath);
            }
            Assert.False(fail);
        }

        private bool AssertReassembleRdt(IRdt rdtFile, string sPath)
        {
            var disassembly = rdtFile.DisassembleScd();
            var scdAssembler = new ScdAssembler();
            var fail = false;
            int err;
            try
            {
                err = scdAssembler.Generate(new StringFileIncluder(sPath, disassembly), sPath);
            }
            catch
            {
                _output.WriteLine("Exception occured in '{0}'", sPath);
                return true;
            }
            if (err != 0)
            {
                foreach (var error in scdAssembler.Errors.Errors)
                {
                    _output.WriteLine(error.ToString());
                }
                fail = true;
            }
            else
            {
                if (rdtFile.Version == BioVersion.Biohazard1)
                {
                    var scdInit = GetScdMemory(rdtFile, BioScriptKind.Init);
                    var scdDataInit = scdAssembler.Operations
                        .OfType<ScdRdtEditOperation>()
                        .FirstOrDefault(x => x.Kind == BioScriptKind.Init)
                        .Container;
                    var index = CompareByteArray(scdInit, scdDataInit.Data);
                    if (index != -1)
                    {
                        _output.WriteLine(".init differs at 0x{0:X2} for '{1}'", index, sPath);
                        fail = true;
                    }

                    var scdMain = GetScdMemory(rdtFile, BioScriptKind.Main);
                    var scdDataMain = scdAssembler.Operations
                        .OfType<ScdRdtEditOperation>()
                        .FirstOrDefault(x => x.Kind == BioScriptKind.Main)
                        .Container;
                    index = CompareByteArray(scdMain, scdDataMain.Data);
                    if (index != -1)
                    {
                        _output.WriteLine(".main differs at 0x{0:X2} for '{1}'", index, sPath);
                        fail = true;
                    }

                    var scdEventsNew = scdAssembler.Operations
                        .OfType<ScdRdtEditOperation>()
                        .FirstOrDefault(x => x.Kind == BioScriptKind.Event)
                        ?.Events;

                    var rdt1 = rdtFile as Rdt1;
                    var sceEventsOriginal = rdt1.EventSCD;
                    if (sceEventsOriginal.Count == (scdEventsNew?.Count ?? 0))
                    {
                        for (var i = 0; i < sceEventsOriginal.Count; i++)
                        {
                            var scdEventOriginal = sceEventsOriginal[i];
                            var scdEventNew = scdEventsNew.Value[i];
                            index = CompareByteArray(scdEventOriginal.Data, scdEventNew.Data);
                            if (index != -1)
                            {
                                _output.WriteLine(".event event_{2:X2} differs at 0x{0:X2} for '{1}'", index, sPath, i);
                                fail = true;
                            }
                        }
                    }
                    else
                    {
                        _output.WriteLine("Incorrect number of events for '{0}'", sPath);
                        fail = true;
                    }
                }
                else
                {
                    var scdInit = GetScdMemory(rdtFile, BioScriptKind.Init);
                    var scdDataInit = scdAssembler.Operations
                        .OfType<ScdRdtEditOperation>()
                        .FirstOrDefault(x => x.Kind == BioScriptKind.Init)
                        .Data;
                    var index = CompareByteArray(scdInit, scdDataInit.Data);
                    if (index != -1)
                    {
                        _output.WriteLine(".init differs at 0x{0:X2} for '{1}'", index, sPath);
                        fail = true;
                    }

                    if (rdtFile.Version != BioVersion.Biohazard3)
                    {
                        var scdMain = GetScdMemory(rdtFile, BioScriptKind.Main);
                        var scdDataMain = scdAssembler.Operations
                            .OfType<ScdRdtEditOperation>()
                            .FirstOrDefault(x => x.Kind == BioScriptKind.Main)
                            .Data;
                        index = CompareByteArray(scdMain, scdDataMain.Data);
                        if (index != -1)
                        {
                            _output.WriteLine(".main differs at 0x{0:X2} for '{1}'", index, sPath);
                            fail = true;
                        }
                    }
                }
            }
            return fail;
        }

        private ReadOnlyMemory<byte> GetScdMemory(IRdt rdt, BioScriptKind kind)
        {
            if (rdt is Rdt1 rdt1)
            {
                if (kind == BioScriptKind.Init)
                    return rdt1.InitSCD.Data;
                if (kind == BioScriptKind.Main)
                    return rdt1.MainSCD.Data;
            }
            else if (rdt is Rdt2 rdt2)
            {
                if (kind == BioScriptKind.Init)
                    return rdt2.SCDINIT.Data;
                if (kind == BioScriptKind.Main)
                    return rdt2.SCDMAIN.Data;
            }
            throw new NotImplementedException();
        }

        private static int CompareByteArray(ReadOnlyMemory<byte> a, ReadOnlyMemory<byte> b) => CompareByteArray(a.Span, b.Span);
        private static int CompareByteArray(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        {
            var minLen = Math.Min(a.Length, b.Length);
            for (int i = 0; i < minLen; i++)
            {
                if (a[i] != b[i])
                    return i;
            }
            if (a.Length != b.Length)
                return minLen;
            return -1;
        }
    }
}
