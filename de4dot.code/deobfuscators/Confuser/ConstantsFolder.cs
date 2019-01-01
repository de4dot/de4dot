/*
    Copyright (C) 2011-2015 de4dot@gmail.com

    This file is part of de4dot.

    de4dot is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    de4dot is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with de4dot.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using dnlib.DotNet.Emit;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Confuser {
	class ConstantsFolder : BlockDeobfuscator {
		protected override bool Deobfuscate(Block block) {
			bool modified = false;

			var instrs = block.Instructions;
			var constantsReader = CreateConstantsReader(instrs);
			for (int i = 0; i < instrs.Count; i++) {
				int index = 0;
				Instruction newInstr = null;
				var instr = instrs[i];
				if (constantsReader.IsLoadConstantInt32(instr.Instruction)) {
					index = i;
					if (!constantsReader.GetInt32(ref index, out int val))
						continue;
					newInstr = Instruction.CreateLdcI4(val);
				}
				else if (constantsReader.IsLoadConstantInt64(instr.Instruction)) {
					index = i;
					if (!constantsReader.GetInt64(ref index, out long val))
						continue;
					newInstr = Instruction.Create(OpCodes.Ldc_I8, val);
				}
				else if (constantsReader.IsLoadConstantDouble(instr.Instruction)) {
					index = i;
					if (!constantsReader.GetDouble(ref index, out double val))
						continue;
					newInstr = Instruction.Create(OpCodes.Ldc_R8, val);
				}

				if (newInstr != null && index - i > 1) {
					block.Insert(index++, Instruction.Create(OpCodes.Pop));
					block.Insert(index++, newInstr);
					i = index - 1;
					constantsReader = CreateConstantsReader(instrs);
					modified = true;
					continue;
				}

				// Convert ldc.r4/r8 followed by conv to the appropriate ldc.i4/i8 instr
				if (i + 1 < instrs.Count && (instr.OpCode.Code == Code.Ldc_R4 || instr.OpCode.Code == Code.Ldc_R8)) {
					var conv = instrs[i + 1];
					/*int vali32 = instr.OpCode.Code == Code.Ldc_R4 ? (int)(float)instr.Operand : (int)(double)instr.Operand;
					long vali64 = instr.OpCode.Code == Code.Ldc_R4 ? (long)(float)instr.Operand : (long)(double)instr.Operand;
					uint valu32 = instr.OpCode.Code == Code.Ldc_R4 ? (uint)(float)instr.Operand : (uint)(double)instr.Operand;
					ulong valu64 = instr.OpCode.Code == Code.Ldc_R4 ? (ulong)(float)instr.Operand : (ulong)(double)instr.Operand;*/
					switch (conv.OpCode.Code) {
					case Code.Conv_I1:
						newInstr = Instruction.CreateLdcI4(instr.OpCode.Code == Code.Ldc_R4 ? (sbyte)(float)instr.Operand : (sbyte)(double)instr.Operand);
						break;
					case Code.Conv_U1:
						newInstr = Instruction.CreateLdcI4(instr.OpCode.Code == Code.Ldc_R4 ? (byte)(float)instr.Operand : (byte)(double)instr.Operand);
						break;
					case Code.Conv_I2:
						newInstr = Instruction.CreateLdcI4(instr.OpCode.Code == Code.Ldc_R4 ? (short)(float)instr.Operand : (short)(double)instr.Operand);
						break;
					case Code.Conv_U2:
						newInstr = Instruction.CreateLdcI4(instr.OpCode.Code == Code.Ldc_R4 ? (ushort)(float)instr.Operand : (ushort)(double)instr.Operand);
						break;
					case Code.Conv_I4:
						newInstr = Instruction.CreateLdcI4(instr.OpCode.Code == Code.Ldc_R4 ? (int)(float)instr.Operand : (int)(double)instr.Operand);
						break;
					case Code.Conv_U4:
						newInstr = Instruction.CreateLdcI4(instr.OpCode.Code == Code.Ldc_R4 ? (int)(uint)(float)instr.Operand : (int)(uint)(double)instr.Operand);
						break;
					case Code.Conv_I8:
						newInstr = Instruction.Create(OpCodes.Ldc_I8, instr.OpCode.Code == Code.Ldc_R4 ? (long)(float)instr.Operand : (long)(double)instr.Operand);
						break;
					case Code.Conv_U8:
						newInstr = Instruction.Create(OpCodes.Ldc_I8, instr.OpCode.Code == Code.Ldc_R4 ? (ulong)(float)instr.Operand : (ulong)(double)instr.Operand);
						break;
					default:
						newInstr = null;
						break;
					}
					if (newInstr != null) {
						block.Replace(i, 2, newInstr);
						constantsReader = CreateConstantsReader(instrs);
						modified = true;
						continue;
					}
				}
			}

			return modified;
		}

		static ConstantsReader CreateConstantsReader(IList<Instr> instrs) => new ConstantsReader(instrs, false);
	}
}
