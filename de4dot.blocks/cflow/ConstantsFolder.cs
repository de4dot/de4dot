/*
    Copyright (C) 2011-2014 de4dot@gmail.com

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

using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.blocks.cflow {
	// Very simple constants folder which is all that's needed at the moment
	class ConstantsFolder : BlockDeobfuscator {
		InstructionEmulator instructionEmulator = new InstructionEmulator();
		IList<Parameter> args;

		protected override void Initialize(List<Block> allBlocks) {
			base.Initialize(allBlocks);
			args = blocks.Method.Parameters;
		}

		protected override bool Deobfuscate(Block block) {
			bool modified = false;

			instructionEmulator.Initialize(blocks, allBlocks[0] == block);
			var instrs = block.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var instr = instrs[i];

				switch (instr.OpCode.Code) {
				case Code.Ldarg:
				case Code.Ldarg_0:
				case Code.Ldarg_1:
				case Code.Ldarg_2:
				case Code.Ldarg_3:
				case Code.Ldarg_S:
					modified |= FixLoadInstruction(block, i, instructionEmulator.GetArg(instr.Instruction.GetParameter(args)));
					break;

				case Code.Ldloc:
				case Code.Ldloc_0:
				case Code.Ldloc_1:
				case Code.Ldloc_2:
				case Code.Ldloc_3:
				case Code.Ldloc_S:
					modified |= FixLoadInstruction(block, i, instructionEmulator.GetLocal(instr.Instruction.GetLocal(blocks.Locals)));
					break;

				case Code.Ldarga:
				case Code.Ldarga_S:
					instructionEmulator.MakeArgUnknown((Parameter)instr.Operand);
					break;

				case Code.Ldloca:
				case Code.Ldloca_S:
					instructionEmulator.MakeLocalUnknown((Local)instr.Operand);
					break;
				}

				try {
					instructionEmulator.Emulate(instr.Instruction);
				}
				catch (NullReferenceException) {
					// Here if eg. invalid metadata token in a call instruction (operand is null)
					break;
				}
			}

			return modified;
		}

		bool FixLoadInstruction(Block block, int index, Value value) {
			if (value.IsInt32()) {
				var intValue = (Int32Value)value;
				if (!intValue.AllBitsValid())
					return false;
				block.Instructions[index] = new Instr(Instruction.CreateLdcI4(intValue.Value));
				return true;
			}
			else if (value.IsInt64()) {
				var intValue = (Int64Value)value;
				if (!intValue.AllBitsValid())
					return false;
				block.Instructions[index] = new Instr(OpCodes.Ldc_I8.ToInstruction(intValue.Value));
				return true;
			}
			return false;
		}
	}
}
