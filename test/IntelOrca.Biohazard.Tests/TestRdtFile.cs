using System;
using IntelOrca.Biohazard.Room;
using Xunit;
using Xunit.Abstractions;
using static IntelOrca.Biohazard.Tests.MemoryAssert;
using static IntelOrca.Biohazard.Tests.TestInfo;

namespace IntelOrca.Biohazard.Tests
{
    public class TestRdtFile
    {
        private readonly ITestOutputHelper _output;

        public TestRdtFile(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void RE1_100()
        {
            var rdt = (Rdt1)GetRdt(BioVersion.Biohazard1, "ROOM1000.RDT");

            Assert.Equal(2432, rdt.EDD.Data.Length);    // animation.edd
            Assert.Equal(52804, rdt.EMR.Data.Length);   // animation.emr
            Assert.Equal(14, rdt.BLK.Length);           // block.blk
            Assert.Equal(264, rdt.RID.Length);          // camera.rid
            Assert.Equal(192, rdt.SCA.Length);          // collision.sca
            Assert.Equal(12, rdt.FLR.Length);           // floor.flr
            Assert.Equal(66, rdt.LIT.Length);           // light.lit
            Assert.Equal(2076, rdt.PRI.Length);         // sprite.pri
            Assert.Equal(280, rdt.RVD.Length);          // zone.rvd
        }

        [Fact]
        public void RE1_100_SCD_INIT_MAIN_EVENTS()
        {
            var rdt = (Rdt1)GetRdt(BioVersion.Biohazard1, "ROOM1000.RDT");
            var rdtBuilder = rdt.ToBuilder();
            {
                var scd = rdt.InitSCD;
                var scdBuilder = scd.ToBuilder();
                var rebuiltScd = scdBuilder.ToContainer();
                rdtBuilder.InitSCD = rebuiltScd;
                AssertMemory(scd.Data, rebuiltScd.Data);
            }
            {
                var scd = rdt.MainSCD;
                var scdBuilder = scd.ToBuilder();
                var rebuiltScd = scdBuilder.ToContainer();
                rdtBuilder.MainSCD = rebuiltScd;
                AssertMemory(scd.Data, rebuiltScd.Data);
            }
            var rebuiltRdt = rdtBuilder.ToRdt();
            AssertMemory(rdt.Data, rebuiltRdt.Data);
        }

        [Fact]
        public void RE1_100_SCD_EVENTS()
        {
            var rdt = (Rdt1)GetRdt(BioVersion.Biohazard1, "ROOM1000.RDT");

            var evtList = rdt.EventSCD;
            var evtListBuilder = evtList.ToBuilder();
            var rebuiltEvtList = evtListBuilder.ToEventList();
            AssertMemory(evtList.Data, rebuiltEvtList.Data);

            var rdtBuilder = rdt.ToBuilder();
            rdtBuilder.EventSCD = rebuiltEvtList;
            var rebuiltRdt = rdtBuilder.ToRdt();
            AssertMemory(rdt.Data, rebuiltRdt.Data);
        }

        [Fact]
        public void RE1_10B_ESP()
        {
            var assertEspData = new Action<Rdt1>(rdt1 =>
            {
                // Check ESP files are as expected
                var effectList = rdt1.EmbeddedEffects;
                Assert.Equal(0x03, effectList[0].Id);
                Assert.Equal(0x04, effectList[1].Id);
                Assert.Equal(0x20, effectList[2].Id);
                Assert.Equal(17731334930585642120U, effectList[0].Tim.Data.CalculateFnv1a());
                Assert.Equal(1363492920606414161U, effectList[1].Tim.Data.CalculateFnv1a());
                Assert.Equal(6968197342918359829U, effectList[2].Tim.Data.CalculateFnv1a());
                Assert.Equal(3831764485800859409U, effectList[0].Eff.Data.CalculateFnv1a());
                Assert.Equal(9618078066332305110U, effectList[1].Eff.Data.CalculateFnv1a());
                Assert.Equal(17638525937642694821U, effectList[2].Eff.Data.CalculateFnv1a());
            });

            var rdt = (Rdt1)GetRdt(BioVersion.Biohazard1, "ROOM10B0.RDT");
            assertEspData(rdt);

            // Reduce the total RDT size in some way
            var rdtBuilder = rdt.ToBuilder();
            rdtBuilder.InitSCD = new ScdProcedureContainer(new byte[] { 0, 0, 0, 0 });

            var newRdt = rdtBuilder.ToRdt();
            assertEspData(newRdt);
        }

        [Fact]
        public void RE1_100_Rebuild() => AssertRebuild(BioVersion.Biohazard1, "ROOM1000.RDT");

        [Fact]
        public void RE1_106_Rebuild() => AssertRebuild(BioVersion.Biohazard1, "ROOM1060.RDT");

        [Fact]
        public void RE1_All_Rebuild() => AssertRebuildAll(BioVersion.Biohazard1);

        [Fact]
        public void RE2_200()
        {
            var rdt = (Rdt2)GetRdt(BioVersion.Biohazard2, "ROOM2000.RDT");

            Assert.Equal(840, rdt.RVD.Length);                  // animation/anim.rbj
            Assert.Equal(112, rdt.BLK.Length);                  // block.blk
            Assert.Equal(512, rdt.RID.Data.Length);             // camera.rid
            Assert.Equal(832, rdt.SCA.Length);                  // collision.sca
            Assert.Equal(50, rdt.FLR.Length);                   // floor.flr
            Assert.Equal(640, rdt.LIT.Length);                  // light.lit
            Assert.Equal(153600, rdt.TIMSCROLL.Data.Length);    // scroll.tim
            Assert.Equal(6340, rdt.PRI.Length);                 // sprite.pri
            Assert.Equal(10744, rdt.RBJ.Data.Length);           // zone.rvd

            // Assert.Equal(8, rdt.ESPID.Length);
            // Assert.Equal(8, rdt.ESPEFF.Length);
            Assert.Equal(4224, rdt.EmbeddedEffects[0].Tim.Data.Length);
        }

        [Fact]
        public void RE2_100_Rebuild() => AssertRebuild(BioVersion.Biohazard2, "ROOM1000.RDT");

        [Fact]
        public void RE2_200_Rebuild() => AssertRebuild(BioVersion.Biohazard2, "ROOM2000.RDT");

        [Fact]
        public void RE2_All_Rebuild() => AssertRebuildAll(BioVersion.Biohazard2);

        [Fact]
        public void RE2_102_MSG()
        {
            var rdt = (Rdt2)GetRdt(BioVersion.Biohazard2, "ROOM1020.RDT");

            Assert.Equal(MsgLanguage.Japanese, rdt.MSGJA.Language);
            Assert.Equal(BioVersion.Biohazard2, rdt.MSGJA.Version);

            Assert.Equal(MsgLanguage.English, rdt.MSGEN.Language);
            Assert.Equal(BioVersion.Biohazard2, rdt.MSGEN.Version);

            var jaBuilder = new MsgList.Builder();
            jaBuilder.Messages.Add(new Msg(BioVersion.Biohazard2, MsgLanguage.Japanese, "This is a test."));

            var enBuilder = new MsgList.Builder();
            enBuilder.Messages.Add(new Msg(BioVersion.Biohazard2, MsgLanguage.English, "This is a test."));

            var rdtBuilder = rdt.ToBuilder();
            rdtBuilder.MSGJA = jaBuilder.ToMsgList();
            rdtBuilder.MSGEN = enBuilder.ToMsgList();
            var rebuiltRdt = rdtBuilder.ToRdt();

            var actual = rebuiltRdt.Data.CalculateFnv1a();
            Assert.Equal(14281979433154599006UL, actual);
        }

        [Fact]
        public void RE2_201_MSG()
        {
            var rdt = (Rdt2)GetRdt(BioVersion.Biohazard2, "ROOM2010.RDT");

            Assert.Equal(MsgLanguage.Japanese, rdt.MSGJA.Language);
            Assert.Equal(BioVersion.Biohazard2, rdt.MSGJA.Version);

            Assert.Equal(MsgLanguage.English, rdt.MSGEN.Language);
            Assert.Equal(BioVersion.Biohazard2, rdt.MSGEN.Version);

            Assert.Equal(8, rdt.MSGJA.Count);
            Assert.Equal(8, rdt.MSGEN.Count);

            var rdtBuilder = rdt.ToBuilder();
            rdtBuilder.MSGJA = rdt.MSGJA.ToBuilder().ToMsgList();
            rdtBuilder.MSGEN = rdt.MSGEN.ToBuilder().ToMsgList();
            var rebuilt = rdtBuilder.ToRdt();

            AssertMemory(rdt.Data, rebuilt.Data);
        }

        [Fact]
        public void RE2_20B_MSG()
        {
            var rdt = (Rdt2)GetRdt(BioVersion.Biohazard2, "ROOM20B1.RDT");

            Assert.Equal(MsgLanguage.Japanese, rdt.MSGJA.Language);
            Assert.Equal(BioVersion.Biohazard2, rdt.MSGJA.Version);

            Assert.Equal(MsgLanguage.English, rdt.MSGEN.Language);
            Assert.Equal(BioVersion.Biohazard2, rdt.MSGEN.Version);

            Assert.Equal(19, rdt.MSGJA.Count);
            Assert.Equal(19, rdt.MSGEN.Count);

            Assert.Equal("A {police station map}.\nWill you take it?@00", rdt.MSGEN[0].ToString());
            Assert.Equal("\nThe door won't open!\n", rdt.MSGEN[18].ToString());

            var rdtBuilder = rdt.ToBuilder();
            rdtBuilder.MSGJA = rdt.MSGJA.ToBuilder().ToMsgList();
            rdtBuilder.MSGEN = rdt.MSGEN.ToBuilder().ToMsgList();

            AssertMemory(rdt.MSGEN.Data, rdtBuilder.MSGEN.Data);
            AssertMemory(rdt.MSGJA.Data, rdtBuilder.MSGJA.Data);

            var rebuilt = rdtBuilder.ToRdt();
            AssertMemory(rdt.Data, rebuilt.Data);
        }

        [Fact]
        public void RE2_112_RBJ()
        {
            var rdt = (Rdt2)GetRdt(BioVersion.Biohazard2, "ROOM1121.RDT");
            var rdtBuilder = rdt.ToBuilder();

            var rbj = rdt.RBJ;
            var rbjBuilder = rbj.ToBuilder();
            rdtBuilder.RBJ = rbjBuilder.ToRbj();
            var rebuiltRdt = rdtBuilder.ToRdt();

            AssertMemory(rdt.RBJ.Data, rdtBuilder.RBJ.Data);
            AssertMemory(rdt.Data, rebuiltRdt.Data);
        }

        [Fact]
        public void RE2_112_SCD()
        {
            var rdt = (Rdt2)GetRdt(BioVersion.Biohazard2, "ROOM1121.RDT");
            var builder = rdt.ToBuilder();

            builder.SCDINIT = new ScdProcedureList(BioVersion.Biohazard2, new byte[] { 0x02, 0x00, 0x01, 0x00 });
            builder.SCDMAIN = new ScdProcedureList(BioVersion.Biohazard2, new byte[] { 0x04, 0x00, 0x06, 0x00, 0x01, 0x00, 0x01, 0x00 });
            var rebuilt = builder.ToRdt();

            var hash = rebuilt.Data.CalculateFnv1a();
            Assert.Equal(6902255125174090017UL, hash);
        }

        [Fact]
        public void RE2_101_SCD()
        {
            var rdt = (Rdt2)GetRdt(BioVersion.Biohazard2, "ROOM1010.RDT");
            var rdtBuilder = rdt.ToBuilder();

            rdtBuilder.SCDINIT = rdtBuilder.SCDINIT.ToBuilder().ToProcedureList();
            rdtBuilder.SCDMAIN = rdtBuilder.SCDMAIN.ToBuilder().ToProcedureList();
            var rebuiltRdt = rdtBuilder.ToRdt();

            AssertMemory(rdt.SCDINIT.Data, rdtBuilder.SCDINIT.Data);
            AssertMemory(rdt.SCDMAIN.Data, rdtBuilder.SCDMAIN.Data);
            AssertMemory(rdt.Data, rebuiltRdt.Data);
        }

        [Fact]
        public void RE3_All_Rebuild() => AssertRebuildAll(BioVersion.Biohazard3);

        [Fact]
        public void RE3_100()
        {
            AssertRebuild(BioVersion.Biohazard3, "R100.RDT");
        }

        [Fact]
        public void RE3_101()
        {
            AssertRebuild(BioVersion.Biohazard3, "R101.RDT");
        }

        [Fact]
        public void RE3_100_ETD()
        {
            var rdt = (Rdt2)GetRdt(BioVersion.Biohazard3, "R100.RDT");
            AssertRebuildEtd(rdt.ETD);
        }

        [Fact]
        public void RE3_101_ETD()
        {
            var rdt = (Rdt2)GetRdt(BioVersion.Biohazard3, "R101.RDT");
            AssertRebuildEtd(rdt.ETD);
        }

        [Fact]
        public void RE3_103_ETD()
        {
            var rdt = (Rdt2)GetRdt(BioVersion.Biohazard3, "R103.RDT");
            AssertRebuildEtd(rdt.ETD);
        }

        [Fact]
        public void RE3_201_ETD()
        {
            var rdt = (Rdt2)GetRdt(BioVersion.Biohazard3, "R201.RDT");
            AssertRebuildEtd(rdt.ETD);
        }

        [Fact]
        public void RE3_Rebuild_All_ETD()
        {
            var fileNames = GetAllRdtFileNames(BioVersion.Biohazard3);
            foreach (var fileName in fileNames)
            {
                var rdt = (Rdt2)GetRdt(BioVersion.Biohazard3, fileName);
                AssertRebuildEtd(rdt.ETD);
            }
        }

        private void AssertRebuildEtd(Etd unk)
        {
            var unkBuilder = unk.ToBuilder();
            var rebuilt = unkBuilder.ToEtd();
            AssertAndCompareMemory(unk.Data, rebuilt.Data);
        }

        private void AssertRebuildAll(BioVersion version)
        {
            var fail = false;
            var fileNames = GetAllRdtFileNames(version);
            foreach (var fileName in fileNames)
            {
                try
                {
                    AssertRebuild(version, fileName);
                }
                catch (Exception ex)
                {
                    fail = true;
                    _output.WriteLine($"{fileName}: FAIL {ex.Message}");
                }
            }
            Assert.False(fail);
        }

        private static void AssertRebuild(BioVersion version, string fileName)
        {
            var rdt = GetRdt(version, fileName);
            var rebuiltRdt = rdt.ToBuilder().ToRdt();
            AssertAndCompareMemory(rdt.Data, rebuiltRdt.Data);
        }
    }
}
