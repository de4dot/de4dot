/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using dnlib.DotNet.Emit;

namespace de4dot.blocks.cflow {
	class BlockCflowDeobfuscator : BlockDeobfuscator, IBranchHandler {
		Block block;
		InstructionEmulator instructionEmulator;
		BranchEmulator branchEmulator;

		public BlockCflowDeobfuscator() {
			instructionEmulator = new InstructionEmulator();
			branchEmulator = new BranchEmulator(instructionEmulator, this);
		}

		protected override bool deobfuscate(Block block) {
			this.block = block;
			if (!block.LastInstr.isConditionalBranch() && block.LastInstr.OpCode.Code != Code.Switch)
				return false;
			instructionEmulator.init(blocks);

			var instructions = block.Instructions;
			if (instructions.Count == 0)
				return false;
			try {
				for (int i = 0; i < instructions.Count - 1; i++) {
					var instr = instructions[i].Instruction;
					instructionEmulator.emulate(instr);
				}
			}
			catch (NullReferenceException) {
				// Here if eg. invalid metadata token in a call instruction (operand is null)
				return false;
			}

			return branchEmulator.emulate(block.LastInstr.Instruction);
		}

		void popPushedArgs(int stackArgs) {
			// Pop the arguments to the bcc instruction. The dead code remover will get rid of the
			// pop and any pushed arguments. Insert the pops just before the bcc instr.
			for (int i = 0; i < stackArgs; i++)
				block.insert(block.Instructions.Count - 1, OpCodes.Pop.ToInstruction());
		}

		void IBranchHandler.handleNormal(int stackArgs, bool isTaken) {
			popPushedArgs(stackArgs);
			block.replaceBccWithBranch(isTaken);
		}

		bool IBranchHandler.handleSwitch(Int32Value switchIndex) {
			var target = CflowUtils.getSwitchTarget(block.Targets, block.FallThrough, switchIndex);
			if (target == null)
				return false;

			popPushedArgs(1);
			block.replaceSwitchWithBranch(target);
			return true;
		}
	}
}
