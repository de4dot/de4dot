/*
    Copyright (C) 2011 de4dot@gmail.com

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
using Mono.Cecil.Cil;

namespace de4dot.blocks.cflow {
	class SwitchCflowDeobfuscator {
		List<Block> allBlocks;
		Blocks blocks;
		InstructionEmulator instructionEmulator = new InstructionEmulator();

		public void init(Blocks blocks, List<Block> allBlocks) {
			this.blocks = blocks;
			this.allBlocks = allBlocks;
		}

		public bool deobfuscate() {
			bool changed = false;
			foreach (var switchBlock in allBlocks) {
				if (switchBlock.LastInstr.OpCode.Code != Code.Switch)
					continue;
				if (isSwitchTopOfStack(switchBlock))
					changed |= topOfstackDeobfuscate(switchBlock);
				else if (isSwitchLocal(switchBlock))
					changed |= localDeobfuscate(switchBlock);
			}
			return changed;
		}

		bool isSwitchTopOfStack(Block switchBlock) {
			return switchBlock.Instructions.Count == 1;
		}

		bool topOfstackDeobfuscate(Block switchBlock) {
			bool changed = false;
			if (switchBlock.Targets == null)
				return changed;
			var targets = new List<Block>(switchBlock.Targets);
			foreach (var source in new List<Block>(switchBlock.Sources)) {
				if (!isBranchBlock(source))
					continue;
				instructionEmulator.init(false, blocks.Method.Parameters, blocks.Locals);
				instructionEmulator.emulate(source.Instructions);

				var target = getSwitchTarget(targets, switchBlock.FallThrough, source, instructionEmulator.pop());
				if (target == null)
					continue;
				source.replaceLastNonBranchWithBranch(0, target);
				source.add(new Instr(Instruction.Create(OpCodes.Pop)));
				changed = true;
			}
			return changed;
		}

		bool isSwitchLocal(Block switchBlock) {
			return switchBlock.Instructions.Count == 2 && switchBlock.Instructions[0].isLdloc();
		}

		bool localDeobfuscate(Block switchBlock) {
			bool changed = false;

			var switchVariable = Instr.getLocalVar(blocks.Locals, switchBlock.Instructions[0]);
			if (switchVariable == null)
				return changed;

			if (switchBlock.Targets == null)
				return changed;
			var targets = new List<Block>(switchBlock.Targets);
			foreach (var source in new List<Block>(switchBlock.Sources)) {
				if (!isBranchBlock(source))
					continue;
				instructionEmulator.init(false, blocks.Method.Parameters, blocks.Locals);
				instructionEmulator.emulate(source.Instructions);

				var target = getSwitchTarget(targets, switchBlock.FallThrough, source, instructionEmulator.getLocal(switchVariable));
				if (target == null)
					continue;
				source.replaceLastNonBranchWithBranch(0, target);
				changed = true;
			}

			return changed;
		}

		bool isBranchBlock(Block block) {
			if (block.FallThrough != null)
				return block.Targets == null || block.Targets.Count == 0;
			return block.Targets != null && block.Targets.Count == 1;
		}

		Block getSwitchTarget(IList<Block> targets, Block fallThrough, Block source, Value value) {
			if (!value.isInt32())
				return null;
			return CflowUtils.getSwitchTarget(targets, fallThrough, (Int32Value)value);
		}
	}
}
