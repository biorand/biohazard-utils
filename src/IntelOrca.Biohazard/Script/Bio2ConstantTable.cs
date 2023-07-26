﻿using System.IO;
using System.Text;

namespace IntelOrca.Biohazard.Script
{
    public class Bio2ConstantTable : IConstantTable
    {
        private const byte SCE_MESSAGE = 4;
        private const byte SCE_EVENT = 5;
        private const byte SCE_FLAG_CHG = 6;

        public string GetEnemyName(byte kind) => g_enemyNames.Namify("ENEMY_", kind);
        public string GetItemName(byte kind) => g_itemNames.Namify("ITEM_", kind);

        public byte? FindOpcode(string name)
        {
            for (int i = 0; i < g_instructionSignatures.Length; i++)
            {
                var signature = g_instructionSignatures[i];
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

        public string? GetConstant(byte opcode, int pIndex, BinaryReader reader)
        {
            using (var br = reader.Fork())
            {
                if (opcode == (byte)OpcodeV2.Ck)
                {
                    if (pIndex == 1)
                    {
                        var F = br.ReadByte();
                        var f = br.ReadByte();
                        return GetFlagName(F, f);
                    }
                }
                else if (opcode == (byte)OpcodeV2.WorkSet)
                {
                    if (pIndex == 1)
                    {
                        var kind = br.ReadByte();
                        var id = br.ReadByte();
                        if (kind == 3)
                            return GetConstant('2', id);
                        else if (kind == 4)
                            return GetConstant('1', id);
                    }
                }
                else if (opcode == (byte)OpcodeV2.AotReset)
                {
                    if (pIndex == 3)
                    {
                        br.BaseStream.Position++;
                        var sce = br.ReadByte();
                        if (sce == SCE_MESSAGE)
                        {
                            br.BaseStream.Position += 1;
                            return GetConstant('3', br.ReadByte());
                        }
                    }
                    if (pIndex == 5)
                    {
                        br.BaseStream.Position++;
                        var sce = br.ReadByte();
                        if (sce == SCE_EVENT)
                        {
                            br.BaseStream.Position += 3;
                            return GetConstant('g', br.ReadByte());
                        }
                        else if (sce == SCE_FLAG_CHG)
                        {
                            br.BaseStream.Position += 3;
                            return GetConstant('t', br.ReadByte());
                        }
                    }
                    else if (pIndex == 6)
                    {
                        br.BaseStream.Position++;
                        var sce = br.ReadByte();
                        if (sce == SCE_EVENT)
                        {
                            br.BaseStream.Position += 3;
                            if (br.ReadByte() == (byte)OpcodeV2.Gosub)
                            {
                                return GetConstant('p', br.ReadByte());
                            }
                        }
                    }
                    else if (pIndex == 7)
                    {
                        br.BaseStream.Position++;
                        var sce = br.ReadByte();
                        if (sce == SCE_FLAG_CHG)
                        {
                            br.BaseStream.Position += 5;
                            return GetConstant('p', br.ReadByte());
                        }
                    }
                }
                else if (opcode == (byte)OpcodeV2.AotSet)
                {
                    if (pIndex == 9)
                    {
                        br.BaseStream.Position++;
                        var sce = br.ReadByte();
                        if (sce == SCE_MESSAGE)
                        {
                            br.BaseStream.Position += 11;
                            return GetConstant('3', br.ReadByte());
                        }
                    }
                    if (pIndex == 11)

                    {
                        br.BaseStream.Position++;
                        var sce = br.ReadByte();
                        if (sce == SCE_EVENT)
                        {
                            br.BaseStream.Position += 13;
                            return GetConstant('g', br.ReadByte());
                        }
                        else if (sce == SCE_FLAG_CHG)
                        {
                            br.BaseStream.Position += 13;
                            return GetConstant('t', br.ReadByte());
                        }
                    }
                    else if (pIndex == 12)
                    {
                        br.BaseStream.Position++;
                        var sce = br.ReadByte();
                        if (sce == SCE_EVENT || sce == SCE_FLAG_CHG)
                        {
                            br.BaseStream.Position += 13;
                            if (br.ReadByte() == (byte)OpcodeV2.Gosub)
                            {
                                return GetConstant('p', br.ReadByte());
                            }
                        }
                    }
                    else if (pIndex == 13)
                    {
                        br.BaseStream.Position++;
                        var sce = br.ReadByte();
                        if (sce == SCE_FLAG_CHG)
                        {
                            br.BaseStream.Position += 15;
                            return GetConstant('p', br.ReadByte());
                        }
                    }
                }
                else if (opcode == (byte)OpcodeV2.AotSet4p)
                {
                    if (pIndex == 15)
                    {
                        br.BaseStream.Position++;
                        var sce = br.ReadByte();
                        if (sce == SCE_EVENT)
                        {
                            br.BaseStream.Position += 21;
                            return GetConstant('g', br.ReadByte());
                        }
                    }
                    else if (pIndex == 16)
                    {
                        br.BaseStream.Position++;
                        var sce = br.ReadByte();
                        if (sce == SCE_EVENT)
                        {
                            br.BaseStream.Position += 21;
                            if (br.ReadByte() == (byte)OpcodeV2.Gosub)
                            {
                                return GetConstant('p', br.ReadByte());
                            }
                        }
                    }
                }
                return null;
            }
        }

        public string? GetConstant(char kind, int value)
        {
            switch (kind)
            {
                case '0':
                    return $"ID_AOT_{value}";
                case '1':
                    return $"ID_OBJ_{value}";
                case '2':
                    return $"ID_EM_{value}";
                case '3':
                    return $"ID_MSG_{value}";
                case 'e':
                    return GetEnemyName((byte)value);
                case 't':
                case 'T':
                    if (value == 255)
                        return "LOCKED";
                    else if (value == 254)
                        return "UNLOCK";
                    else if (value == 0)
                        return "UNLOCKED";
                    else
                        return GetItemName((byte)value);
                case 'c':
                    return GetConstantName(g_comparators, value);
                case 'o':
                    return GetConstantName(g_operators, value);
                case 's':
                    return GetConstantName(g_sceNames, value);
                case 'a':
                    if (value == 0)
                        return "SAT_AUTO";
                    var sb = new StringBuilder();
                    for (int i = 0; i < 8; i++)
                    {
                        var mask = 1 << i;
                        if (value == mask)
                        {
                            return g_satNames[i];
                        }
                        else if ((value & (1 << i)) != 0)
                        {
                            sb.Append(g_satNames[i]);
                            sb.Append(" | ");
                        }
                    }
                    sb.Remove(sb.Length - 3, 3);
                    return sb.ToString();
                case 'w':
                    return GetConstantName(g_workKinds, value);
                case 'g':
                    if (value == (byte)OpcodeV2.Gosub)
                        return "I_GOSUB";
                    break;
                case 'p':
                    if (value == 0)
                        return "main";
                    else if (value == 1)
                        return "aot";
                    return $"main_{value:X2}";
                case 'f':
                    return GetConstantName(g_flagGroups, value);
                case 'v':
                    return GetNamedVariable(value);
            }
            return null;
        }

        private string? GetConstantName(string?[] table, int value)
        {
            if (value >= 0 && value < table.Length)
                return table[value];
            return null;
        }

        private int? FindConstantValue(string symbol, char kind)
        {
            for (int i = 0; i < 256; i++)
            {
                var name = GetConstant(kind, i);
                if (name == symbol)
                    return i;
            }
            return null;
        }

        public int? GetConstantValue(string symbol)
        {
            switch (symbol)
            {
                case "LOCKED":
                    return 255;
                case "UNLOCK":
                    return 254;
                case "UNLOCKED":
                    return 0;
                case "I_GOSUB":
                    return (byte)OpcodeV2.Gosub;
            }

            if (symbol.StartsWith("ENEMY_"))
                return FindConstantValue(symbol, 'e');
            else if (symbol.StartsWith("ITEM_"))
                return FindConstantValue(symbol, 't');
            else if (symbol.StartsWith("CMP_"))
                return FindConstantValue(symbol, 'c');
            else if (symbol.StartsWith("OP_"))
                return FindConstantValue(symbol, 'o');
            else if (symbol.StartsWith("SCE_"))
                return FindConstantValue(symbol, 's');
            else if (symbol.StartsWith("SAT_"))
                return FindConstantValue(symbol, 'a');
            else if (symbol.StartsWith("WK_"))
                return FindConstantValue(symbol, 'w');
            else if (symbol.StartsWith("FG_"))
                return FindConstantValue(symbol, 'f');
            else if (symbol.StartsWith("F_"))
            {
                for (var i = 0; i < 32; i++)
                    for (var j = 0; j < 256; j++)
                        if (GetFlagName(i, j) == symbol)
                            return j;
            }
            else if (symbol.StartsWith("V_"))
                return FindConstantValue(symbol, 'v');

            return null;
        }

        public int GetInstructionSize(byte opcode, BinaryReader? br)
        {
            if (opcode < g_instructionSizes.Length)
                return g_instructionSizes[opcode];
            return 0;
        }

        public string GetOpcodeSignature(byte opcode)
        {
            if (opcode < g_instructionSignatures.Length)
                return g_instructionSignatures[opcode];
            return "";
        }

        public bool IsOpcodeCondition(byte opcode)
        {
            switch ((OpcodeV2)opcode)
            {
                case OpcodeV2.Ck:
                case OpcodeV2.Cmp:
                case OpcodeV2.MemberCmp:
                case OpcodeV2.KeepItemCk:
                case OpcodeV2.WorkCopy:
                    return true;
            }
            return false;
        }

        public string? GetNamedFlag(int group, int index)
        {
            var groupName = GetConstant('F', group);
            var flagName = GetFlagName(group, index);
            flagName ??= index.ToString();
            return $"${groupName}.{flagName})";
        }

        private static string? GetFlagName(int group, int index)
        {
            return group switch
            {
                0 when index == 0x19 => "F_DIFFICULT",
                1 when index == 0 => "F_PLAYER",
                1 when index == 1 => "F_SCENARIO",
                1 when index == 5 => "F_EASY",
                1 when index == 6 => "F_BONUS",
                1 when index == 0x1B => "F_CUTSCENE",
                0xB when index == 0x1F => "F_QUESTION",
                _ => null
            };
        }

        public string? GetNamedVariable(int index)
        {
            return index switch
            {
                2 => "V_USED_ITEM",
                16 => "V_TEMP",
                26 => "V_CUT",
                27 => "V_LAST_RDT",
                _ => $"V_{index:X2}",
            };
        }

        private static string?[] g_flagGroups = new[]
        {
            "FG_0",
            "FG_GAME",
            "FG_STATE",
            "FG_3",
            "FG_GENERAL",
            "FG_LOCAL",
            "FG_ENEMY",
            "FG_7",
            "FG_ITEM",
            "FG_9",
            "FG_A",
            "FG_INPUT",
            null,
            null,
            null,
            null,

            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "FG_LOCK"
        };

        private static int[] g_instructionSizes = new int[]
        {
            1, 2, 1, 4, 4, 2, 4, 4, 1, 4, 3, 1, 1, 6, 2, 4,
            2, 4, 2, 4, 6, 2, 2, 6, 2, 2, 2, 6, 1, 4, 1, 1,
            1, 4, 4, 6, 4, 3, 6, 4, 1, 2, 1, 6, 20, 38, 3, 4,
            1, 1, 8, 8, 4, 3, 12, 4, 3, 8, 16, 32, 2, 3, 6, 4,
            8, 10, 1, 4, 22, 5, 10, 2, 16, 8, 2, 3, 5, 22, 22, 4,
            4, 6, 6, 6, 22, 6, 4, 8, 4, 4, 2, 2, 3, 2, 2, 2,
            14, 4, 2, 1, 16, 2, 1, 28, 40, 30, 6, 4, 1, 4, 6, 2,
            1, 1, 16, 8, 4, 22, 3, 4, 6, 1, 16, 16, 6, 6, 6, 6,
            2, 3, 3, 1, 2, 6, 1, 1, 3, 1, 6, 6, 8, 24, 24
        };

        private string[] g_enemyNames = new string[]
        {
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
            "Zombie (Cop)",
            "Zombie (Brad)",
            "Zombie (Guy1)",
            "Zombie (Girl)",
            "",
            "Zombie (TestSubject)",
            "Zombie (Scientist)",
            "Zombie (Naked)",
            "Zombie (Guy2)",
            "",
            "",
            "",
            "",
            "",
            "Zombie (Guy3)",
            "Zombie (Random)",
            "Zombie Dog",
            "Crow",
            "Licker (Red)",
            "Alligator",
            "Licker (Grey)",
            "Spider",
            "Baby Spider",
            "Birkin Goo",
            "Birkin Goo Child",
            "Cockroach",
            "Tyrant 1",
            "Tyrant 2",
            "",
            "Zombie Arms",
            "Ivy",
            "Vines",
            "Birkin 1",
            "Birkin 2",
            "Birkin 3",
            "Birkin 4",
            "Birkin 5",
            "",
            "",
            "",
            "",
            "Ivy (Purple)",
            "Giant Moth",
            "Maggots",
            "",
            "",
            "",
            "",
            "Chief Irons 1",
            "Ada Wong 1",
            "Chief Irons 2",
            "Ada Wong 2",
            "Ben Bertolucci 1",
            "Sherry (Pendant)",
            "BenBertolucci 2",
            "AnnetteBirkin 1",
            "Robert Kendo",
            "Annette Birkin 2",
            "Marvin Branagh",
            "Mayors Daughter",
            "",
            "",
            "",
            "Sherry (Jacket)",
            "Leon Kennedy (Rpd)",
            "Claire Redfield",
            "",
            "",
            "Leon Kennedy (Bandaged)",
            "Claire Redfield (No Jacket)",
            "",
            "",
            "Leon Kennedy (Tank Top)",
            "Claire Redfield (Cow Girl)",
            "Leon Kennedy (Leather)",
        };

        private string[] g_itemNames = new string[]
        {
            "None",
            "Knife",
            "HandgunLeon",
            "HandgunClaire",
            "CustomHandgun",
            "Magnum",
            "CustomMagnum",
            "Shotgun",
            "CustomShotgun",
            "GrenadeLauncherExplosive",
            "GrenadeLauncherFlame",
            "GrenadeLauncherAcid",
            "Bowgun",
            "ColtSAA",
            "Sparkshot",
            "SMG",
            "Flamethrower",
            "RocketLauncher",
            "GatlingGun",
            "Beretta",
            "HandgunAmmo",
            "ShotgunAmmo",
            "MagnumAmmo",
            "FuelTank",
            "ExplosiveRounds",
            "FlameRounds",
            "AcidRounds",
            "SMGAmmo",
            "SparkshotAmmo",
            "BowgunAmmo",
            "InkRibbon",
            "SmallKey",
            "HandgunParts",
            "MagnumParts",
            "ShotgunParts",
            "FAidSpray",
            "AntivirusBomb",
            "ChemicalACw32",
            "HerbG",
            "HerbR",
            "HerbB",
            "HerbGG",
            "HerbGR",
            "HerbGB",
            "HerbGGG",
            "HerbGGB",
            "HerbGRB",
            "Lighter",
            "Lockpick",
            "PhotoSherry",
            "ValveHandle",
            "RedJewel",
            "RedCard",
            "BlueCard",
            "SerpentStone",
            "JaguarStone",
            "JaguarStoneL",
            "JaguarStoneR",
            "EagleStone",
            "BishopPlug",
            "RookPlug",
            "KnightPlug",
            "KingPlug",
            "WeaponBoxKey",
            "Detonator",
            "C4",
            "C4Detonator",
            "Crank",
            "FilmA",
            "FilmB",
            "FilmC",
            "UnicornMedal",
            "EagleMedal",
            "WolfMedal",
            "Cog",
            "ManholeOpener",
            "MainFuse",
            "FuseCase",
            "Vaccine",
            "VaccineCart",
            "FilmD",
            "VaccineBase",
            "GVirus",
            "SpecialKey",
            "JointPlugBlue",
            "JointPlugRed",
            "Cord",
            "PhotoAda",
            "CabinKey",
            "SpadeKey",
            "DiamondKey",
            "HeartKey",
            "ClubKey",
            "DownKey",
            "UpKey",
            "PowerRoomKey",
            "MODisk",
            "UmbrellaKeyCard",
            "MasterKey",
            "PlatformKey",
        };

        private static string[] g_instructionSignatures = new string[]
        {
            "nop",
            "evt_end",
            "evt_next",
            "evt_chain",
            "evt_exec:ugp",
            "evt_kill",
            "if:uL",
            "else:u@",
            "endif",
            "sleep:uU",
            "sleeping:U",
            "wsleep",
            "wsleeping",
            "for:uLU",
            "next",
            "while:uL",

            "ewhile",
            "do:uL",
            "edwhile:'",
            "switch:uL",
            "case:uLU",
            "default",
            "eswitch",
            "goto:uuu~",
            "gosub:p",
            "return",
            "break",
            "for2",
            "break_point",
            "work_copy",
            "nop_1E",
            "nop_1F",

            "nop_20",
            "ck:fuu",
            "set:fuu",
            "cmp:uvcI",
            "save:vI",
            "copy:vv",
            "calc:uovI",
            "calc2:ovv",
            "sce_rnd",
            "cut_chg",
            "cut_old",
            "message_on:u3uuu",
            "aot_set:0sauuIIIIuuuuuu",
            "obj_model_set:1uuuuUUIIIIIIIIIIIIuuuu",
            "work_set:wu",
            "speed_set:uI",

            "add_speed",
            "add_aspeed",
            "pos_set:uIII",
            "dir_set:uIII",
            "member_set",
            "member_set2:uv",
            "se_on:uIIIII",
            "sca_id_set",
            "flr_set",
            "dir_ck",
            "sce_espr_on:uUUUIIII",
            "door_aot_se:0sauuIIIIIIIIuuuuuuuutu",
            "cut_auto",
            "member_copy:vu",
            "member_cmp",
            "plc_motion",

            "plc_dest:uuuII",
            "plc_neck:uIIIuu",
            "plc_ret",
            "plc_flg:uU",
            "sce_em_set:u2euuuuuuIIIIUU",
            "col_chg_set",
            "aot_reset:0sauuuuuu",
            "aot_on:0",
            "super_set:uuuIIIIII",
            "super_reset:uIII",
            "plc_gun",
            "cut_replace",
            "sce_espr_kill",
            "",
            "item_aot_set:0sauuIIUUTUUuu",
            "sce_key_ck:uU",

            "sce_trg_ck:uU",
            "sce_bgm_control",
            "sce_espr_control",
            "sce_fade_set",
            "sce_espr3d_on:uUUUIIIIIII",
            "member_calc:oUI",
            "member_calc2:ouu",
            "sce_bgmtbl_set:uuuUU",
            "plc_rot:uU",
            "xa_on:uU",
            "weapon_chg",
            "plc_cnt",
            "sce_shake_on",
            "mizu_div_set",
            "keep_item_ck:t",
            "xa_vol",

            "kage_set",
            "cut_be_set",
            "sce_item_lost:t",
            "plc_gun_eff",
            "sce_espr_on2",
            "sce_espr_kill2",
            "plc_stop",
            "aot_set_4p:usauuIIIIIIIIuuuuuu",
            "door_aot_set_4p:0sauuIIIIIIIIIIIIuuuuuuuutu",
            "item_aot_set_4p:0sauuIIIIIIIITUUuu",
            "light_pos_set:uuuI",
            "light_kido_set:uI",
            "rbj_reset",
            "sce_scr_move:uI",
            "parts_set:uuuI",
            "movie_on",

            "splc_ret",
            "splc_sce",
            "super_on",
            "mirror_set",
            "sce_fade_adjust",
            "sce_espr3d_on2",
            "sce_item_get",
            "sce_line_start",
            "sce_line_main",
            "sce_line_end",
            "sce_parts_bomb",
            "sce_parts_down",
            "light_color_set",
            "light_pos_set2:uuuI",
            "light_kido_set2:uuuU",
            "light_color_set2",

            "se_vol",
            "",
            "",
            "",
            "",
            "",
            "poison_ck",
            "poison_clr",
            "sce_item_ck_lost:tu",
            "",
            "nop_8a",
            "nop_8b",
            "nop_8c",
            "",
            "",
        };

        private static readonly string[] g_comparators = new string[]
        {
            "CMP_EQ",
            "CMP_GT",
            "CMP_GE",
            "CMP_LT",
            "CMP_LE",
            "CMP_NE"
        };

        private static readonly string[] g_operators = new string[]
        {
            "OP_ADD",
            "OP_SUB",
            "OP_MUL",
            "OP_DIV",
            "OP_MOD",
            "OP_OR",
            "OP_AND",
            "OP_XOR",
            "OP_NOT",
            "OP_LSL",
            "OP_LSR",
            "OP_ASR"
        };

        private static readonly string[] g_satNames = new string[]
        {
            "SAT_PL",
            "SAT_EM",
            "SAT_SPL",
            "SAT_OB",
            "SAT_MANUAL",
            "SAT_FRONT",
            "SAT_UNDER",
            "0x80"
        };

        private static readonly string[] g_sceNames = new string[] {
            "SCE_AUTO",
            "SCE_DOOR",
            "SCE_ITEM",
            "SCE_NORMAL",
            "SCE_MESSAGE",
            "SCE_EVENT",
            "SCE_FLAG_CHG",
            "SCE_WATER",
            "SCE_MOVE",
            "SCE_SAVE",
            "SCE_ITEMBOX",
            "SCE_DAMAGE",
            "SCE_STATUS",
            "SCE_HIKIDASHI",
            "SCE_WINDOWS"
        };

        private static readonly string[] g_workKinds = new string[]
        {
            "WK_NONE",
            "WK_PLAYER",
            "WK_SPLAYER",
            "WK_ENEMY",
            "WK_OBJECT",
            "WK_DOOR",
            "WK_ALL"
        };
    }
}
