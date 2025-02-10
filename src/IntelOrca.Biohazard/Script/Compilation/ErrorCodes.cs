namespace IntelOrca.Biohazard.Script.Compilation
{
    internal class ErrorCodes
    {
        public const int ScdVersionNotSpecified = 1;
        public const int OpcodeNotInProcedure = 2;
        public const int LabelAlreadyDefined = 3;
        public const int UnknownOpcode = 4;
        public const int ExpectedOpcode = 5;
        public const int ExpectedComma = 6;
        public const int UnknownSymbol = 7;
        public const int TooManyOperands = 8;
        public const int IncorrectNumberOfOperands = 9;
        public const int UnknownLabel = 10;
        public const int InvalidOperator = 11;
        public const int ExpectedProcedureName = 12;
        public const int ExpectedScdVersionNumber = 13;
        public const int InvalidScdVersionNumber = 14;
        public const int ScdVersionAlreadySpecified = 15;
        public const int ProcedureNotValid = 16;
        public const int ScdTypeAlreadySpecified = 17;
        public const int UnknownDirective = 18;
        public const int ExpectedOperator = 19;
        public const int ProcedureNameAlreadyDefined = 20;
        public const int ExpectedOpenBlock = 21;
        public const int ExpectedCloseBlock = 22;
        public const int ExpectedOpenParen = 23;
        public const int ExpectedCloseParen = 24;
        public const int ExpectedSemicolon = 25;
        public const int ExpectedOperand = 26;
        public const int InvalidOperand = 27;
        public const int ExpectedCondition = 28;
        public const int InvalidExpression = 29;
        public const int UnknownProcedure = 30;
        public const int ExpectedPath = 31;
        public const int FileNotFound = 32;
        public const int ExpectedMacroName = 33;
        public const int MacroAlreadyDefined = 34;
        public const int InvalidMacroName = 35;
        public const int ParserFailure = 36;
        public const int ExpectedWhile = 37;
        public const int ExpectedColon = 38;
        public const int InvalidSyntax = 39;
        public const int RecursiveMacro = 40;
        public const int EventNotValid = 41;
        public const int ExpectedLabelName = 42;
        public const int FoundHashElifOutsideHashIf = 43;
        public const int FoundHashElseOutsideHashIf = 44;
        public const int FoundHashEndifOutsideHashIf = 45;
        public const int NoMatchingHashEndifForHashIf = 46;

        public static string GetMessage(int code) => _messages[code];

        private static readonly string[] _messages = new string[]
        {
                "",
                "SCD version must be specified before any procedure or opcode.",
                "Opcode must be inside a procedure.",
                "'{0}' has already been defined as a label.",
                "'{0}' it not a valid opcode.",
                "Expected opcode.",
                "Expected , after opcode.",
                "'{0}' has not been defined as a constant.",
                "Too many operands for this opcode.",
                "Incorrect number of operands for opcode.",
                "'{0}' has not been defined as a label within the same procedure.",
                "'{0}' is not a known or valid operator.",
                "Expected procedure name.",
                "Expected SCD version number.",
                "Invalid SCD version number. Only version 1, 2, and 3 are supported.",
                "SCD version already specified.",
                "Procedures are not valid in SCD version 1.",
                "SCD type already specified.",
                "'{0}' is not a valid directive.",
                "Expected operator.",
                "The name '{0}' has already been defined as a procedure.",
                "{{ expected",
                "}} expected",
                "( expected",
                ") expected",
                "; expected",
                "Operand expected",
                "Invalid operand",
                "Expected condition",
                "Invalid expression",
                "'{0}' has not been defined as a procedure.",
                "Expected quoted path.",
                "'{0}' could not found or read.",
                "Macro name expected.",
                "'{0}' has already been defined.",
                "'{0}' is an invalid name for a macro.",
                "The parser unexpectedly failed.",
                "while expected.",
                ": expected",
                "Invalid syntax",
                "Recursive macro",
                "Events are only valid in SCD version 1.",
                "Expected label name.",
                "#elif without matching #if directive",
                "#else without matching #if directive",
                "#endif without matching #if directive",
                "No matching #endif found for #if directive"
        };
    }
}
