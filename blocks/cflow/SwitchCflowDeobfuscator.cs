/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
					changed |= deobfuscateTos(switchBlock);
				else if (isLdlocBranch(switchBlock, true))
					changed |= deobfuscateLdloc(switchBlock);
				else if (isStLdlocBranch(switchBlock, true))
					changed |= deobfuscateStLdloc(switchBlock);
				else if (isSwitchType1(switchBlock))
					changed |= deobfuscateType1(switchBlock);
			}
			return changed;
		}

		static bool isSwitchTopOfStack(Block switchBlock) {
			return switchBlock.Instructions.Count == 1;
		}

		static bool isLdlocBranch(Block switchBlock, bool isSwitch) {
			int numInstrs = 1 + (isSwitch ? 1 : 0);
			return switchBlock.Instructions.Count == numInstrs && switchBlock.Instructions[0].isLdloc();
		}

		static bool isSwitchType1(Block switchBlock) {
			return switchBlock.FirstInstr.isLdloc();
		}

		bool isStLdlocBranch(Block switchBlock, bool isSwitch) {
			int numInstrs = 2 + (isSwitch ? 1 : 0);
			return switchBlock.Instructions.Count == numInstrs &&
				switchBlock.Instructions[0].isStloc() &&
				switchBlock.Instructions[1].isLdloc() &&
				Instr.getLocalVar(blocks.Locals, switchBlock.Instructions[0]) == Instr.getLocalVar(blocks.Locals, switchBlock.Instructions[1]);
		}

		bool deobfuscateTos(Block switchBlock) {
			bool changed = false;
			if (switchBlock.Targets == null)
				return changed;
			var targets = new List<Block>(switchBlock.Targets);

			changed |= deobfuscateTos(targets, switchBlock.FallThrough, switchBlock);

			return changed;
		}

		bool deobfuscateLdloc(Block switchBlock) {
			bool changed = false;

			var switchVariable = Instr.getLocalVar(blocks.Locals, switchBlock.Instructions[0]);
			if (switchVariable == null)
				return changed;

			if (switchBlock.Targets == null)
				return changed;
			var targets = new List<Block>(switchBlock.Targets);

			changed |= deobfuscateLdloc(targets, switchBlock.FallThrough, switchBlock, switchVariable);

			return changed;
		}

		bool deobfuscateStLdloc(Block switchBlock) {
			bool changed = false;

			var switchVariable = Instr.getLocalVar(blocks.Locals, switchBlock.Instructions[0]);
			if (switchVariable == null)
				return changed;

			if (switchBlock.Targets == null)
				return changed;
			var targets = new List<Block>(switchBlock.Targets);

			changed |= deobfuscateStLdloc(targets, switchBlock.FallThrough, switchBlock);

			return changed;
		}

		// Switch deobfuscation when block uses stloc N, ldloc N to load switch constant
		//	blk1:
		//		ldc.i4 X
		//		br swblk
		//	swblk:
		//		stloc N
		//		ldloc N
		//		switch (......)
		bool deobfuscateStLdloc(IList<Block> switchTargets, Block switchFallThrough, Block block) {
			bool changed = false;
			foreach (var source in new List<Block>(block.Sources)) {
				if (!isBranchBlock(source))
					continue;
				instructionEmulator.init(blocks.Method.HasThis, false, blocks.Method.Parameters, blocks.Locals);
				instructionEmulator.emulate(source.Instructions);

				var target = getSwitchTarget(switchTargets, switchFallThrough, source, instructionEmulator.pop());
				if (target == null)
					continue;
				source.replaceLastNonBranchWithBranch(0, target);
				source.add(new Instr(Instruction.Create(OpCodes.Pop)));
				changed = true;
			}
			return changed;
		}

		// Switch deobfuscation when block uses ldloc N to load switch constant
		//	blk1:
		//		ldc.i4 X
		//		stloc N
		//		br swblk
		//	swblk:
		//		ldloc N
		//		switch (......)
		bool deobfuscateLdloc(IList<Block> switchTargets, Block switchFallThrough, Block block, VariableDefinition switchVariable) {
			bool changed = false;
			foreach (var source in new List<Block>(block.Sources)) {
				if (!isBranchBlock(source))
					continue;
				instructionEmulator.init(blocks.Method.HasThis, false, blocks.Method.Parameters, blocks.Locals);
				instructionEmulator.emulate(source.Instructions);

				var target = getSwitchTarget(switchTargets, switchFallThrough, source, instructionEmulator.getLocal(switchVariable));
				if (target == null)
					continue;
				source.replaceLastNonBranchWithBranch(0, target);
				changed = true;
			}
			return changed;
		}

		// Switch deobfuscation when block has switch contant on TOS:
		//	blk1:
		//		ldc.i4 X
		//		br swblk
		//	swblk:
		//		switch (......)
		bool deobfuscateTos(IList<Block> switchTargets, Block switchFallThrough, Block block) {
			bool changed = false;
			foreach (var source in new List<Block>(block.Sources)) {
				if (!isBranchBlock(source))
					continue;
				instructionEmulator.init(blocks.Method.HasThis, false, blocks.Method.Parameters, blocks.Locals);
				instructionEmulator.emulate(source.Instructions);

				var target = getSwitchTarget(switchTargets, switchFallThrough, source, instructionEmulator.pop());
				if (target == null) {
					changed |= deobfuscateTos_Ldloc(switchTargets, switchFallThrough, source);
				}
				else {
					source.replaceLastNonBranchWithBranch(0, target);
					source.add(new Instr(Instruction.Create(OpCodes.Pop)));
					changed = true;
				}
			}
			return changed;
		}

		//		ldloc N
		//		br swblk
		// or
		//		stloc N
		//		ldloc N
		//		br swblk
		bool deobfuscateTos_Ldloc(IList<Block> switchTargets, Block switchFallThrough, Block block) {
			if (isLdlocBranch(block, false)) {
				var switchVariable = Instr.getLocalVar(blocks.Locals, block.Instructions[0]);
				if (switchVariable == null)
					return false;
				return deobfuscateLdloc(switchTargets, switchFallThrough, block, switchVariable);
			}
			else if (isStLdlocBranch(block, false))
				return deobfuscateStLdloc(switchTargets, switchFallThrough, block);

			return false;
		}

		bool isBranchBlock(Block block) {
			if (block.Targets != null)
				return false;
			if (block.FallThrough == null)
				return false;
			switch (block.LastInstr.OpCode.Code) {
			case Code.Switch:
			case Code.Leave:
			case Code.Leave_S:
				return false;
			default:
				return true;
			}
		}

		bool deobfuscateType1(Block switchBlock) {
			Block target;
			if (!emulateGetTarget(switchBlock, out target) || target != null)
				return false;

			bool changed = false;

			foreach (var source in new List<Block>(switchBlock.Sources)) {
				if (!source.canAppend(switchBlock))
					continue;
				if (!willHaveKnownTarget(switchBlock, source))
					continue;

				source.append(switchBlock);
				changed = true;
			}

			return changed;
		}

		bool emulateGetTarget(Block switchBlock, out Block target) {
			instructionEmulator.init(blocks.Method.HasThis, false, blocks.Method.Parameters, blocks.Locals);
			try {
				instructionEmulator.emulate(switchBlock.Instructions, 0, switchBlock.Instructions.Count - 1);
			}
			catch (System.NullReferenceException) {
				// Here if eg. invalid metadata token in a call instruction (operand is null)
				target = null;
				return false;
			}
			target = getTarget(switchBlock);
			return true;
		}

		bool willHaveKnownTarget(Block switchBlock, Block source) {
			instructionEmulator.init(blocks.Method.HasThis, false, blocks.Method.Parameters, blocks.Locals);
			try {
				instructionEmulator.emulate(source.Instructions);
				instructionEmulator.emulate(switchBlock.Instructions, 0, switchBlock.Instructions.Count - 1);
			}
			catch (System.NullReferenceException) {
				// Here if eg. invalid metadata token in a call instruction (operand is null)
				return false;
			}
			return getTarget(switchBlock) != null;
		}

		Block getTarget(Block switchBlock) {
			var val1 = instructionEmulator.pop();
			if (!val1.isInt32())
				return null;
			return CflowUtils.getSwitchTarget(switchBlock.Targets, switchBlock.FallThrough, (Int32Value)val1);
		}

		Block getSwitchTarget(IList<Block> targets, Block fallThrough, Block source, Value value) {
			if (!value.isInt32())
				return null;
			return CflowUtils.getSwitchTarget(targets, fallThrough, (Int32Value)value);
		}
	}
}
