using System;
using IntelOrca.Biohazard.Extensions;
using IntelOrca.Biohazard.Model;
using IntelOrca.Biohazard.Room;

namespace IntelOrca.Biohazard.Script.Compilation
{
    public interface IRdtEditOperation
    {
        void Perform(IRdtBuilder target);
    }

    public class ScdRdtEditOperation : IRdtEditOperation
    {
        public BioScriptKind Kind { get; }
        public ScdProcedureContainer Container { get; }
        public ScdEventList Events { get; }
        public ScdProcedureList Data { get; }

        public ScdRdtEditOperation(BioScriptKind kind, ScdProcedureContainer container)
        {
            Kind = kind;
            Container = container;
        }

        public ScdRdtEditOperation(BioScriptKind kind, ScdEventList events)
        {
            Kind = kind;
            Events = events;
        }

        public ScdRdtEditOperation(BioScriptKind kind, ScdProcedureList data)
        {
            Kind = kind;
            Data = data;
        }

        public void Perform(IRdtBuilder target)
        {
            if (target is Rdt1.Builder builder1)
            {
                if (Kind == BioScriptKind.Init)
                {
                    builder1.InitSCD = Container;
                }
                else if (Kind == BioScriptKind.Main)
                {
                    builder1.MainSCD = Container;
                }
                else if (Kind == BioScriptKind.Event)
                {
                    builder1.EventSCD = Events;
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            else if (target is Rdt2.Builder builder2)
            {
                if (Kind == BioScriptKind.Init)
                {
                    builder2.SCDINIT = Data;
                }
                else if (Kind == BioScriptKind.Main)
                {
                    builder2.SCDMAIN = Data;
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            else
            {
                throw new NotSupportedException();
            }
        }
    }

    public class TextRdtEditOperation : IRdtEditOperation
    {
        public MsgLanguage Language { get; }
        public int Index { get; }
        public Msg Value { get; }

        public TextRdtEditOperation(MsgLanguage language, int index, Msg value)
        {
            Language = language;
            Index = index;
            Value = value;
        }

        public void Perform(IRdtBuilder target)
        {
            if (target is Rdt2.Builder builder2)
            {
                if (Language == MsgLanguage.Japanese)
                {
                    builder2.MSGJA = builder2.MSGJA.WithMessage(Index, Value);
                }
                else if (Language == MsgLanguage.English)
                {
                    builder2.MSGEN = builder2.MSGEN.WithMessage(Index, Value);
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            else
            {
                throw new NotSupportedException();
            }
        }
    }

    public class AnimationRdtEditOperation : IRdtEditOperation
    {
        public int Index { get; }
        public RbjAnimation Animation { get; }

        public AnimationRdtEditOperation(int index, in RbjAnimation animation)
        {
            Index = index;
            Animation = animation;
        }

        public void Perform(IRdtBuilder target)
        {
            if (target is Rdt2.Builder builder2)
            {
                builder2.RBJ = builder2.RBJ.WithAnimation(Index, Animation);
            }
            else
            {
                throw new NotSupportedException();
            }
        }
    }

    public class ObjectRdtEditOperation : IRdtEditOperation
    {
        public int Index { get; }
        public Md1 Model { get; }
        public Tim Texture { get; }

        public ObjectRdtEditOperation(int index, Md1 model, Tim texture)
        {
            Index = index;
            Model = model;
            Texture = texture;
        }

        public void Perform(IRdtBuilder target)
        {
            if (target is Rdt2.Builder builder2)
            {
                builder2.EmbeddedObjectMd1.Add(Model);
                builder2.EmbeddedObjectTim.Add(Texture);

                if (builder2.EmbeddedObjectModelTable.Count <= Index)
                {
                    builder2.EmbeddedObjectModelTable.Add(new ModelTextureIndex(-1, -1));
                    var header = builder2.Header;
                    header.nOmodel++;
                    builder2.Header = header;
                }

                builder2.EmbeddedObjectModelTable[Index] = new ModelTextureIndex(
                    builder2.EmbeddedObjectMd1.Count - 1,
                    builder2.EmbeddedObjectTim.Count - 1);
            }
            else
            {
                throw new NotSupportedException();
            }
        }
    }
}
