using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IntelOrca.Biohazard.Script
{
    internal class BioCvConstantTable : IConstantTable
    {
        public byte? FindOpcode(string name, bool isEventOpcode)
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

        public string? GetConstant(char kind, int value)
        {
            return kind switch
            {
                '0' => $"ID_ITEM_{value}",
                '1' => $"ID_AOT_{value}",
                'f' => GetFlagName(value),
                'p' => GetProcedureName(value),
                'T' => GetItemName((byte)value),
                't' => GetItemName((byte)value),
                _ => null
            };
        }

        public string? GetConstant(byte opcode, int pIndex, BinaryReader reader)
        {
            using var br = reader.Fork();
            if (opcode == 0x04 || opcode == 0x05)
            {
                if (pIndex == 2)
                {
                    var a = br.ReadByte();
                    var b = br.ReadUInt16();
                    var c = br.ReadByte();
                    if (a == 10 && b == 23)
                        return GetConstant('1', c);
                }
            }
            else if (opcode == 0x06)
            {
                // cmpb: variable, comparison, value
                var variable = br.ReadByte();
                var comparison = br.ReadByte();
                var value = br.ReadByte();
                if (pIndex == 0)
                    return GetVariableName(variable);
                if (pIndex == 1)
                    return GetCompareName(comparison);
                if (pIndex == 2 && variable == 8)
                    return GetConstant('t', value);
            }
            else if (opcode == 0x07)
            {
                // cmpw: enemy index, variable, comparison, value
                br.ReadByte();
                var variable = br.ReadByte();
                var comparison = br.ReadByte();
                if (pIndex == 1)
                    return GetVariableName(variable);
                if (pIndex == 2)
                    return GetCompareName(comparison);
            }
            else if (opcode == 0x08)
            {
                // setb: variable, value
                var variable = br.ReadByte();
                var value = br.ReadByte();
                if (pIndex == 0)
                    return GetVariableName(variable);
                if (pIndex == 1 && variable == 8)
                    return GetConstant('t', value);
            }
            else if (opcode == 0x09)
            {
                // setw: variable, value
                var variable = br.ReadByte();
                if (pIndex == 0)
                    return GetVariableName(variable);
            }
            return null;
        }

        public int? GetConstantValue(string symbol)
        {
            return null;
        }

        private static string? GetFlagName(int value)
        {
            // Flag groups used by the CV flag check/set handlers.
            return value switch
            {
                1 => "FG_EVENT",
                2 => "FG_KEY",
                3 => "FG_ENEMY_DEAD",
                4 => "FG_ROOM",
                5 => "FG_STATUS",
                6 => "FG_SPECIAL",
                7 => "FG_ITEM",
                8 => "FG_MAP",
                9 => "FG_ITEM_COUNT",
                10 => "FG_CUT_BG",
                11 => "FG_GAME",
                12 => "FG_TIME",
                13 => "FG_PLAYER",
                14 => "FG_PLAYER_STATUS",
                15 => "FG_PLAYER_2",
                16 => "FG_SSD",
                _ => null
            };
        }

        private static string? GetVariableName(byte value)
        {
            // Variable ids used by the CV compare/save handlers.
            return value switch
            {
                0 => "VAR_STAGE",
                1 => "VAR_ROOM",
                2 => "VAR_CUT",
                5 => "VAR_PLAYER_HP",
                7 => "VAR_MESSAGE_SELECT",
                8 => "VAR_ITEM_ID",
                9 => "VAR_EVENT_TIME",
                10 => "VAR_ENEMY_HP",
                11 => "VAR_ENEMY_HP_0",
                12 => "VAR_ENEMY_HP_1",
                13 => "VAR_ENEMY_HP_2",
                14 => "VAR_ENEMY_HP_3",
                15 => "VAR_POS",
                16 => "VAR_FLOOR",
                17 => "VAR_WPNL",
                18 => "VAR_WPNR",
                19 => "VAR_EF_SLOW",
                20 => "VAR_ETC_IDX",
                21 => "VAR_ROOM_CASE",
                22 => "VAR_ITEM_SLOT",
                23 => "VAR_GAME_MODE",
                24 => "VAR_PLAYER",
                25 => "VAR_COSTUME",
                _ => null
            };
        }

        private static string? GetCompareName(byte value)
        {
            return value switch
            {
                0 => "CMP_EQ",
                1 => "CMP_GT",
                2 => "CMP_GE",
                3 => "CMP_LT",
                4 => "CMP_LE",
                5 => "CMP_NE",
                _ => null
            };
        }

        private static string GetProcedureName(int value)
        {
            return $"main_{value + 2:X2}";
        }

        public string GetEnemyName(byte kind) => $"ENEMY_{kind:X2}";

        public string GetItemName(byte kind) => g_itemNames.Namify("ITEM_", kind);

        public string? GetNamedFlag(int obj, int index) => null;

        public string? GetNamedVariable(int index)
        {
            return index >= 0 && index <= byte.MaxValue ?
                GetVariableName((byte)index) :
                null;
        }

        public int GetInstructionSize(byte opcode, BinaryReader? br, bool isEventOpcode = false)
        {
            if (IsSubOpcodeParent(opcode))
            {
                if (br == null)
                    return 1;

                var position = br.BaseStream.Position;
                if (position >= br.BaseStream.Length)
                    return 1;

                try
                {
                    var subOpcode = br.ReadByte();
                    return GetSubOpcodeSize(opcode, subOpcode, br);
                }
                finally
                {
                    br.BaseStream.Position = position;
                }
            }
            return _opcodeSizes[opcode];
        }

        internal static bool IsSubOpcodeParent(byte opcode)
        {
            return opcode == 0x64 || opcode == 0x66 || opcode == 0x67 || opcode == 0x69;
        }

        /// <summary>
        /// Gets a signature for a two-byte control opcode (0x64/0x66/0x67/0x69).
        /// The sub-opcode byte itself is consumed by the 'x' placeholder so the
        /// decompiled output shows the command name and its real operands only.
        /// </summary>
        public string GetSubOpcodeSignature(byte opcode, byte subOpcode, int instructionLength)
        {
            var key = (ushort)((opcode << 8) | subOpcode);
            if (!_subOpcodes.TryGetValue(key, out var subOpcodeInfo))
                return GetOpcodeSignature(opcode);

            var operandCount = Math.Max(0, instructionLength - 2);
            var signature = subOpcodeInfo.Signature;
            if (signature == null || GetSignatureByteLength(signature) != instructionLength - 1)
                signature = "x" + new string('u', operandCount);

            return $"{subOpcodeInfo.Name}:{signature}";
        }

        private static int GetSignatureByteLength(string signature)
        {
            var length = 0;
            foreach (var c in signature)
            {
                length += c == 'U' || c == 'I' || c == 'L' || c == '~' || c == '@' ? 2 : 1;
            }
            return length;
        }

        internal static IEnumerable<ushort> GetSubOpcodeKeys() => _subOpcodes.Keys;

        private static int GetSubOpcodeSize(byte opcode, byte subOpcode, BinaryReader br)
        {
            var key = (ushort)((opcode << 8) | subOpcode);
            if (key == 0x6923)
            {
                // Common control 0x23 has two forms; the second selector at +3
                // chooses between the 7-byte and 8-byte payload.
                return PeekByte(br, 1) == 1 ? 9 : 8;
            }

            return _subOpcodes.TryGetValue(key, out var subOpcodeInfo) ?
                subOpcodeInfo.Size :
                2;
        }

        private static byte PeekByte(BinaryReader br, int offset)
        {
            var position = br.BaseStream.Position;
            if (position + offset >= br.BaseStream.Length)
                return 0;

            br.BaseStream.Position = position + offset;
            var value = br.ReadByte();
            br.BaseStream.Position = position;
            return value;
        }

        public string GetOpcodeSignature(byte opcode, bool isEventOpcode = false)
        {
            return g_instructionSignatures[opcode];
        }

        public bool IsOpcodeCondition(byte opcode)
        {
            return opcode switch
            {
                0x04 => true,
                0x06 => true,
                0x07 => true,
                0x10 => true,
                0x11 => true,
                0x21 => true,
                0x26 => true,
                0x5D => true,
                0x72 => true,
                0x81 => true,
                0x8A => true,
                _ => false,
            };
        }

        private byte[] _opcodeSizes = new byte[]
        {
            2, 2, 2, 2, 6, 6, 4, 6, 4, 4, 4, 4, 4, 4, 6, 2, 2,
            2, 2, 4, 4, 4, 2, 8, 2, 8, 2, 6, 6, 4,
            6, 4, 4, 6, 4, 6, 4, 10, 2, 2, 2, 2, 4,
            2, 4, 2, 2, 4, 4, 2, 12, 8, 12, 4, 6, 2,
            6, 4, 2, 6, 4, 4, 4, 4, 8, 2, 4, 4,
            6, 2, 8, 10, 6, 10, 8, 4, 6, 4, 14, 6, 2, 12,
            12, 12, 12, 2, 6, 2, 14, 12, 4, 2, 2, 6, 4, 2,
            6, 2, 6, 2, 0, 4, 0, 0, 6, 0, 2, 2, 8, 22, 22, 12,
            2, 2, 14, 22, 4, 4, 4, 6, 14, 6, 2, 10, 10,
            4, 4, 6, 4, 2, 4, 2, 8, 4, 16, 2, 10, 2,
            2, 12, 6, 2, 4, 4, 2, 4, 2, 2, 4, 4, 2, 8, 12,
            4, 4, 6, 2, 4, 2, 2, 6, 2, 4, 12, 4, 4,
            4, 4, 10, 2, 10, 6, 6, 4, 2, 4, 22, 6, 4, 8, 4,
            4, 2, 2, 2, 10, 16, 2, 2, 2, 6, 4, 28, 2, 6, 4, 8,
            2, 2, 4, 8, 4, 2, 2, 4, 2, 2, 2, 4, 10, 10,
            2, 4, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 4, 3, 4, 2, 2, 1, 1, 2,
        };

        private readonly struct CvSubOpcode
        {
            public ushort Key { get; }
            public string Name { get; }
            public int Size { get; }
            public string? Signature { get; }

            public CvSubOpcode(ushort key, string name, int size, string? signature = null)
            {
                Key = key;
                Name = name;
                Size = size;
                Signature = signature;
            }
        }

        // Two-byte control opcodes: 0x64 player_ctr, 0x66 object_ctr,
        // 0x67 sub_ctr and 0x69 common_ctr dispatch through a second byte.
        // Names, sizes and operand layouts come from the corresponding CV
        // player, object, sub and common control handlers.
        private static readonly Dictionary<ushort, CvSubOpcode> _subOpcodes =
            new CvSubOpcode[]
            {
                new CvSubOpcode(0x6400, "player_ctr_end", 2, "x"),
                new CvSubOpcode(0x6407, "player_ctr_pos_set", 8, "xUUU"),
                new CvSubOpcode(0x640C, "player_ctr_base_pos_set", 6, "xuuux"),
                new CvSubOpcode(0x640D, "player_ctr_base_ang_set", 6, "xuuux"),
                new CvSubOpcode(0x6434, "player_ctr_stand", 2, "x"),
                new CvSubOpcode(0x6480, "player_ctr_motion_init", 2, "x"),
                new CvSubOpcode(0x6481, "player_ctr_exp1_set", 12, "xUUUuuuu"),
                new CvSubOpcode(0x6482, "player_ctr_exp1_clear", 2, "x"),
                new CvSubOpcode(0x6483, "player_ctr_mode_set", 4, "xuu"),
                new CvSubOpcode(0x6484, "player_ctr_motion_set", 6, "xuuuu"),
                new CvSubOpcode(0x6489, "player_ctr_pos_from_evt", 4, "xux"),
                new CvSubOpcode(0x648B, "player_ctr_mode_reset", 2, "x"),
                new CvSubOpcode(0x648C, "player_ctr_ct0_set", 8, "xuxUU"),
                new CvSubOpcode(0x648D, "player_ctr_exp1_shorts_set", 6, "xuuux"),
                new CvSubOpcode(0x648E, "player_ctr_exp1_flags_clear", 2, "x"),
                new CvSubOpcode(0x648F, "player_ctr_mode3_set", 4, "xux"),
                new CvSubOpcode(0x6491, "player_ctr_room_motion_set", 6, "xuuuu"),
                new CvSubOpcode(0x6496, "player_ctr_flags_set", 4, "xux"),

                new CvSubOpcode(0x6601, "object_ctr_mode_reset", 2, "x"),
                new CvSubOpcode(0x6614, "object_ctr_pos_add", 4, "xux"),

                new CvSubOpcode(0x6700, "sub_ctr_end", 2, "x"),
                new CvSubOpcode(0x6780, "sub_ctr_motion_init", 2, "x"),
                new CvSubOpcode(0x678B, "sub_ctr_mode_reset", 2, "x"),
                new CvSubOpcode(0x678F, "sub_ctr_motion_set", 6, "xuuuu"),
                new CvSubOpcode(0x6790, "sub_ctr_motion_init2", 2, "x"),
                new CvSubOpcode(0x6792, "sub_ctr_motion_set_ex", 8, "xuuuuux"),
                new CvSubOpcode(0x6793, "sub_ctr_motion_set2", 8, "xuuuuux"),
                new CvSubOpcode(0x6794, "sub_ctr_light_control", 4, "xux"),
                new CvSubOpcode(0x6795, "sub_ctr_flag_set", 4, "xux"),

                new CvSubOpcode(0x6900, "common_ctr_end", 2, "x"),
                new CvSubOpcode(0x6902, "common_ctr_pos_add", 3, "xu"),
                new CvSubOpcode(0x6903, "common_ctr_ang_add", 3, "xu"),
                new CvSubOpcode(0x6904, "common_ctr_pos_add2", 3, "xu"),
                new CvSubOpcode(0x6905, "common_ctr_add_pos_set", 6, "xuuux"),
                new CvSubOpcode(0x6906, "common_ctr_add_ang_set", 6, "xuuux"),
                new CvSubOpcode(0x6907, "common_ctr_pos_set", 8, "xUUU"),
                new CvSubOpcode(0x6908, "common_ctr_model_pos_set", 8, "xUUU"),
                new CvSubOpcode(0x6909, "common_ctr_ang_set", 6, "xuuux"),
                new CvSubOpcode(0x690A, "common_ctr_model_pos_axis_set", 6, "xuuU"),
                new CvSubOpcode(0x690B, "common_ctr_pos_set2", 6, "xuuux"),
                new CvSubOpcode(0x690C, "common_ctr_base_pos_set", 6, "xuuux"),
                new CvSubOpcode(0x690D, "common_ctr_base_ang_set", 6, "xuuux"),
                new CvSubOpcode(0x690E, "common_ctr_ct3_set", 4, "xux"),
                new CvSubOpcode(0x690F, "common_ctr_pos_set3", 6, "xuuux"),
                new CvSubOpcode(0x6910, "common_ctr_pos_set4", 6, "xuuux"),
                new CvSubOpcode(0x6911, "common_ctr_update", 3, "xu"),
                new CvSubOpcode(0x6912, "common_ctr_update2", 3, "xu"),
                new CvSubOpcode(0x6913, "common_ctr_update3", 3, "xu"),
                new CvSubOpcode(0x6915, "common_ctr_ct1_set", 4, "xux"),
                new CvSubOpcode(0x6916, "common_ctr_model_pos_axis_set2", 6, "xuuU"),
                new CvSubOpcode(0x6917, "common_ctr_flag_set", 4, "xux"),
                new CvSubOpcode(0x6918, "common_ctr_room_motion_set", 8, "xuuuuuu"),
                new CvSubOpcode(0x6919, "common_ctr_player_motion_set", 8, "xuuuuuu"),
                new CvSubOpcode(0x691A, "common_ctr_ips_set", 10, "xuuUUU"),
                new CvSubOpcode(0x691B, "common_ctr_ian_set", 8, "xuuuuuu"),
                new CvSubOpcode(0x691C, "common_ctr_ips_calc", 3, "xu"),
                new CvSubOpcode(0x691D, "common_ctr_ips_calc2", 3, "xu"),
                new CvSubOpcode(0x691E, "common_ctr_ips_calc3", 3, "xu"),
                new CvSubOpcode(0x691F, "common_ctr_ian_calc", 3, "xu"),
                new CvSubOpcode(0x6920, "common_ctr_ips_calc4", 3, "xu"),
                new CvSubOpcode(0x6921, "common_ctr_ian_calc2", 3, "xu"),
                new CvSubOpcode(0x6922, "common_ctr_model_pos_set2", 10, "xuuuuuuU"),
                new CvSubOpcode(0x6923, "common_ctr_motion_set", 8, "xuuuuuu"),
                new CvSubOpcode(0x6924, "common_ctr_model_calc", 2, "x"),
                new CvSubOpcode(0x6926, "common_ctr_model_ips_set", 10, "xuuUUU"),
                new CvSubOpcode(0x6928, "common_ctr_frm_set", 4, "xU"),
                new CvSubOpcode(0x6929, "common_ctr_flag_40000_set", 4, "xU"),
                new CvSubOpcode(0x692A, "common_ctr_ips_set2", 4, "xuu"),
                new CvSubOpcode(0x692B, "common_ctr_ips_ian_set", 5, "xuuu"),
                new CvSubOpcode(0x692C, "common_ctr_ips_ian_set2", 5, "xuuu"),
                new CvSubOpcode(0x692D, "common_ctr_ips_ian_set3", 5, "xuuu"),
                new CvSubOpcode(0x692E, "common_ctr_flag_20_set", 4, "xU"),
                new CvSubOpcode(0x692F, "common_ctr_model_calc2", 2, "x"),
                new CvSubOpcode(0x6930, "common_ctr_ips_get", 4, "xux"),
                new CvSubOpcode(0x6931, "common_ctr_ian_get", 4, "xuu"),
                new CvSubOpcode(0x6932, "common_ctr_model_type_set", 4, "xux"),
                new CvSubOpcode(0x6933, "common_ctr_ips_evt_set", 6, "xuuux"),
                new CvSubOpcode(0x6934, "common_ctr_ips_evt_set2", 4, "xux"),
                new CvSubOpcode(0x6935, "common_ctr_mtn_md_set", 4, "xux"),
                new CvSubOpcode(0x6984, "common_ctr_motion_set2", 6, "xuuuu"),
                new CvSubOpcode(0x698C, "common_ctr_ct0_set2", 8, "xuxUU"),
                new CvSubOpcode(0x698D, "common_ctr_motion_set3", 6, "xuuuu"),
            }.ToDictionary(x => x.Key);

        private static string[] g_instructionSignatures = new string[]
        {
            "end",
            "if:\'",
            "else:l",
            "endif:u",
            "ck:fUuu",
            "set:fUuu",
            "cmpb:uuu",
            "cmpw:uuuU",
            "setb:uuu",
            "setw:uU",
            "set_wall_atari:uuu",
            "set_etc_atari:1uu",
            "set_floor_atari:uuu",
            "ck_death:uU",
            "ck_item:0U1u",
            "clr_use_item:u",
            "ck_use_item:t",
            "ck_player_item:t",
            "set_cinematic:u",
            "set_camera:uuu",
            "event_on:uup",
            "bgm_on:uuu",
            "bgm_off:u",
            "se_on:uuuUuu",
            "se_off:u",
            "voice_on:uUuuuu",
            "voice_off:u",
            "ck_adx:uuuuu",
            "bg_se_on:uUuu",
            "bg_se_off:uuu",
            "ck_adx_time:uUuu",
            "set_message:uuu",
            "set_display_object:uuu",
            "ck_death_event:uUuu",
            "set_ck_enemy:uU",
            "set_ck_item:0U1u",
            "set_init_model:uuu",
            "set_etc_atari2:1Uuuuuuu",
            "ck_arms_item:u",
            "change_arms_item:u",
            "sub_status:u",
            "set_camera_pause:u",
            "set_camera_2:uuu",
            "set_motion_pause:u",
            "set_effect:uU",
            "init_motion_pause:u",
            "set_player_motion_pause:u",
            "init_set_kage:uuu",
            "init_motion_pause_ex:uuu",
            "player_item_lost:t",
            "set_object_link:uuuuuUUU",
            "set_door_call:uUuuuu",
            "set_player_object_link:uuuuuUUU",
            "set_light:uuu",
            "set_fade:uuuuu",
            "room_case_no:u",
            "ck_frame:uuuU",
            "set_camera_info:uuu",
            "set_player_muteki:u",
            "set_default_model:uuuuu",
            "set_mask:uuu",
            "set_lip:uuu",
            "start_mask:uuu",
            "start_lip:uuu",
            "set_player_start_look_g:uUUU",
            "set_player_stop_look_g:u",
            "set_item_aspd:uuu",
            "set_effect_display:uuu",
            "set_effect_amb:uuuuu",
            "delete_object_se:u",
            "set_next_room_bgm:uuuUuu",
            "set_next_room_bg_se:uuuUUuu",
            "call_foot_se:uuuuu",
            "call_weapon_se:uuuuuuuuu",
            "set_yakkyou:uuuuuuu",
            "set_light_type:uuu",
            "set_fog_color:uuuuu",
            "ck_player_item_block:uuu",
            "set_effect_blood:uuuUUUuuuu",
            "set_cyouten_henkei:uuuuu",
            "set_object_motion:u",
            "set_object_enemy_link:uuuuuUUU",
            "set_object_item_link:uuuuuUUU",
            "set_enemy_item_link:uuuuuUUU",
            "set_enemy_enemy_link:uuuuuUUU",
            "start_cyouten_henkei:u",
            "set_effect_blood_pool:uuuU",
            "fix_event_camera_player:u",
            "set_effect_blood_pool_2:uUUUuuuuU",
            "set_object_object_link:uuuuuUUU",
            "set_camera_yure:uU",
            "set_init_camera:u",
            "set_message_display_end:u",
            "check_pad:uuuuu",
            "start_movie:uuu",
            "stop_movie:u",
            "check_t_frame:uuuU",
            "check_event_timer:u",
            "check_camera:uuuuu",
            "set_random:u",
            "player_ctr",
            "load_work:uuu",
            "object_ctr",
            "sub_ctr",
            "load_work_2:uuuuu",
            "common_ctr",
            "event_skip_set:u",
            "delete_yakkyou:u",
            "object_alpha_set:uuuuuuu",
            "cyodan_set:uuuuuuuuuuuuuuuuuuuuu",
            "h_effect_set:uuuuuuuuuuuuuuuuuuuuu",
            "object_link_set_object_ply:uuuuuUUU",
            "effect_push:u",
            "effect_pop:u",
            "area_search_object:uuuuuuuuuuuuu",
            "light_parameter_c_set:uuuuuuuuuuuuuuuuuuuuu",
            "light_parameter_start:uuu",
            "init_midi_slot_set:uuu",
            "d_sound_flag_set:uuu",
            "sound_volume_set:uuuuu",
            "light_parameter_set:uuuUUUUU",
            "enemy_se_on:uuuU",
            "enemy_se_off:u",
            "wal_atari_set_2:uUuuuuuu",
            "flr_atari_set2:uUuuuuuu",
            "motion_pos_set_enemy_ply:uuu",
            "kage_sw_set:uuu",
            "sound_pan_set:uuuuu",
            "init_pony_set:uuu",
            "sub_map_busy_check:u",
            "set_debug_loop_ex:uU",
            "sound_fade_out:u",
            "cyouten_henkei_set_ex:uuuuuU",
            "cyouten_henkei_start_ex:uU",
            "easy_s_e_set:uuuuuuuuuuuuuU",
            "sound_flag_re_set:u",
            "effect_uv_set:uUUUU",
            "player_change_set:u",
            "player_poison_check:u",
            "add_object_se:uuuUUUU",
            "rand_test:uUU",
            "event_com_set:u",
            "zombie_up_death_check:uU",
            "face_pause_set:uuu",
            "face_re_set:u",
            "effect_mode_set:uuu",
            "bg_se_off_2:u",
            "bgm_off_2:u",
            "bg_se_on_2:uU",
            "bgm_on_2:uuu",
            "effect_sensya_set:u",
            "effect_kokuen_set:uUUU",
            "effect_sand_set:uuuUUUuu",
            "enemy_hp_up:uuu",
            "face_rep:uuu",
            "movie_check:uuuuu",
            "set_item_motion:u",
            "object_aspd_set:uuu",
            "puru_puru_flag_set:u",
            "puru_puru_start:u",
            "map_system_on:uuuuu",
            "set_trap_damage:u",
            "event_lighter_fire_set:uuu",
            "object_link_set_ply_item:uuuuuUUU",
            "player_kaidan_motion:uuu",
            "enemy_render_set:uuu",
            "bgm_on_ex:uuu",
            "bgm_on_2_ex:uuu",
            "fog_parameter_c_set:uuuuuuuuu",
            "fog_parameter_start:u",
            "effect_uv_set_2:uUUUU",
            "bg_color_set:uuuuu",
            "movie_time_check:uUuu",
            "effect_type_set:uuu",
            "player_poison_2_cr:u",
            "ply_hand_change:uuu",
            "h_effect_set_2:uuuUUUuuuuUUUU",
            "object_dpos_check:uuuuu",
            "item_get_get:uuu",
            "etc_atari_enemy_pos_set:uuuuuuu",
            "etc_atari_event_pos_set:uuu",
            "load_work_ex:uuu",
            "room_sound_case:u",
            "item_player_to_box:u",
            "item_box_to_player:t",
            "grd_pos_set:uuuUUU",
            "grd_pos_move_c_set:uUUUuuUUU",
            "grd_pos_move_start:u",
            "event_kill:p",
            "re_try_point_set:u",
            "ply_dpos_check:uuuuu",
            "player_item_lost_ex:uuu",
            "cyodan_set_ex:uuuuuuuuuUUUuuUUUuuuu",
            "arms_item_set:u",
            "item_get_get_ex:uuuU",
            "effect_sand_set_matsumoto:uuu",
            "voice_wait:uUuuuu",
            "voice_start:u",
            "game_over_set:u",
            "player_item_change_m:ttu",
            "effect_baku_drm_set:uUUU",
            "player_item_tama:tU",
            "effect_clear_event:u",
            "event_timer_set:u",
            "enemy_look_flag_set:uuu",
            "return_title_event:u",
            "syukan_mode_set:u",
            "ex_game_item_init:u",
            "set_enemy_life_m:uU",
            "set_effect_ssize:uuuUUU",
            "set_effect_link_offset:uuuUUU",
            "ranking_call:u",
            "call_sys_se:uU",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "nop",
            "e_while_2",
            "com_next",
            "nop",
            "nop",
            "nop",
            "sleep:xU",
            "sleeping:uu",
            "for:uU",
            "next:u",
            "while:u",
            "e_while",
            "event_next",
            "event_end:u",
        };

        private static string[] g_itemNames = new[]
        {
            "None",
            "RocketLauncher",
            "AssaultRifle",
            "SniperRifle",
            "Shotgun",
            "HandgunGlock17",
            "GrenadeLauncher",
            "BowGun",
            "CombatKnife",
            "Handgun",
            "CustomHandgun",
            "LinearLauncher",
            "HandgunBullets",
            "MagnumBullets",
            "ShotgunShells",
            "GrenadeRounds",
            "AcidRounds",
            "FlameRounds",
            "BowGunArrows",
            "M93RPart",
            "FAidSpray",
            "GreenHerb",
            "RedHerb",
            "BlueHerb",
            "MixedHerb2Green",
            "MixedHerbRedGreen",
            "MixedHerbBlueGreen",
            "MixedHerb2GreenBlue",
            "MixedHerb3Green",
            "MixedHerbGreenBlueRed",
            "MagnumBulletsInsideCase",
            "InkRibbon",
            "Magnum",
            "GoldLugers",
            "SubMachineGun",
            "BowGunPowder",
            "GunPowderArrow",
            "BOWGasRounds",
            "MGunBullets",
            "GasMask",
            "RifleBullets",
            "DuraluminCaseUnused",
            "ARifleBullets",
            "AlexandersPierce",
            "AlexandersJewel",
            "AlfredsRing",
            "AlfredsJewel",
            "PrisonersDiary",
            "DirectorsMemo",
            "Instructions",
            "Lockpick",
            "GlassEye",
            "PianoRoll",
            "SteeringWheel",
            "CraneKey",
            "Lighter",
            "EaglePlate",
            "SidePack",
            "MapRoll",
            "HawkEmblem",
            "QueenAntObject",
            "KingAntObject",
            "BiohazardCard",
            "DuraluminCaseM93RParts",
            "Detonator",
            "ControlLever",
            "GoldDragonfly",
            "SilverKey",
            "GoldKey",
            "ArmyProof",
            "NavyProof",
            "AirForceProof",
            "KeyWithTag",
            "IDCard",
            "Map",
            "AirportKey",
            "EmblemCard",
            "SkeletonPicture",
            "MusicBoxPlate",
            "GoldDragonflyNoWings",
            "Album",
            "Halberd",
            "Extinguisher",
            "Briefcase",
            "PadlockKey",
            "TG01",
            "SpAlloyEmblem",
            "ValveHandle",
            "OctaValveHandle",
            "MachineRoomKey",
            "MiningRoomKey",
            "BarCodeSticker",
            "SterileRoomKey",
            "DoorKnob",
            "BatteryPack",
            "HemostaticWire",
            "TurnTableKey",
            "ChemStorageKey",
            "ClementAlpha",
            "ClementSigma",
            "TankObject",
            "SpAlloyEmblemUnused",
            "AlfredsMemo",
            "RustedSword",
            "Hemostatic",
            "SecurityCard",
            "SecurityFile",
            "AlexiasChoker",
            "AlexiasJewel",
            "QueenAntRelief",
            "KingAntRelief",
            "RedJewel",
            "BlueJewel",
            "Socket",
            "SqValveHandle",
            "Serum",
            "EarthenwareVase",
            "PaperWeight",
            "SilverDragonflyNoWings",
            "SilverDragonfly",
            "WingObject",
            "Crystal",
            "GoldDragonfly1Wing",
            "GoldDragonfly2Wings",
            "GoldDragonfly3Wings",
            "File",
            "PlantPot",
            "PictureB",
            "DuraluminCaseBowGunPowder",
            "DuraluminCaseMagnumRounds",
            "BowGunPowderUnused",
            "EnhancedHandgun",
            "Memo",
            "BoardClip",
            "Card",
            "NewspaperClip",
            "LugerReplica",
            "QueenAntReliefComplete",
            "FamilyPicture",
            "FileFolders",
            "RemoteController",
            "QuestionA",
            "M1P",
            "CalicoBullets",
            "ClementMixture",
            "PlayingManual",
            "QuestionB",
            "QuestionC",
            "QuestionD",
            "EmptyExtinguisher",
            "SquareSocket",
            "QuestionE",
            "CrestKeyS",
            "CrestKeyG"
        };
    }
}
