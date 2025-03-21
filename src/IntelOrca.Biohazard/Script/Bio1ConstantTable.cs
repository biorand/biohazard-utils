using System;
using System.Collections.Generic;
using System.IO;

namespace IntelOrca.Biohazard.Script
{
    public class Bio1ConstantTable : IConstantTable
    {
        private const byte SCE_MESSAGE = 2;
        private const byte SCE_ITEMBOX = 8;
        private const byte SCE_EVENT = 9;
        private const byte SCE_SAVE = 10;
        private const byte SCE_HIKIDASHI = 14;

        private static readonly SortedDictionary<string, int> _constantMap = new SortedDictionary<string, int>();

        public string GetEnemyName(byte kind) => g_enemyNames.Namify("ENEMY_", kind);
        public string GetItemName(byte kind) => g_itemNames.Namify("ITEM_", kind);
        private string GetKeyName(byte value)
        {
            return value switch
            {
                0 => "UNLOCKED",
                254 => "UNLOCK",
                255 => "LOCKED",
                _ => GetItemName(value)
            };
        }
        private string GetRdtName(byte target)
        {
            var stage = (byte)(target >> 5);
            var room = (byte)(target & 0b11111);
            return $"RDT_{stage:X}{room:X2}";
        }

        public string GetOpcodeSignature(byte opcode, bool isEventOpcode)
        {
            var table = isEventOpcode ? _eventOpcodes : _opcodes;
            if (opcode < table.Length)
                return table[opcode];
            return "";
        }

        public string? GetConstant(byte opcode, int pIndex, BinaryReader reader)
        {
            using (var br = reader.Fork())
            {
                if (opcode == (byte)OpcodeV1.AotSet)
                {
                    if (pIndex == 9)
                    {
                        br.BaseStream.Position += 9;
                        var sce = br.ReadByte();
                        if (sce == SCE_EVENT)
                        {
                            br.BaseStream.Position += 3;
                            return GetConstant('p', br.ReadByte());
                        }
                    }
                }
                else if (opcode == (byte)OpcodeV1.AotReset)
                {
                    if (pIndex == 4)
                    {
                        br.BaseStream.Position += 1;
                        var sce = br.ReadByte();
                        if (sce == SCE_EVENT)
                        {
                            br.BaseStream.Position += 3;
                            return GetConstant('p', br.ReadByte());
                        }
                    }
                }
            }
            return null;
        }

        public string? GetConstant(char kind, int value)
        {
            return kind switch
            {
                'e' => GetEnemyName((byte)value),
                't' => GetKeyName((byte)value),
                's' => GetConstantName(g_sceNames, value),
                'p' => $"event_{value:X2}",
                'r' => GetRdtName((byte)value),
                'w' => GetConstantName(g_wkNames, value),
                _ => null
            };
        }

        private string? GetConstantName(string?[] table, int value)
        {
            if (value >= 0 && value < table.Length)
                return table[value];
            return null;
        }

        public int? GetConstantValue(string symbol)
        {
            if (_constantMap.Count == 0)
            {
                _constantMap.Add("I_GOSUB", (byte)OpcodeV2.Gosub);
                var constChars = new char[]
                {
                    'e', 'i', 's', 'p', 'r', 't', 'w',
                };
                foreach (var ch in constChars)
                {
                    for (var i = 0; i < 256; i++)
                    {
                        var name = GetConstant(ch, i);
                        if (name != null && !name.Contains(" ") && !char.IsNumber(name[0]))
                        {
                            if (_constantMap.TryGetValue(name, out var check) && check != i)
                                throw new InvalidOperationException();
                            else
                                _constantMap[name] = i;
                        }
                    }
                }
            }
            if (!_constantMap.TryGetValue(symbol, out var value))
                return null;
            return value;
        }

        public int GetInstructionSize(byte opcode, BinaryReader? br, bool isEventOpcode = false)
        {
            if (isEventOpcode)
            {
                return (Re1EventOpcode)opcode switch
                {
                    Re1EventOpcode.Unk03 => 3,
                    Re1EventOpcode.WorkSet => 3,
                    Re1EventOpcode.Fork => 4,
                    Re1EventOpcode.Block => 2,
                    Re1EventOpcode.Single => 2,
                    Re1EventOpcode.Unk08 => 2,
                    Re1EventOpcode.Disable => 2,

                    (Re1EventOpcode)0x80 => 1,
                    (Re1EventOpcode)0x81 => 2,
                    (Re1EventOpcode)0x83 => 8,
                    (Re1EventOpcode)0x84 => 4,
                    (Re1EventOpcode)0x85 => 4,
                    (Re1EventOpcode)0x87 => 4,
                    (Re1EventOpcode)0x88 => 4,

                    (Re1EventOpcode)0xE8 => 4,

                    Re1EventOpcode.Sleep => 4,
                    Re1EventOpcode.For => 4,
                    Re1EventOpcode.Do => 2,
                    Re1EventOpcode.Finish => 2,
                    _ => 1,
                };
            }
            else
            {

                if (opcode >= _instructionSizes1.Length)
                    return 0;
                return _instructionSizes1[opcode];
            }
        }

        public byte? FindOpcode(string name, bool isEventOpcode)
        {
            var table = isEventOpcode ? _eventOpcodes : _opcodes;
            for (int i = 0; i < table.Length; i++)
            {
                var signature = table[i];
                if (signature == "")
                    continue;

                var opcodeName = signature;
                var colonIndex = signature.IndexOf(':');
                if (colonIndex != -1)
                    opcodeName = signature.Substring(0, colonIndex);

                if (name == opcodeName)
                    return (byte)i;
            }
            return null;
        }

        public bool IsOpcodeCondition(byte opcode)
        {
            switch ((OpcodeV1)opcode)
            {
                case OpcodeV1.Ck:
                case OpcodeV1.Cmp6:
                case OpcodeV1.Cmp7:
                case OpcodeV1.TestItem:
                case OpcodeV1.TestPickup:
                case OpcodeV1.ItemCk:
                    return true;
            }
            return false;
        }

        public string? GetNamedFlag(int obj, int index)
        {
            return null;
        }

        public string? GetNamedVariable(int index)
        {
            return null;
        }

        private string[] g_enemyNames = new string[]
        {
            "Zombie (Groundskeeper)",
            "Zombie (Naked)",
            "Cerberus",
            "Web Spinner",
            "Black Tiger",
            "Crow",
            "Hunter",
            "Wasp",
            "Plant 42",
            "Chimera",
            "Adder",
            "Neptune",
            "Tyrant 1",
            "Yawn 1",
            "Plant42 (roots)",
            "Fountain Plant",
            "Tyrant 2",
            "Zombie (Researcher)",
            "Yawn 2",
            "Cobweb",
            "Computer Hands (left)",
            "Computer Hands (right)",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "Chris (Stars)",
            "Jill (Stars)",
            "Barry (Stars)",
            "Rebecca (Stars)",
            "Wesker (Stars)",
            "Kenneth 1",
            "Forrest",
            "Richard",
            "Enrico",
            "Kenneth 2",
            "Barry 2",
            "Barry 2 (Stars)",
            "Rebecca 2 (Stars)",
            "Barry 3",
            "Wesker 2 (Stars)",
            "Chris (Jacket)",
            "Jill (Black Shirt)",
            "Chris 2 (Jacket)",
            "Jill (Red Shirt)",
        };

        private string[] g_itemNames = new string[]
        {
            "Nothing",
            "Combat Knife",
            "Beretta",
            "Shotgun",
            "DumDum Colt",
            "Colt Python",
            "FlameThrower",
            "Bazooka Acid",
            "Bazooka Explosive",
            "Bazooka Flame",
            "Rocket Launcher",
            "Clip",
            "Shells",
            "DumDum Rounds",
            "Magnum Rounds",
            "FlameThrower Fuel",
            "Explosive Rounds",
            "Acid Rounds",
            "Flame Rounds",
            "Empty Bottle",
            "Water",
            "Umb No. 2",
            "Umb No. 4",
            "Umb No. 7",
            "Umb No. 13",
            "Yellow 6",
            "NP-003",
            "V-Jolt",
            "Broken Shotgun",
            "Square Crank",
            "Hex Crank",
            "Wood Emblem",
            "Gold Emblem",
            "Blue Jewel",
            "Red Jewel",
            "Music Notes",
            "Wolf Medal",
            "Eagle Medal",
            "Chemical",
            "Battery",
            "MO Disk",
            "Wind Crest",
            "Flare",
            "Slides",
            "Moon Crest",
            "Star Crest",
            "Sun Crest",
            "Ink Ribbon",
            "Lighter",
            "Lock Pick",
            "Nameless (Can of Oil)",
            "Sword Key",
            "Armor Key",
            "Sheild Key",
            "Helmet Key",
            "Lab Key (1)",
            "Special Key",
            "Dorm Key (002)",
            "Dorm Key (003)",
            "C. Room Key",
            "Lab Key (2)",
            "Small Key",
            "Red Book",
            "Doom Book (2)",
            "Doom Book (1)",
            "F-Aid Spray",
            "Serum",
            "Red Herb",
            "Green Herb",
            "Blue Herb",
            "Mixed (Red+Green)",
            "Mixed (2 Green)",
            "Mixed (Blue + Green)",
            "Mixed (All)",
            "Mixed (Silver Color)",
            "Mixed (Bright Blue-Green)"
        };

        private string[] _opcodes = new string[]
        {
            "end:u",
            "if:l",
            "else:l",
            "endif:u",
            "ck:uuu",
            "set:uuu",
            "cmpb:uuu",
            "cmpw:uuuI",
            "setb:uuu",
            "cutnext:u",
            "cutcurr:u",
            "",
            "door_aot_set:uIIIIuuuuurIIIItu",
            "aot_set:uIIIIsuuuuuuu",
            "nop:u",
            "",
            "testitem:t",
            "testpickup:t",
            "aot_reset:usuIII",
            "aot_delete",
            "evt_exec:uup",
            "bgm_play:u",
            "bgm_stop:u",
            "",
            "item_aot_set:uIIIItuuuuuuuuuuuuuuu",
            "setbyte:uuu",
            "item_ck:t",
            "enemy:euuuuuuIuuIIIuuuu",
            "",
            "",
            "xa_on",
            "obj:uuuIIIIuuuuuuuuuuuuuuuu",
            "dir_set:uIIIIII",
            "pos_set:uIIIIII",
            "",
            "cut_auto",
            "aot_on",
            "",
            "",
            "",
            "",
            "movie_on:u",
            "effect",
            "",
            "remove_item:t",
            "give_item:uuu",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "", // last
        };

        private string[] _eventOpcodes = new string[]
        {
            "evt_nop",
            "evt_01",
            "evt_02",
            "evt_03",
            "evt_work_set:wu",
            "evt_fork:upu",
            "evt_block:u",
            "evt_single:u",
            "evt_08",
            "evt_disable:u",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "",
            "evt_plc_dest",
            "evt_plc_motion",
            "",
            "evt_plc_ret",
            "",
            "evt_plc_rotate",
            "",
            "evt_F6",
            "evt_F7",
            "evt_sleep",
            "evt_F9",
            "evt_for:uU",
            "evt_fornext",
            "evt_do:u",
            "evt_dountil",
            "evt_next",
            "evt_finish"
        };

        private static int[] _instructionSizes1 = new int[]
        {
            2, 2, 2, 2, 4, 4, 4, 6, 4, 2, 2, 4, 26, 18, 2, 8,
            2, 2, 10, 4, 4, 2, 2, 10, 26, 4, 2, 22, 6, 2, 4, 28,
            14, 14, 4, 2, 4, 4, 0, 2, 4 + 2, 2, 12, 4, 2, 4, 0, 4,
            12, 4, 4, 4 + 0, 8, 4, 4, 4, 4, 2, 4, 6, 6, 12, 2, 6,
            16, 4, 4, 4, 2, 2, 44 + 0, 14, 2, 2, 2, 2, 4, 2, 4, 2,
            2
        };

        private static readonly string[] g_sceNames = new string[] {
            "SCE_0",
            "SCE_1",
            "SCE_MESSAGE",
            "SCE_3",
            "SCE_4",
            "SCE_5",
            "SCE_6",
            "SCE_7",
            "SCE_ITEMBOX",
            "SCE_EVENT",
            "SCE_SAVE",
            "SCE_11",
            "SCE_12",
            "SCE_13",
            "SCE_HIKIDASHI",
        };

        private static readonly string[] g_wkNames = new string[] {
            "WK_PLAYER",
            "WK_ENEMY",
            "WK_OBJ",
            "WK_AOT",
        };
    }
}
