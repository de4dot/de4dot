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

namespace de4dot.blocks.cflow {
	// Removes dead code that is the result of one of our optimizations, or created by the
	// obfuscator.
	class DeadCodeRemover : BlockDeobfuscator {
		List<int> allDeadInstructions = new List<int>();
		InstructionExpressionFinder instructionExpressionFinder = new InstructionExpressionFinder();

		protected override bool Deobfuscate(Block block) {
			allDeadInstructions.Clear();

			bool modified = false;
			var instructions = block.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instr = instructions[i];
				switch (instr.OpCode.Code) {
				case Code.Nop:
					// The NOP is recreated if the block is empty so don't remove it if it's
					// the only instruction.
					if (instructions.Count > 1)
						allDeadInstructions.Add(i);
					break;

				case Code.Dup:
					if (i + 1 >= instructions.Count)
						break;
					if (instructions[i + 1].OpCode.Code != Code.Pop)
						break;
					allDeadInstructions.Add(i);
					allDeadInstructions.Add(i + 1);
					i++;
					break;

				case Code.Leave:
				case Code.Leave_S:
				case Code.Endfinally:
				case Code.Pop:
					instructionExpressionFinder.Initialize(block, false);
					if (!instructionExpressionFinder.Find(i))
						continue;
					if (!OkInstructions(block, instructionExpressionFinder.DeadInstructions))
						continue;
					allDeadInstructions.AddRange(instructionExpressionFinder.DeadInstructions);
					break;

				default:
					break;
				}
			}

			if (allDeadInstructions.Count > 0) {
				block.Remove(allDeadInstructions);
				modified = true;
			}

			return modified;
		}

		bool OkInstructions(Block block, IEnumerable<int> indexes) {
			foreach (var index in indexes) {
				var instr = block.Instructions[index];
				switch (instr.OpCode.Code) {
				case Code.Add:
				case Code.Add_Ovf:
				case Code.Add_Ovf_Un:
				case Code.And:
				case Code.Arglist:
				case Code.Beq:
				case Code.Beq_S:
				case Code.Bge:
				case Code.Bge_S:
				case Code.Bge_Un:
				case Code.Bge_Un_S:
				case Code.Bgt:
				case Code.Bgt_S:
				case Code.Bgt_Un:
				case Code.Bgt_Un_S:
				case Code.Ble:
				case Code.Ble_S:
				case Code.Ble_Un:
				case Code.Ble_Un_S:
				case Code.Blt:
				case Code.Blt_S:
				case Code.Blt_Un:
				case Code.Blt_Un_S:
				case Code.Bne_Un:
				case Code.Bne_Un_S:
				case Code.Box:
				case Code.Br:
				case Code.Br_S:
				case Code.Break:
				case Code.Brfalse:
				case Code.Brfalse_S:
				case Code.Brtrue:
				case Code.Brtrue_S:
				case Code.Castclass:
				case Code.Ceq:
				case Code.Cgt:
				case Code.Cgt_Un:
				case Code.Ckfinite:
				case Code.Clt:
				case Code.Clt_Un:
				case Code.Constrained:
				case Code.Conv_I:
				case Code.Conv_I1:
				case Code.Conv_I2:
				case Code.Conv_I4:
				case Code.Conv_I8:
				case Code.Conv_Ovf_I:
				case Code.Conv_Ovf_I1:
				case Code.Conv_Ovf_I1_Un:
				case Code.Conv_Ovf_I2:
				case Code.Conv_Ovf_I2_Un:
				case Code.Conv_Ovf_I4:
				case Code.Conv_Ovf_I4_Un:
				case Code.Conv_Ovf_I8:
				case Code.Conv_Ovf_I8_Un:
				case Code.Conv_Ovf_I_Un:
				case Code.Conv_Ovf_U:
				case Code.Conv_Ovf_U1:
				case Code.Conv_Ovf_U1_Un:
				case Code.Conv_Ovf_U2:
				case Code.Conv_Ovf_U2_Un:
				case Code.Conv_Ovf_U4:
				case Code.Conv_Ovf_U4_Un:
				case Code.Conv_Ovf_U8:
				case Code.Conv_Ovf_U8_Un:
				case Code.Conv_Ovf_U_Un:
				case Code.Conv_R4:
				case Code.Conv_R8:
				case Code.Conv_R_Un:
				case Code.Conv_U:
				case Code.Conv_U1:
				case Code.Conv_U2:
				case Code.Conv_U4:
				case Code.Conv_U8:
				case Code.Div:
				case Code.Div_Un:
				case Code.Dup:
				case Code.Endfilter:
				case Code.Endfinally:
				case Code.Isinst:
				case Code.Jmp:
				case Code.Ldarg:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
				case Code.Ldarg_S:
				case Code.Ldarga:
				case Code.Ldarga_S:
				case Code.Ldc_I4:
				case Code.Ldc_I4_0:
				case Code.Ldc_I4_1:
				case Code.Ldc_I4_2:
				case Code.Ldc_I4_3:
				case Code.Ldc_I4_4:
				case Code.Ldc_I4_5:
				case Code.Ldc_I4_6:
				case Code.Ldc_I4_7:
				case Code.Ldc_I4_8:
				case Code.Ldc_I4_M1:
				case Code.Ldc_I4_S:
				case Code.Ldc_I8:
				case Code.Ldc_R4:
				case Code.Ldc_R8:
				case Code.Ldelem:
				case Code.Ldelem_I:
				case Code.Ldelem_I1:
				case Code.Ldelem_I2:
				case Code.Ldelem_I4:
				case Code.Ldelem_I8:
				case Code.Ldelem_R4:
				case Code.Ldelem_R8:
				case Code.Ldelem_Ref:
				case Code.Ldelem_U1:
				case Code.Ldelem_U2:
				case Code.Ldelem_U4:
				case Code.Ldelema:
				case Code.Ldfld:
				case Code.Ldflda:
				case Code.Ldftn:
				case Code.Ldind_I:
				case Code.Ldind_I1:
				case Code.Ldind_I2:
				case Code.Ldind_I4:
				case Code.Ldind_I8:
				case Code.Ldind_R4:
				case Code.Ldind_R8:
				case Code.Ldind_Ref:
				case Code.Ldind_U1:
				case Code.Ldind_U2:
				case Code.Ldind_U4:
				case Code.Ldlen:
				case Code.Ldloc:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
				case Code.Ldloc_S:
				case Code.Ldloca:
				case Code.Ldloca_S:
				case Code.Ldnull:
				case Code.Ldobj:
				case Code.Ldsfld:
				case Code.Ldsflda:
				case Code.Ldstr:
				case Code.Ldtoken:
				case Code.Ldvirtftn:
				case Code.Leave:
				case Code.Leave_S:
				case Code.Localloc:
				case Code.Mkrefany:
				case Code.Mul:
				case Code.Mul_Ovf:
				case Code.Mul_Ovf_Un:
				case Code.Neg:
				case Code.Newarr:
				case Code.Nop:
				case Code.Not:
				case Code.Or:
				case Code.Pop:
				case Code.Readonly:
				case Code.Refanytype:
				case Code.Refanyval:
				case Code.Rem:
				case Code.Rem_Un:
				case Code.Ret:
				case Code.Rethrow:
				case Code.Shl:
				case Code.Shr:
				case Code.Shr_Un:
				case Code.Sizeof:
				case Code.Sub:
				case Code.Sub_Ovf:
				case Code.Sub_Ovf_Un:
				case Code.Switch:
				case Code.Tailcall:
				case Code.Throw:
				case Code.Unaligned:
				case Code.Unbox:
				case Code.Unbox_Any:
				case Code.Volatile:
				case Code.Xor:
					break;

				case Code.Call:
				case Code.Calli:
				case Code.Callvirt:
				case Code.Cpblk:
				case Code.Cpobj:
				case Code.Initblk:
				case Code.Initobj:
				case Code.Newobj:
				case Code.Starg:
				case Code.Starg_S:
				case Code.Stelem:
				case Code.Stelem_I:
				case Code.Stelem_I1:
				case Code.Stelem_I2:
				case Code.Stelem_I4:
				case Code.Stelem_I8:
				case Code.Stelem_R4:
				case Code.Stelem_R8:
				case Code.Stelem_Ref:
				case Code.Stfld:
				case Code.Stind_I:
				case Code.Stind_I1:
				case Code.Stind_I2:
				case Code.Stind_I4:
				case Code.Stind_I8:
				case Code.Stind_R4:
				case Code.Stind_R8:
				case Code.Stind_Ref:
				case Code.Stloc:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
				case Code.Stloc_S:
				case Code.Stobj:
				case Code.Stsfld:
				default:
					return false;
				}
			}

			return true;
		}

		class InstructionExpressionFinder {
			List<int> deadInstructions = new List<int>();
			Block block;
			bool methodHasReturnValue;

			public List<int> DeadInstructions => deadInstructions;

			public void Initialize(Block block, bool methodHasReturnValue) {
				deadInstructions.Clear();
				this.block = block;
				this.methodHasReturnValue = methodHasReturnValue;
			}

			public bool Find(int index) => Find(ref index, true);

			bool Find(ref int index, bool addIt) {
				if (index < 0)
					return false;

				var startInstr = block.Instructions[index];
				CalculateStackUsage(startInstr.Instruction, false, out int startInstrPushes, out int startInstrPops);

				// Don't add it if it clears the stack (eg. leave)
				if (addIt && startInstrPops >= 0)
					AddIndex(index);

				if (startInstrPops == 0)
					return true;

				while (index > 0) {
					var instr = block.Instructions[index - 1];
					if (startInstrPops == 0 && instr.OpCode.OpCodeType != OpCodeType.Prefix)
						break;

					CalculateStackUsage(instr.Instruction, methodHasReturnValue, out int pushes, out int pops);
					if (pops < 0)
						break;	// eg. leave
					index--;

					if (pops > 0) {	// if instr uses any args
						bool otherExpr = pops > 0 && pushes == 0;
						if (!Find(ref index, addIt && !otherExpr))
							break;
					}
					else if (pushes != 0 || pops != 0) {
						if (addIt)
							AddIndex(index);
					}
					if (pushes > 0 && startInstrPops >= 0) {
						if (pushes > startInstrPops)
							return false;
						startInstrPops -= pushes;
					}
				}

				return startInstrPops <= 0;
			}

			void AddIndex(int index) => deadInstructions.Add(index);
		}

		static void CalculateStackUsage(Instruction instr, bool methodHasReturnValue, out int pushes, out int pops) =>
			instr.CalculateStackUsage(false, out pushes, out pops);
	}
}
