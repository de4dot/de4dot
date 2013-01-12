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
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.blocks.cflow {
	class SwitchCflowDeobfuscator : BlockDeobfuscator {
		InstructionEmulator instructionEmulator = new InstructionEmulator();

		protected override bool deobfuscate(Block switchBlock) {
			if (switchBlock.LastInstr.OpCode.Code != Code.Switch)
				return false;

			if (isSwitchTopOfStack(switchBlock) && deobfuscateTos(switchBlock))
				return true;

			if (isLdlocBranch(switchBlock, true) && deobfuscateLdloc(switchBlock))
				return true;

			if (isStLdlocBranch(switchBlock, true) && deobfuscateStLdloc(switchBlock))
				return true;

			if (isSwitchType1(switchBlock) && deobfuscateType1(switchBlock))
				return true;

			if (isSwitchType2(switchBlock) && deobfuscateType2(switchBlock))
				return true;

			if (switchBlock.FirstInstr.isLdloc() && fixSwitchBranch(switchBlock))
				return true;

			return false;
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

		bool isSwitchType2(Block switchBlock) {
			Local local = null;
			foreach (var instr in switchBlock.Instructions) {
				if (!instr.isLdloc())
					continue;
				local = Instr.getLocalVar(blocks.Locals, instr);
				break;
			}
			if (local == null)
				return false;

			foreach (var source in switchBlock.Sources) {
				var instrs = source.Instructions;
				for (int i = 1; i < instrs.Count; i++) {
					var ldci4 = instrs[i - 1];
					if (!ldci4.isLdcI4())
						continue;
					var stloc = instrs[i];
					if (!stloc.isStloc())
						continue;
					if (Instr.getLocalVar(blocks.Locals, stloc) != local)
						continue;

					return true;
				}
			}

			return false;
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
				instructionEmulator.init(blocks);
				instructionEmulator.emulate(source.Instructions);

				var target = getSwitchTarget(switchTargets, switchFallThrough, instructionEmulator.pop());
				if (target == null)
					continue;
				source.replaceLastNonBranchWithBranch(0, target);
				source.add(new Instr(OpCodes.Pop.ToInstruction()));
				changed = true;
			}
			return changed;
		}

		// Switch deobfuscation when block uses ldloc N to load switch constant
		//	blk1:
		//		ldc.i4 X
		//		stloc N
		//		br swblk / bcc swblk
		//	swblk:
		//		ldloc N
		//		switch (......)
		bool deobfuscateLdloc(IList<Block> switchTargets, Block switchFallThrough, Block block, Local switchVariable) {
			bool changed = false;
			foreach (var source in new List<Block>(block.Sources)) {
				if (isBranchBlock(source)) {
					instructionEmulator.init(blocks);
					instructionEmulator.emulate(source.Instructions);

					var target = getSwitchTarget(switchTargets, switchFallThrough, instructionEmulator.getLocal(switchVariable));
					if (target == null)
						continue;
					source.replaceLastNonBranchWithBranch(0, target);
					changed = true;
				}
				else if (isBccBlock(source)) {
					instructionEmulator.init(blocks);
					instructionEmulator.emulate(source.Instructions);

					var target = getSwitchTarget(switchTargets, switchFallThrough, instructionEmulator.getLocal(switchVariable));
					if (target == null)
						continue;
					if (source.Targets[0] == block) {
						source.setNewTarget(0, target);
						changed = true;
					}
					if (source.FallThrough == block) {
						source.setNewFallThrough(target);
						changed = true;
					}
				}
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
				instructionEmulator.init(blocks);
				instructionEmulator.emulate(source.Instructions);

				var target = getSwitchTarget(switchTargets, switchFallThrough, instructionEmulator.pop());
				if (target == null) {
					changed |= deobfuscateTos_Ldloc(switchTargets, switchFallThrough, source);
				}
				else {
					source.replaceLastNonBranchWithBranch(0, target);
					source.add(new Instr(OpCodes.Pop.ToInstruction()));
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

		static bool isBranchBlock(Block block) {
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

		static bool isBccBlock(Block block) {
			if (block.Targets == null || block.Targets.Count != 1)
				return false;
			if (block.FallThrough == null)
				return false;
			switch (block.LastInstr.OpCode.Code) {
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
			case Code.Brfalse:
			case Code.Brfalse_S:
			case Code.Brtrue:
			case Code.Brtrue_S:
				return true;
			default:
				return false;
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

		bool deobfuscateType2(Block switchBlock) {
			bool changed = false;

			var bccSources = new List<Block>();
			foreach (var source in new List<Block>(switchBlock.Sources)) {
				if (source.LastInstr.isConditionalBranch()) {
					bccSources.Add(source);
					continue;
				}
				if (!source.canAppend(switchBlock))
					continue;
				if (!willHaveKnownTarget(switchBlock, source))
					continue;

				source.append(switchBlock);
				changed = true;
			}

			foreach (var bccSource in bccSources) {
				if (!willHaveKnownTarget(switchBlock, bccSource))
					continue;
				var consts = getBccLocalConstants(bccSource);
				if (consts.Count == 0)
					continue;
				var newFallThrough = createBlock(consts, bccSource.FallThrough);
				var newTarget = createBlock(consts, bccSource.Targets[0]);
				var oldFallThrough = bccSource.FallThrough;
				var oldTarget = bccSource.Targets[0];
				bccSource.setNewFallThrough(newFallThrough);
				bccSource.setNewTarget(0, newTarget);
				newFallThrough.setNewFallThrough(oldFallThrough);
				newTarget.setNewFallThrough(oldTarget);
				changed = true;
			}

			return changed;
		}

		static Block createBlock(Dictionary<Local, int> consts, Block fallThrough) {
			var block = new Block();
			foreach (var kv in consts) {
				block.Instructions.Add(new Instr(Instruction.CreateLdcI4(kv.Value)));
				block.Instructions.Add(new Instr(OpCodes.Stloc.ToInstruction(kv.Key)));
			}
			fallThrough.Parent.add(block);
			return block;
		}

		Dictionary<Local, int> getBccLocalConstants(Block block) {
			var dict = new Dictionary<Local, int>();
			var instrs = block.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.isStloc()) {
					var local = Instr.getLocalVar(blocks.Locals, instr);
					if (local == null)
						continue;
					var ldci4 = i == 0 ? null : instrs[i - 1];
					if (ldci4 == null || !ldci4.isLdcI4())
						dict.Remove(local);
					else
						dict[local] = ldci4.getLdcI4Value();
				}
				else if (instr.isLdloc()) {
					var local = Instr.getLocalVar(blocks.Locals, instr);
					if (local != null)
						dict.Remove(local);
				}
				else if (instr.OpCode.Code == Code.Ldloca || instr.OpCode.Code == Code.Ldloca_S) {
					var local = instr.Operand as Local;
					if (local != null)
						dict.Remove(local);
				}
			}
			return dict;
		}

		bool emulateGetTarget(Block switchBlock, out Block target) {
			instructionEmulator.init(blocks);
			try {
				instructionEmulator.emulate(switchBlock.Instructions, 0, switchBlock.Instructions.Count - 1);
			}
			catch (NullReferenceException) {
				// Here if eg. invalid metadata token in a call instruction (operand is null)
				target = null;
				return false;
			}
			target = getTarget(switchBlock);
			return true;
		}

		bool willHaveKnownTarget(Block switchBlock, Block source) {
			instructionEmulator.init(blocks);
			try {
				instructionEmulator.emulate(source.Instructions);
				instructionEmulator.emulate(switchBlock.Instructions, 0, switchBlock.Instructions.Count - 1);
			}
			catch (NullReferenceException) {
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

		static Block getSwitchTarget(IList<Block> targets, Block fallThrough, Value value) {
			if (!value.isInt32())
				return null;
			return CflowUtils.getSwitchTarget(targets, fallThrough, (Int32Value)value);
		}

		static bool fixSwitchBranch(Block switchBlock) {
			// Code:
			//	blk1:
			//		ldc.i4 XXX
			//		br common
			//	blk2:
			//		ldc.i4 YYY
			//		br common
			//	common:
			//		stloc X
			//		br swblk
			//	swblk:
			//		ldloc X
			//		switch
			// Inline common into blk1 and blk2.

			bool changed = false;

			foreach (var commonSource in new List<Block>(switchBlock.Sources)) {
				if (commonSource.Instructions.Count != 1)
					continue;
				if (!commonSource.FirstInstr.isStloc())
					continue;
				foreach (var blk in new List<Block>(commonSource.Sources)) {
					if (blk.canAppend(commonSource)) {
						blk.append(commonSource);
						changed = true;
					}
				}
			}

			return changed;
		}
	}
}
