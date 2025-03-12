using System.Collections.Generic;
using System.Linq;
using IntelOrca.Biohazard.Room;

namespace IntelOrca.Biohazard.Script.Compilation
{
    public partial class ScdAssembler : IScdGenerator
    {
        private const int EvtBlockSubOpcode = -2;
        private const int UnkOpcode = -1;

        private const int OperandStateNone = 0;
        private const int OperandStateValue = 1;
        private const int OperandStateOr = 2;
        private const int OperandStateAdd = 3;
        private const int OperandStateSubtract = 4;

        private readonly List<IRdtEditOperation> _editOperations = new List<IRdtEditOperation>();
        private IConstantTable _constantTable = new Bio1ConstantTable();

        private ParserState _state;
        private ParserState _restoreState;
        private Dictionary<string, int> _procNames = new Dictionary<string, int>();
        private List<(string, int)> _labels = new List<(string, int)>();
        private List<LabelReference> _labelReferences = new List<LabelReference>();
        private List<byte> _procData = new List<byte>();
        private List<byte[]> _procedures = new List<byte[]>();
        private int _currentOpcodeOffset;
        private int _currentOpcodeLength;
        private int _currentOpcode;
        private string _currentOpcodeSignature = "";
        private bool _currentOpcodeIsEvent;
        private int _signatureIndex;
        private BioVersion? _version;
        private BioScriptKind? _currScriptKind;
        private BioScriptKind? _lastScriptKind;
        private int _operandState;
        private int _operandValue;

        public ErrorList Errors { get; } = new ErrorList();
        public IRdtEditOperation[] Operations => _editOperations.ToArray();

        public int Generate(IFileIncluder includer, string path)
        {
            var lexer = new Lexer(includer, Errors);
            var tokens = lexer.GetTokens(path).ToArray();
            if (Errors.ErrorCount != 0)
                return 1;

            _procNames = GetAllProcedureNames(tokens);
            _state = ParserState.Default;
            foreach (var token in tokens)
            {
                if (_state == ParserState.Terminate)
                {
                    break;
                }
                else if (_state == ParserState.SkipToNextLine)
                {
                    if (token.Kind != TokenKind.NewLine)
                    {
                        continue;
                    }
                    _state = _restoreState;
                }
                ProcessToken(in token);
            }
            EndScript();

            if (Errors.ErrorCount == 0 && _version == null)
            {
                Errors.AddError(path, 0, 0, ErrorCodes.ExpectedScdVersionNumber, ErrorCodes.GetMessage(ErrorCodes.ExpectedScdVersionNumber));
            }
            return Errors.ErrorCount == 0 ? 0 : 1;
        }

        private void ProcessToken(in Token token)
        {
            if (token.Kind == TokenKind.Whitespace)
                return;

            switch (_state)
            {
                case ParserState.Default:
                    if (token.Kind == TokenKind.Directive)
                    {
                        ProcessDirective(in token);
                    }
                    break;
                case ParserState.ExpectVersion:
                    if (token.Kind != TokenKind.Number)
                    {
                        EmitError(in token, ErrorCodes.ExpectedScdVersionNumber);
                        _state = ParserState.SkipToNextLine;
                        _restoreState = ParserState.Default;
                    }
                    else
                    {
                        if (!int.TryParse(token.Text, out var num) || num < 1 || num > 3)
                        {
                            EmitError(in token, ErrorCodes.InvalidScdVersionNumber);
                            _state = ParserState.SkipToNextLine;
                            _restoreState = ParserState.Default;
                        }
                        else
                        {
                            switch (num)
                            {
                                case 1:
                                    _version = BioVersion.Biohazard1;
                                    _constantTable = new Bio1ConstantTable();
                                    break;
                                case 2:
                                    _version = BioVersion.Biohazard2;
                                    _constantTable = new Bio2ConstantTable();
                                    break;
                                case 3:
                                    _version = BioVersion.Biohazard3;
                                    _constantTable = new Bio3ConstantTable();
                                    break;
                            }
                            _state = num == 1 ? ParserState.ExpectOpcode : ParserState.Default;
                        }
                    }
                    break;
                case ParserState.ExpectProcName:
                    if (token.Kind != TokenKind.Symbol)
                    {
                        EmitError(in token, ErrorCodes.ExpectedProcedureName);
                        _state = ParserState.SkipToNextLine;
                        _restoreState = ParserState.Default;
                    }
                    else
                    {
                        _state = ParserState.ExpectOpcode;
                    }
                    break;
                case ParserState.ExpectOpcode:
                    if (token.Kind == TokenKind.Directive)
                    {
                        ProcessDirective(in token);
                    }
                    else if (token.Kind == TokenKind.Label)
                    {
                        if (!AddLabel(token.Text.Substring(0, token.Text.Length - 1)))
                        {
                            EmitError(in token, ErrorCodes.LabelAlreadyDefined, token.Text);
                        }
                    }
                    else if (token.Kind == TokenKind.Opcode)
                    {
                        if (BeginOpcode(token.Text))
                        {
                            _state = ParserState.ExpectOperand;
                        }
                        else
                        {
                            EmitError(in token, ErrorCodes.UnknownOpcode, token.Text);
                            _state = ParserState.SkipToNextLine;
                            _restoreState = ParserState.ExpectOpcode;
                        }
                        break;
                    }
                    else if (!TokenIsEndOfLine(token))
                    {
                        EmitError(in token, ErrorCodes.ExpectedOpcode);
                        _state = ParserState.SkipToNextLine;
                        _restoreState = ParserState.ExpectOpcode;
                    }
                    break;
                case ParserState.ExpectOperand:
                    if (token.Kind == TokenKind.Number)
                    {
                        AddOperandNumber(token);
                        _state = ParserState.ExpectCommaOrOperator;
                    }
                    else if (token.Kind == TokenKind.Symbol)
                    {
                        AddOperandSymbol(token);
                        _state = ParserState.ExpectCommaOrOperator;
                    }
                    else if (TokenIsEndOfLine(token))
                    {
                        EndCurrentOpcode(in token);
                        _state = ParserState.ExpectOpcode;
                    }
                    break;
                case ParserState.ExpectCommaOrOperator:
                    if (token.Kind == TokenKind.Plus)
                    {
                        _operandState = OperandStateAdd;
                        _state = ParserState.ExpectOperand;
                    }
                    else if (token.Kind == TokenKind.Minus)
                    {
                        _operandState = OperandStateSubtract;
                        _state = ParserState.ExpectOperand;
                    }
                    else if (token.Kind == TokenKind.Pipe)
                    {
                        _operandState = OperandStateOr;
                        _state = ParserState.ExpectOperand;
                    }
                    else if (token.Kind == TokenKind.Comma)
                    {
                        EndOperand();
                        _state = ParserState.ExpectOperand;
                    }
                    else if (TokenIsEndOfLine(token))
                    {
                        EndCurrentOpcode(in token);
                        _state = ParserState.ExpectOpcode;
                    }
                    else
                    {
                        EmitError(in token, ErrorCodes.ExpectedComma);
                        _state = ParserState.SkipToNextLine;
                        _restoreState = ParserState.ExpectOpcode;
                    }
                    break;
            }
        }

        private void ProcessDirective(in Token token)
        {
            switch (token.Text)
            {
                case ".version":
                    if (_version != null)
                    {
                        EmitError(in token, ErrorCodes.ScdVersionAlreadySpecified);
                        _state = ParserState.SkipToNextLine;
                        _restoreState = ParserState.Default;
                    }
                    _state = ParserState.ExpectVersion;
                    break;
                case ".init":
                    if (_currScriptKind == BioScriptKind.Init ||
                        _lastScriptKind == BioScriptKind.Init)
                    {
                        EmitError(in token, ErrorCodes.ScdTypeAlreadySpecified);
                    }
                    else
                    {
                        ChangeScriptKind(BioScriptKind.Init);
                    }
                    break;
                case ".main":
                    if (_currScriptKind == BioScriptKind.Main ||
                        _lastScriptKind == BioScriptKind.Main)
                    {
                        EmitError(in token, ErrorCodes.ScdTypeAlreadySpecified);
                    }
                    else
                    {
                        ChangeScriptKind(BioScriptKind.Main);
                    }
                    break;
                case ".proc":
                    if (_version == null)
                    {
                        EmitError(in token, ErrorCodes.ScdVersionNotSpecified);
                        _state = ParserState.Terminate;
                    }
                    else if (_version == BioVersion.Biohazard1)
                    {
                        EmitError(in token, ErrorCodes.ProcedureNotValid);
                        _state = ParserState.SkipToNextLine;
                        _restoreState = ParserState.Default;
                    }
                    else
                    {
                        if (_procedures.Count != 0 || _procData.Count != 0)
                            EndProcedure();
                        _state = ParserState.ExpectProcName;
                    }
                    break;
                case ".event":
                    if (_version != BioVersion.Biohazard1)
                    {
                        EmitError(in token, ErrorCodes.EventNotValid);
                        _state = ParserState.SkipToNextLine;
                        _restoreState = ParserState.Default;
                    }
                    else
                    {
                        if (_currScriptKind != BioScriptKind.Event)
                        {
                            ChangeScriptKind(BioScriptKind.Event);
                        }
                        else
                        {
                            if (_procedures.Count != 0 || _procData.Count != 0)
                                EndProcedure();
                        }
                        _state = ParserState.ExpectProcName;
                    }
                    break;
                default:
                    EmitError(in token, ErrorCodes.UnknownDirective, token.Text);
                    _restoreState = _state;
                    _state = ParserState.SkipToNextLine;
                    break;
            }
        }

        private void BeginScript()
        {
            _procedures.Clear();
            _labels.Clear();
            _labelReferences.Clear();
            _procData.Clear();
            if (_version == BioVersion.Biohazard1)
            {
                if (_currScriptKind != BioScriptKind.Event)
                {
                    _procData.Add(0);
                    _procData.Add(0);
                }
            }
        }

        private void EndScript()
        {
            if (_currScriptKind == null)
                return;

            if (Errors.ErrorCount == 0)
            {
                if (_version == BioVersion.Biohazard1)
                {
                    EndProcedure();
                    if (_currScriptKind != BioScriptKind.Event)
                    {
                        var procData = _procedures[0].ToList();
                        var procLength = procData.Count;
                        if (procLength <= 2)
                        {
                            // Empty script
                            procLength = 0;
                        }

                        procData[0] = (byte)(procLength & 0xFF);
                        procData[1] = (byte)(procLength >> 8);
                        procData.Add(0);
                        while ((procData.Count & 3) != 0)
                        {
                            procData.Add(0);
                        }

                        var output = procData.ToArray();
                        var container = new ScdProcedureContainer(output);
                        _editOperations.Add(new ScdRdtEditOperation(_currScriptKind.Value, container));
                    }
                    else
                    {
                        var b = new Room.ScdEventList.Builder();
                        foreach (var p in _procedures)
                        {
                            b.Events.Add(new ScdEvent(p));
                        }
                        _editOperations.Add(new ScdRdtEditOperation(_currScriptKind.Value, b.ToEventList()));
                    }
                }
                else
                {
                    EndProcedure();

                    var offset = _procedures.Count * 2;
                    foreach (var p in _procedures)
                    {
                        WriteUint16((ushort)offset);
                        offset += p.Length;
                    }
                    foreach (var p in _procedures)
                    {
                        _procData.AddRange(p);
                    }

                    var output = _procData.ToArray();
                    _editOperations.Add(new ScdRdtEditOperation(_currScriptKind.Value, new Room.ScdProcedureList(_version!.Value, output)));
                }
            }
        }

        private void ChangeScriptKind(BioScriptKind kind)
        {
            EndScript();
            _lastScriptKind = _currScriptKind;
            _currScriptKind = kind;
            BeginScript();
        }

        private void EndProcedure()
        {
            FixLabelReferences();
            _procedures.Add(_procData.ToArray());
            _labels.Clear();
            _labelReferences.Clear();
            _procData.Clear();
        }

        private Dictionary<string, int> GetAllProcedureNames(Token[] tokens)
        {
            var names = new Dictionary<string, int>();
            var index = 0;
            var state = 0;
            foreach (var token in tokens)
            {
                switch (token.Kind)
                {
                    case TokenKind.Whitespace:
                        break;
                    case TokenKind.Directive:
                        if (token.Text == ".proc")
                            state = 1;
                        else
                            state = 0;
                        if (token.Text == ".init" || token.Text == ".main")
                            index = 0;
                        break;
                    case TokenKind.Symbol:
                        if (state == 1)
                        {
                            if (names.ContainsKey(token.Text))
                            {
                                EmitError(in token, ErrorCodes.ProcedureNameAlreadyDefined, token.Text);
                            }
                            else
                            {
                                names.Add(token.Text, index);
                                index++;
                            }
                            state = 0;
                        }
                        break;
                    default:
                        state = 0;
                        break;
                }
            }
            return names;
        }

        private bool AddLabel(string name)
        {
            if (_labels.Any(x => x.Item1 == name))
                return false;

            _labels.Add((name, _procData.Count));
            return true;
        }

        private void RecordLabelReference(int baseAddress, int size, in Token token)
        {
            _labelReferences.Add(new LabelReference(_procData.Count, (byte)size, baseAddress, token));
        }

        private bool BeginOpcode(string name)
        {
            if (_currScriptKind == null)
                _currScriptKind = BioScriptKind.Init;

            _currentOpcodeOffset = _procData.Count;
            _signatureIndex = 0;
            if (name == "unk" || name == "db")
            {
                _currentOpcode = UnkOpcode;
                _currentOpcodeLength = 0;
                return true;
            }
            else if (name == "evt_block_sub")
            {
                _currentOpcode = EvtBlockSubOpcode;
                _currentOpcodeLength = 2;
                _currentOpcodeSignature = "U";
                return true;
            }

            byte? eventOpcode = null;
            if (_currScriptKind == BioScriptKind.Event)
            {
                eventOpcode = _constantTable.FindOpcode(name, isEventOpcode: true);
            }

            var opcode = eventOpcode ?? _constantTable.FindOpcode(name, isEventOpcode: false);
            if (opcode == null)
            {
                return false;
            }
            _currentOpcodeIsEvent = eventOpcode != null;
            _currentOpcode = opcode.Value;
            _currentOpcodeLength = _constantTable.GetInstructionSize(opcode.Value, null, _currentOpcodeIsEvent);
            _currentOpcodeSignature = _constantTable.GetOpcodeSignature(opcode.Value, _currentOpcodeIsEvent);
            var colonIndex = _currentOpcodeSignature.IndexOf(':');
            if (colonIndex == -1)
            {
                var length = _currentOpcodeLength;
                if (length != 0)
                {
                    if (_currentOpcodeSignature != "")
                        length--;
                    _currentOpcodeSignature = new string('u', length);
                }
            }
            else
            {
                _currentOpcodeSignature = _currentOpcodeSignature.Substring(colonIndex + 1);
            }
            WriteUInt8(opcode.Value);
            return true;
        }

        private void AddOperandNumber(in Token token)
        {
            int num;
            if (token.Text.StartsWith("0x"))
            {
                num = int.Parse(token.Text.Substring(2), System.Globalization.NumberStyles.HexNumber);
            }
            else
            {
                num = int.Parse(token.Text);
            }
            AddOperandNumber(in token, num);
        }

        private void AddOperandNumber(in Token token, int num)
        {
            if (!CheckOperandLength(in token))
                return;

            switch (_operandState)
            {
                case OperandStateNone:
                    _operandValue = num;
                    break;
                case OperandStateValue:
                    EmitError(in token, ErrorCodes.ExpectedOperator);
                    break;
                case OperandStateOr:
                    _operandValue |= num;
                    break;
                case OperandStateAdd:
                    _operandValue += num;
                    break;
                case OperandStateSubtract:
                    _operandValue -= num;
                    break;
            }
            _operandState = OperandStateValue;
        }

        private void EndOperand()
        {
            if (_operandState == 0)
                return;

            if (_currentOpcode == UnkOpcode)
            {
                WriteUInt8((byte)_operandValue);
            }
            else
            {
                var arg = _currentOpcodeSignature[_signatureIndex];
                if (arg == 'I')
                {
                    WriteInt16((short)_operandValue);
                }
                else if (arg == 'L' || arg == 'U' || arg == '@' || arg == '~')
                {
                    WriteUint16((ushort)_operandValue);
                }
                else if (char.IsUpper(arg))
                {
                    WriteUint16((ushort)_operandValue);
                }
                else
                {
                    WriteUInt8((byte)_operandValue);
                }
                _signatureIndex++;
            }

            _operandState = 0;
            _operandValue = 0;
        }

        private void AddOperandSymbol(in Token token)
        {
            if (!CheckOperandLength(in token))
                return;

            var arg = _currentOpcodeSignature[_signatureIndex];
            switch (arg)
            {
                case 'l':
                case 'L':
                case '\'':
                case '@':
                case '~':
                    var baseAddress = _currentOpcodeOffset;
                    if (arg == 'L' || arg == '\'')
                        baseAddress += _currentOpcodeLength;

                    var size = arg == 'l' || arg == '\'' ? 1 : 2;
                    RecordLabelReference(baseAddress, size, in token);
                    AddOperandNumber(in token, 0);
                    break;
                default:
                    int? value;
                    if (_procNames.TryGetValue(token.Text, out var procIndex))
                        value = procIndex;
                    else
                        value = _constantTable.GetConstantValue(token.Text);
                    if (value == null)
                    {
                        EmitError(in token, ErrorCodes.UnknownSymbol, token.Text);
                        AddOperandNumber(in token, 0);
                    }
                    else
                    {
                        AddOperandNumber(in token, value.Value);
                    }
                    break;
            }
        }

        private bool CheckOperandLength(in Token token)
        {
            if (_currentOpcode != UnkOpcode && _signatureIndex > _currentOpcodeSignature.Length)
            {
                EmitError(in token, ErrorCodes.TooManyOperands);
                return false;
            }
            return true;
        }

        private bool EndCurrentOpcode(in Token token)
        {
            EndOperand();
            if (_currentOpcodeLength != 0 && _signatureIndex != _currentOpcodeSignature.Length)
            {
                EmitError(in token, ErrorCodes.IncorrectNumberOfOperands);
                return false;
            }
            return true;
        }

        private void FixLabelReferences()
        {
            foreach (var labelReference in _labelReferences)
            {
                var token = labelReference.Token;
                var labelIndex = _labels.FindIndex(x => x.Item1 == token.Text);
                if (labelIndex == -1)
                {
                    EmitError(in token, ErrorCodes.UnknownLabel, token.Text);
                }
                else
                {
                    var labelAddress = _labels[labelIndex].Item2;
                    var offset = labelReference.WriteOffset;
                    var value = labelAddress - labelReference.BaseAddress;
                    // TODO Check range
                    if (labelReference.WriteLength == 1)
                    {
                        _procData[offset] = (byte)(_procData[offset] + value);
                    }
                    else
                    {
                        var customOffset = (short)(_procData[offset + 0] | _procData[offset + 1] << 8);
                        var updatedValue = (short)(value + customOffset);
                        _procData[offset + 0] = (byte)(updatedValue & 0xFF);
                        _procData[offset + 1] = (byte)(updatedValue >> 8 & 0xFF);
                    }
                }
            }
        }

        private static bool TokenIsEndOfLine(in Token token)
        {
            return token.Kind == TokenKind.EOF || token.Kind == TokenKind.Comment || token.Kind == TokenKind.NewLine;
        }

        private void EmitError(in Token token, int code, params object[] args)
        {
            Errors.AddError(token.Path, token.Line, token.Column, code, string.Format(ErrorCodes.GetMessage(code), args));
        }

        private void EmitWarning(in Token token, int code, params object[] args)
        {
            Errors.AddWarning(token.Path, token.Line, token.Column, code, string.Format(ErrorCodes.GetMessage(code), args));
        }

        private void WriteUInt8(byte b)
        {
            _procData.Add(b);
        }

        private void WriteInt16(short s)
        {
            _procData.Add((byte)(s & 0xFF));
            _procData.Add((byte)(s >> 8));
        }

        private void WriteUint16(ushort s)
        {
            _procData.Add((byte)(s & 0xFF));
            _procData.Add((byte)(s >> 8));
        }

        private readonly struct LabelReference
        {
            public int WriteOffset { get; }
            public byte WriteLength { get; }
            public int BaseAddress { get; }
            public Token Token { get; }

            public LabelReference(int writeOffset, byte writeLength, int baseAddress, Token token)
            {
                WriteOffset = writeOffset;
                WriteLength = writeLength;
                BaseAddress = baseAddress;
                Token = token;
            }
        }

        private enum ParserState
        {
            Default,
            Terminate,
            SkipToNextLine,
            ExpectVersion,
            ExpectProcName,
            ExpectOpcode,
            ExpectOperand,
            ExpectCommaOrOperator,
        }
    }
}
