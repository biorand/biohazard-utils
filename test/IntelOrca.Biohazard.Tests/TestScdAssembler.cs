using System.Linq;
using IntelOrca.Biohazard.Room;
using IntelOrca.Biohazard.Script;
using IntelOrca.Biohazard.Script.Compilation;
using Xunit;

namespace IntelOrca.Biohazard.Tests
{
    public class TestScdAssembler
    {
        [Fact]
        public void TestLabelInSameProcedure()
        {
            AssertAssemble("02000700040001", 0, @"
.version 3

.proc main
else 0, skip
skip:
db 0x01
");
        }

        [Fact]
        public void TestLabelInOtherProcedure()
        {
            AssertAssemble("04000800070004000114", 1, @"
.version 3

.proc main
else 0, off_000A

.proc main_01
off_000A:
db 0x01, 0x14
");
        }

        [Fact]
        public void TestLabelInOtherProcedureWithOffset()
        {
            AssertAssemble("04000800070006000114", 1, @"
.version 3

.proc main
else 0, off_000A + 2

.proc main_01
off_000A:
db 0x01, 0x14
");
        }

        [Fact]
        public void TestUnknownLabel()
        {
            var (assembler, result) = Assemble(@"
.version 3

.proc main
else 0, nowhere
db 0x01
");
            Assert.NotEqual(0, result);
            Assert.Equal(0, assembler.Errors.WarningCount);
            var error = Assert.Single(assembler.Errors.Errors, x => x.Kind == ErrorKind.Error);
            Assert.Contains("has not been defined as a label within the same procedure", error.Message);
        }

        private static void AssertAssemble(string expected, int expectedWarnings, string script)
        {
            var (assembler, result) = Assemble(script);
            Assert.Equal(0, result);
            Assert.Equal(0, assembler.Errors.ErrorCount);
            Assert.Equal(expectedWarnings, assembler.Errors.WarningCount);

            var scd = assembler.Operations
                .OfType<ScdRdtEditOperation>()
                .Single()
                .Data;
            var actual = string.Concat(scd.Data.ToArray().Select(x => x.ToString("X2")));
            Assert.Equal(expected, actual);
        }

        private static (ScdAssembler Assembler, int Result) Assemble(string script)
        {
            var fileName = "temp.s";
            var assembler = new ScdAssembler();
            var result = assembler.Generate(new StringFileIncluder(fileName, script), fileName);
            return (assembler, result);
        }
    }
}
