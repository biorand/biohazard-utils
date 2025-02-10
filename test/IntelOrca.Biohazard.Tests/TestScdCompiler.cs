using System.Linq;
using IntelOrca.Biohazard.Room;
using IntelOrca.Biohazard.Script;
using IntelOrca.Biohazard.Script.Compilation;
using Xunit;

namespace IntelOrca.Biohazard.Tests
{
    public class TestScdCompiler
    {
        [Fact]
        public void TestEmpty()
        {
            var expected = "0200090A320001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    sleep(10, 50);
    ;
}
");
        }

        [Fact]
        public void TestComment()
        {
            var expected = "020001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    // = $ sleep(10, 50);
}
");
        }

        [Fact]
        public void TestBlockComment()
        {
            var expected = "020001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    /*
        sleep(10, 50);
        sleep(10, 50);
    */
}
");
        }

        [Fact]
        public void TestBuiltInConstant()
        {
            var expected = "02004400002A00800012005A00830000008300000000000001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    sce_em_set(0, 0, ENEMY_TYRANT_1, 0, 128, 0, 18, 0, 90, -32000, 0, -32000, 0, 0, 0);
}
");
        }

        [Fact]
        public void TestOrOperator()
        {
            var expected = "02002C02043100005CC7B4C9E015080700000000FFFF01000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    aot_set(2, SCE_MESSAGE, SAT_PL | SAT_MANUAL | SAT_FRONT, 0, 0, -14500, -13900, 5600, 1800, 0, 0, 0, 0, 255, 255);
}
");
        }

        [Fact]
        public void TestDivideOperator()
        {
            var expected = "02003411020001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    member_set(17, -3600 / -1800);
}
");
        }

        [Fact]
        public void TestIfStatement()
        {
            var expected = "020006000A0021010400090A3200080001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    if (ck(1, 4, 0)) {
        sleep(10, 50);
    }
}
");
        }

        [Fact]
        public void TestIfAndStatement()
        {
            var expected = "020006000E002101040021020500090A3200080001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    if (ck(1, 4, 0) && ck(2, 5, 0)) {
        sleep(10, 50);
    }
}
");
        }

        [Fact]
        public void TestIfElseStatement()
        {
            var expected = "020006000C00210104002201040107000800090A320001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    if (ck(1, 4, 0)) {
        set(1, 4, 1);
    } else {
        sleep(10, 50);
    }
}
");
        }

        [Fact]
        public void TestConditionlessIf()
        {
            var expected = "020006000600090A3200080001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    if () {
        sleep(10, 50);
    }
}
");
        }

        [Fact]
        public void TestWhileStatement()
        {
            var expected = "02000F060A0023001A0504000200100001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    while (cmp(0, 26, CMP_NE, 4))
    {
        evt_next();
    }
}
");
        }

        [Fact]
        public void TestDoWhileStatement()
        {
            var expected = "020011000800020012042105200001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    do {
        evt_next();
    } while (ck(5, 32, 0));
}
");
        }

        [Fact]
        public void TestRepeatStatement()
        {
            var expected = "0200020017FFFF00FEFF01000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    repeat {
        evt_next();
    }
}
");
        }

        [Fact]
        public void TestRepeatCount()
        {
            var expected = "02000D0004002A0002000E0001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    repeat (42) {
        evt_next();
    }
}
");
        }

        [Fact]
        public void TestSwitchStatement()
        {
            var expected = "0200131A2800140006000000090A14001A00140000000100140006000200090A1E001A001500090A28001A00160001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    switch (V_CUT)
    {
        case 0:
            sleep(10, 20);
            break;
        case 1:
        case 2:
        {
            sleep(10, 30);
            break;
        }
        default:
            sleep(10, 40);
            break;
    }
}
");
        }

        [Fact]
        public void TestFork()
        {
            var expected = "0200010006000C000E0004FF180201000100090A1E000100";
            AssertScd(expected, @"
#version 2

proc main
{
    fork wait_for_mr_x;
}

proc wait_for_mr_x
{
    sleep(10, 30);
}
");
        }

        [Fact]
        public void TestCall()
        {
            var expected = "0200010006000A000C00180201000100090A1E000100";
            AssertScd(expected, @"
#version 2

proc main
{
    wait_for_mr_x();
}

proc wait_for_mr_x
{
    sleep(10, 30);
}
");
        }

        [Fact]
        public void TestAotEvent()
        {
            var expected = "0200010006001C001E002C060541000000C242C1100E0807FF0018020000010001000100";
            AssertScd(expected, @"
#version 2

proc main
{
    aot_set(6, SCE_EVENT, SAT_PL | SAT_UNDER, 0, 0, -15872, -16062, 3600, 1800, 255, 0, I_GOSUB, some_event, 0, 0);
}

proc some_event
{
}
");
        }

        [Fact]
        public void TestInclude()
        {
            var mainScript = @"
#version 2

proc main
{
    wait_for_mr_x();
}

#include ""common.bio""
";
            var incScript = @"
proc wait_for_mr_x
{
    sleep(10, 30);
}
";

            var includer = new StringFileIncluder();
            includer.AddFile("/test/main.bio", mainScript);
            includer.AddFile("/test/common.bio", incScript);
            var expected = "0200010006000A000C00180201000100090A1E000100";
            AssertScd(expected, includer, "/test/main.bio");

        }

        [Fact]
        public void TestDefine_Constant()
        {
            var expected = "0200010004000A00090A1E0001000100";
            AssertScd(expected, @"
#version 2
#define SLEEP_1s 30

proc main
{
    sleep(10, SLEEP_1s);
}
");
        }

        [Fact]
        public void TestDefine_ConstantFn()
        {
            var expected = "0200010004000A00090A1E0001000100";
            AssertScd(expected, @"
#version 2
#define SLEEP_1s() 30

proc main
{
    sleep(10, SLEEP_1s());
}
");
        }

        [Fact]
        public void TestDefine_ConstantRecursive()
        {
            var expected = "0200010004000A00090A1E0001000100";
            AssertScd(expected, @"
#version 2
#define SEC_1       30
#define SLEEP_1s    SEC_1

proc main
{
    sleep(10, SLEEP_1s);
}
");
        }

        [Fact]
        public void TestDefine_WithArgs()
        {
            var expected = "0200010004000A00090A1E0001000100";
            AssertScd(expected, @"
#version 2
#define SLEEP(time) sleep(10, time)

proc main
{
    SLEEP(30);
}
");
        }

        [Fact]
        public void TestDefine_Nested()
        {
            var expected = "0200010004000A00090A1E0001000100";
            AssertScd(expected, @"
#version 2
#define SECONDS(time) time * 30
#define SLEEP(time) sleep(10, time)

proc main
{
    SLEEP(SECONDS(1));
}
");
        }

        [Fact]
        public void TestDefine_Recursive()
        {
            var expected = "0200010004000A00090A1E0001000100";
            AssertScd(expected, @"
#version 2
#define SECONDS(time) time * 30
#define SLEEP(time) sleep(10, SECONDS(time))

proc main
{
    SLEEP(1);
}
");
        }

        [Fact]
        public void TestDefine_MultiLine()
        {
            var expected = "02000100040014000F060A0023001A0501000200100001000100";
            AssertScd(expected, @"
#version 2
#define WAIT_FOR_CUT(n)                    \
    while (cmp(0, V_CUT, CMP_NE, n / 2)) {  \
        evt_next();                          \
    }

proc main
{
    WAIT_FOR_CUT(2);
}
");
        }

        [Fact]
        public void TestDefine_ComplexExpression()
        {
            var expected = "0200010004000E00570001021EFF000001000100";
            AssertScd(expected, @"
#version 2
#define BGM_TYRANT                  0x001E
#define MUS_NONE                    0xFF00
#define SET_BGM(room, bgm, mus)     sce_bgmtbl_set(0, room & 0xFF, (room >> 8) & 0xFF, bgm | mus, 0)

proc main
{
    SET_BGM(0x201, BGM_TYRANT, MUS_NONE);
}
");
        }

        [Fact]
        public void TestIfDef()
        {
            var expected = "0200010004000A00090A320001000100";
            AssertScd(expected, @"
#version 2

proc main
{
#if defined(DEBUG)
    sleep(10, 30);
#endif
    sleep(10, 50);
}
");
        }

        [Fact]
        public void TestIfDef_AfterDefine()
        {
            var expected = "0200010004000E00090A1E00090A320001000100";
            AssertScd(expected, @"
#version 2
#define DEBUG 1

proc main
{
#if defined(DEBUG)
    sleep(10, 30);
#endif
    sleep(10, 50);
}
");
        }

        [Fact]
        public void TestAnonymousProcedure()
        {
            var expected = "0200010006000C000E0004FF180201000100090A1E000100";
            AssertScd(expected, @"
#version 2

proc main
{
    fork {
        sleep(10, 30);
    }
}
");
        }

        [Fact]
        public void TestGotoPastLabel()
        {
            var expected = "0200020017FFFF00FEFF01000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
start:
    evt_next();
    goto start;
}
");
        }

        [Fact]
        public void TestGotoFutureLabel()
        {
            var expected = "02000D0016000300020006000E0023001A00020017FFFF000A0008000E00020001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    repeat (3)
    {
        evt_next();
        if (cmp(0, V_CUT, CMP_EQ, 2))
        {
            goto loopend;
        }
    }
loopend:
    evt_next();
}
");
        }

        [Fact]
        public void TestGotoUnknownLabel()
        {
            AssertScd(null, @"
#version 2

proc init
{
    evt_next();
    goto start;
}
");
        }

        [Fact]
        public void TestReturn()
        {
            var expected = "0200190001000400060001000100";
            AssertScd(expected, @"
#version 2

proc init
{
    return;
}
");
        }

        private void AssertCompile(string script)
        {
            var fileName = "temp.bio";
            var scdCompiler = new ScdCompiler();
            var result = scdCompiler.Generate(new StringFileIncluder(fileName, script), fileName);
            Assert.Equal(0, result);
        }

        private void AssertScd(string expected, string script)
        {
            var fileName = "temp.bio";
            var includer = new StringFileIncluder(fileName, script);
            AssertScd(expected, includer, fileName);
        }

        private void AssertScd(string expected, IFileIncluder includer, string path)
        {
            var scdCompiler = new ScdCompiler();
            var result = scdCompiler.Generate(includer, path);
            if (expected == null)
            {
                Assert.NotEqual(0, result);
            }
            else
            {
                Assert.Equal(0, result);

                var scdInit = GetScd(scdCompiler, BioScriptKind.Init);
                var scdMain = GetScd(scdCompiler, BioScriptKind.Main);
                var sInit = Disassemble(scdInit, BioScriptKind.Init);
                var sMain = Disassemble(scdMain, BioScriptKind.Main);

                var actual = string.Concat(scdInit.Data
                    .ToArray()
                    .Concat(scdMain.Data.ToArray())
                    .Select(x => x.ToString("X2"))
                    .ToArray());
                Assert.Equal(expected, actual);
            }
        }

        private string Disassemble(ScdProcedureList scd, BioScriptKind kind)
        {
            var scdReader = new ScdReader();
            return scdReader.Diassemble(scd, BioVersion.Biohazard2, kind, true);
        }

        private static ScdProcedureList GetScd(ScdCompiler compiler, BioScriptKind kind)
        {
            var scd = compiler.Operations
                .OfType<ScdRdtEditOperation>()
                .FirstOrDefault(x => x.Kind == kind)
                .Data;
            return scd;
        }
    }
}
