using System;
using System.Collections.Generic;
using System.IO;
using IntelOrca.Biohazard.Model;
using IntelOrca.Biohazard.Room;

namespace IntelOrca.Biohazard.Rdt
{
    public class RdtPacker(BioVersion version, string hdrPath)
    {
        public string BasePath { get; } = Path.GetDirectoryName(hdrPath);
        public string TargetPath { get; } = Path.ChangeExtension(hdrPath, ".RDT");

        public void Pack()
        {
            var rdt2b = new Rdt2.Builder(version);
            rdt2b.RBJ = new Rbj(version, ReadFile("Animation/anim.rbj"));
            rdt2b.EmbeddedEffects = ReadEffs();
            rdt2b.ETD = new Etd(ReadFile("Effect/effect.etd"));
            rdt2b.EspTable = new EspTable(version, ReadFile("Effect/effect.tbl"));
            rdt2b.MSGJA = ReadMsgs(MsgLanguage.Japanese, "Message/main{0:00}.msg");
            rdt2b.MSGEN = ReadMsgs(MsgLanguage.English, "Message/sub{0:00}.msg");
            ReadObjects(rdt2b);
            rdt2b.SCDINIT = ReadScds("Script/main{0:00}.scd");
            rdt2b.SCDMAIN = ReadScds("Script/sub{0:00}.scd");
            rdt2b.EDT = ReadFile("Sound/snd0.edt");
            rdt2b.VB = ReadFile("Sound/snd0.vb");

            var vboffset = ReadFile("Sound/snd0.vbx");
            if (vboffset.Length != 0)
                rdt2b.VBOFFSET = BitConverter.ToInt32(vboffset, 0);

            rdt2b.VH = ReadFile("Sound/snd0.vh");
            rdt2b.BLK = ReadFile("block.blk");
            rdt2b.RID = new Rid(ReadFile("camera.rid"));
            rdt2b.SCA = ReadFile("collision.sca");
            rdt2b.FLR = ReadFile("floor.flr");

            var flt = ReadFile("floor.flt");
            if (flt.Length != 0)
                rdt2b.FLRTerminator = BitConverter.ToUInt16(flt, 0);

            rdt2b.LIT = ReadFile("light.lit");
            rdt2b.PRI = ReadFile("sprite.pri");
            rdt2b.RVD = ReadFile("zone.rvd");

            var header = ReadHeader(hdrPath);
            header.nOmodel = (byte)rdt2b.EmbeddedObjectModelTable.Count;
            rdt2b.Header = header;

            var rdt2 = rdt2b.ToRdt();
            File.WriteAllBytes(Path.Combine(BasePath, TargetPath), rdt2.Data.ToArray());
        }

        private Rdt2.Rdt2Header ReadHeader(string path)
        {
            using var ms = new MemoryStream(File.ReadAllBytes(hdrPath));
            var br = new BinaryReader(ms);
            return br.ReadStruct<Rdt2.Rdt2Header>();
        }

        private EmbeddedEffectList ReadEffs()
        {
            var list = new List<EmbeddedEffect>();
            for (byte id = 0; id < 255; id++)
            {
                var eff = ReadFile($"Effect/esp{id:X2}.eff");
                if (eff.Length == 0)
                    continue;

                var tim = ReadFile($"esp{id:X2}.eff");
                var embeddedEffect = new EmbeddedEffect(id, new Eff(eff), new Tim(tim));
                list.Add(embeddedEffect);
            }
            return new EmbeddedEffectList(version, list.ToArray());
        }

        private MsgList ReadMsgs(MsgLanguage lng, string fmt)
        {
            var builder = new MsgList.Builder();
            for (var i = 0; i < 100; i++)
            {
                var msg = ReadFile(string.Format(fmt, i));
                if (msg.Length == 0)
                    break;

                builder.Messages.Add(new Msg(version, lng, msg));
            }
            return builder.ToMsgList();
        }

        private void ReadObjects(Rdt2.Builder rdt2b)
        {
            var timHash = new List<ulong>();
            for (var i = 0; i < 100; i++)
            {
                var md1Data = ReadFile($"Object/object{i:00}.md1");
                var timData = ReadFile($"Object/object{i:00}.tim");
                if (md1Data.Length == 0 && timData.Length == 0)
                    break;

                var md1Index = 0;
                var timIndex = 0;
                if (md1Data.Length != 0)
                {
                    rdt2b.EmbeddedObjectMd1.Add(new Md1(md1Data));
                    md1Index = rdt2b.EmbeddedObjectMd1.Count - 1;
                }
                if (timData.Length != 0)
                {
                    var hash = timData.CalculateFnv1a();
                    timIndex = timHash.FindIndex(x => x == hash);
                    if (timIndex == -1)
                    {
                        rdt2b.EmbeddedObjectTim.Add(new Tim(timData));
                        timHash.Add(timData.CalculateFnv1a());
                        timIndex = rdt2b.EmbeddedObjectTim.Count - 1;
                    }
                }

                rdt2b.EmbeddedObjectModelTable.Add(new ModelTextureIndex(md1Index, timIndex));
            }
        }

        private ScdProcedureList ReadScds(string fmt)
        {
            var builder = new ScdProcedureList.Builder(version);
            for (var i = 0; i < 100; i++)
            {
                var scd = ReadFile(string.Format(fmt, i));
                if (scd.Length == 0)
                    break;

                builder.Procedures.Add(new ScdProcedure(version, scd));
            }
            return builder.ToProcedureList();
        }

        private byte[] ReadFile(string relativePath)
        {
            var fullPath = Path.Combine(BasePath, relativePath);
            if (File.Exists(fullPath))
            {
                return File.ReadAllBytes(fullPath);
            }
            return [];
        }
    }
}
