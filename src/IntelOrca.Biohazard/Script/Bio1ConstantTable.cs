using System;
using System.Collections.Generic;
using System.IO;

namespace IntelOrca.Biohazard.Script
{
    public class Bio1ConstantTable : IConstantTable
    {
        private const byte SCE_DOOR = 1;
        private const byte SCE_MESSAGE = 2;
        private const byte SCE_USEITEM = 7;
        private const byte SCE_ITEMBOX = 8;
        private const byte SCE_EVENT = 9;
        private const byte SCE_SAVE = 10;
        private const byte SCE_DOCUMENT = 13;
        private const byte SCE_HIKIDASHI = 14;
        private const byte SCE_TYPEWRITER = 16;

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
                'f' => GetConstantName(g_flagGroups, value),
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
                    'e', 'f', 'i', 's', 'p', 'r', 't', 'w',
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
                    // 03 is a 1-byte state marker (validated x1898 across US rooms);
                    // everything after it belongs to the tween ISA
                    Re1EventOpcode.TweenBeginAlt => 1,
                    Re1EventOpcode.WorkSet => 3,
                    Re1EventOpcode.Fork => 4,
                    Re1EventOpcode.Block => 2,
                    Re1EventOpcode.Single => 2,
                    Re1EventOpcode.Chain => 2,
                    Re1EventOpcode.Disable => 2,

                    // legacy/defensive: these bytes only appear inside state-1
                    // actor streams (see _actorOpcodes); sizes kept for callers
                    // that scan without task-state tracking
                    (Re1EventOpcode)0x80 => 1,
                    (Re1EventOpcode)0x81 => 2,
                    (Re1EventOpcode)0x83 => 7,
                    (Re1EventOpcode)0x84 => 4,
                    (Re1EventOpcode)0x85 => 4,
                    (Re1EventOpcode)0x87 => 4,
                    (Re1EventOpcode)0x88 => 4,
                    (Re1EventOpcode)0x89 => 2,
                    (Re1EventOpcode)0x8A => 2,

                    (Re1EventOpcode)0xE8 => 4,

                    Re1EventOpcode.Sleep => 4,
                    Re1EventOpcode.For => 4,
                    Re1EventOpcode.Do => 2,
                    // FF finishes the task in any state: 1 byte (scd_event_loop
                    // shared control case does PC += 1; the byte that usually
                    // follows is 00 padding, not an operand)
                    Re1EventOpcode.Finish => 1,
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

        /// <summary>
        /// The instruction set executed inside an event task sub-state: after a
        /// top-level "01" the game runs actor commands (event_state_1_logic),
        /// after "02"/"03" it runs tween ops (event_state_2_logic), until the
        /// stream's exit opcode. Sizes validated against all US room data.
        /// </summary>
        public enum Re1SubIsa
        {
            Actor,
            Tween,
        }

        public string GetSubOpcodeSignature(byte opcode, Re1SubIsa isa)
        {
            var table = isa == Re1SubIsa.Actor ? _actorOpcodes : _tweenOpcodes;
            if (opcode < table.Length)
                return table[opcode];
            return "";
        }

        // TODO 81: +8 payload bytes when the low nibble of the param byte is set
        private int GetActorSize(byte opcode, BinaryReader? br)
        {
            if (opcode == 0x81 && br != null)
            {
                // br is positioned just past the opcode byte, so the param is
                // at the fork's current position
                using (var fork = br.Fork())
                {
                    if (fork.BaseStream.Position < fork.BaseStream.Length)
                    {
                        var param = fork.ReadByte();
                        return 2 + ((param & 0x0F) != 0 ? 8 : 0);
                    }
                }
            }
            return opcode switch
            {
                0x00 => 1,
                0x80 => 1,
                0x81 => 2,
                0x82 => 1,
                0x83 => 7,
                0x84 => 4,
                0x85 => 4,
                0x86 => 1,
                0x87 => 4,
                0x88 => 4,
                0x89 => 2,
                0x8A => 2,
                0x8B => 1,
                0xFF => 1,
                _ => 0,
            };
        }

        public int GetSubInstructionSize(byte opcode, BinaryReader? br, Re1SubIsa isa)
        {
            if (isa == Re1SubIsa.Actor)
                return GetActorSize(opcode, br);
            return opcode switch
            {
                0x00 => 1,
                0x01 => 1,
                0x02 => 1,
                0x03 => 1,
                0x04 => 1,
                0x05 => 4,
                0x06 => 4,
                0x07 => 8,
                0x08 => 3,
                0x09 => 2,
                0x0A => 4,
                0x0B => 8,
                0xFF => 1,
                _ => 0,
            };
        }

        public byte? FindSubOpcode(string name, out Re1SubIsa isa)
        {
            isa = Re1SubIsa.Actor;
            for (var pass = 0; pass < 2; pass++)
            {
                var table = pass == 0 ? _actorOpcodes : _tweenOpcodes;
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
                    {
                        isa = pass == 0 ? Re1SubIsa.Actor : Re1SubIsa.Tween;
                        return (byte)i;
                    }
                }
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
            "ck:fuu",
            "set:fuu",
            "cmpb:uuu",
            "cmpw:uuuI",
            "setb:uuu",
            "cutnext:u",
            "cutcurr:u",
            "message",
            "door_aot_set:uIIIIuuuuurIIIItu",
            "aot_set:uIIIIsuuuuuuu",
            "nop:u",
            "scene_setup",
            "testitem:t",
            "testpickup:t",
            "aot_reset:usuIII",
            "aot_delete:usu",
            "evt_exec:uup",
            "bgm_play:u",
            "bgm_stop:u",
            "se_play_3d",
            "item_aot_set:uIIIItuuuuuuuuuuuuuuu",
            "setbyte:uuu",
            "item_ck:t",
            "enemy:euuuuuuIuuIIIuuuu",
            "timer_setup",
            "ck_last_item",
            "xa_on",
            "obj:uuuIIIIuuuuuuuuuuuuuuuu",
            "dir_set:uIIIIII",
            "pos_set:uIIIIII",
            "ck_item_count",
            "cut_auto",
            "aot_on:usu",
            "aot_switch",
            "",
            "snd_fadeout",
            "eml_state",
            "movie_on:u",
            "effect",
            "plw_anim",
            "remove_item:t",
            "give_item:uuu",
            "",
            "se_volume",
            "inst_cfg",
            "setw",
            "nop_wide",
            "sys_multi",
            "model_op",
            "objtbl_b_set",
            "ck_anim",
            "tbl37_set",
            "ck_bits",
            "get_eml_state",
            "msgnode_set",
            "eml_rot",
            "ck_counter",
            "effect_tracked",
            "effect_kill_a",
            "ck_tween",
            "obj_xfm",
            "spd_set",
            "effect_kill_b",
            "se_rate",
            "task_kill",
            "spd_add",
            "msg_list",
            "eml_pos",
            "effect_clear",
            "bank_bit",
            "bgm_bank_down",
            "bgm_bank_up",
            "swap_var",
            "objs_hide",
            "mass_mask",
            "xa_flag",
            "xa_flag_ck",
        };

        private string[] _eventOpcodes = new string[]
        {
            "evt_nop",
            "evt_actor_begin",
            "evt_tween_begin",
            "evt_tween_begin_alt",
            "evt_work_set:wu",
            "evt_fork:upu",
            "evt_block:u",
            "evt_single:u",
            "evt_chain",
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
            "evt_actor_reset",
            "evt_actor_motion",
            "evt_actor_motion_bitclr",
            "evt_actor_anim_seq",
            "evt_actor_anim_flags",
            "evt_actor_anim_set",
            "evt_actor_idle",
            "evt_actor_flag_op",
            "evt_actor_param_set",
            "evt_actor_action_a",
            "evt_actor_action_b",
            "evt_actor_end",
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
            "evt_push_cond",
            "evt_skip_if",
            "evt_sleep",
            "evt_sleep_tick",
            "evt_for:uU",
            "evt_fornext",
            "evt_do:u",
            "evt_dountil",
            "evt_next",
            "evt_finish"
        };

        // State-1 "actor command" stream (event_state_1_logic), entered by the
        // top-level op 01; exits on 8B (80 falls through into it). Control ops
        // F6-FF are valid here too and keep their evt_ names. Best guesses are
        // marked TODO.
        private string[] _actorOpcodes = new string[]
        {
            "act_nop", /* 00 */
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
            "act_reset", /* 80 resets +0x85 then ends the stream (falls through into act_end) */
            "act_motion", /* 81 */
            "act_motion_bitclr", /* 82 */
            "act_anim_seq:UUU", /* 83 */
            "act_anim_flags:bbb", /* 84 */
            "act_anim_set:bbb", /* 85 */
            "act_idle", /* 86 */
            "act_flag_op", /* 87 */
            "act_param_set:Uu", /* 88 */
            "act_action_a:u", /* 89 */
            "act_action_b:u", /* 8A */
            "act_end", /* 8B */
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
            "act_finish" /* FF */
        };

        // State-2 "tween" stream (event_state_2_logic), entered by top-level
        // ops 02/03; exits on 01. Control ops F6-FF are valid here too and
        // keep their evt_ names. Best guesses are marked TODO.
        private string[] _tweenOpcodes = new string[]
        {
            "tw_nop", /* 00 */
            "tw_end", /* 01 */
            "tw_pos_add", /* 02 */
            "tw_rot_add", /* 03 */
            "tw_pos_rot_add", /* 04 */
            "tw_set_pos:uuu", /* 05 */
            "tw_set_rot:uuu", /* 06 */
            "tw_abs_pos:UUUu", /* 07 */
            "tw_set_field:ub", /* 08 */
            "tw_set_8a:u", /* 09 */
            "tw_set_sel:uuu", /* 0A */
            "tw_abs_rot:UUUu", /* 0B */
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
            "tw_finish" /* FF */
        };

        private static int[] _instructionSizes1 = new int[]
        {
            2, 2, 2, 2, 4, 4, 4, 6, 4, 2, 2, 4, 26, 18, 2, 8,
            2, 2, 10, 4, 4, 2, 2, 10, 26, 4, 2, 22, 6, 2, 4, 28,
            14, 14, 4, 2, 4, 4, 0, 2, 4 + 2, 2, 12, 4, 2, 4, 0, 4,
            12, 4, 4, 2, 8, 4, 4, 4, 4, 2, 4, 6, 6, 12, 2, 6,
            16, 4, 4, 4, 2, 2, 44 + 0, 14, 2, 2, 2, 2, 4, 2, 4, 2,
            2
        };


        private static string?[] g_flagGroups = new[]
        {
            "FG_SCENARIO",
            "FG_COMMON",
            "FG_LOCK",
            "FG_ENEMY",
            "FG_ROOM",
            "FG_STATUS",
            "FG_6",
            "FG_ITEM",
            "FG_MAP",
            "FG_9"
        };

        private static readonly string[] g_sceNames = new string[] {
            "SCE_NONE",
            "SCE_DOOR",
            "SCE_MESSAGE",
            "SCE_3",
            "SCE_ITEM",
            "SCE_5",
            "SCE_6",
            "SCE_USEITEM",
            "SCE_ITEMBOX",
            "SCE_EVENT",
            "SCE_SAVE",
            "SCE_B",
            "SCE_C",
            "SCE_DOCUMENT",
            "SCE_HIKIDASHI",
            "SCE_F",
            "SCE_TYPEWRITER",
        };

        private static readonly string[] g_wkNames = new string[] {
            "WK_PLAYER",
            "WK_ENEMY",
            "WK_OBJ",
            "WK_AOT",
        };
    }
}
