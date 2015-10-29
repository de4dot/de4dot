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

using System;
using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.blocks.cflow {
	class SwitchCflowDeobfuscator : BlockDeobfuscator {
		InstructionEmulator instructionEmulator = new InstructionEmulator();

		protected override bool Deobfuscate(Block switchBlock) {
			if (switchBlock.LastInstr.OpCode.Code != Code.Switch)
				return false;

			if (IsSwitchTopOfStack(switchBlock) && DeobfuscateTOS(switchBlock))
				return true;

			if (IsLdlocBranch(switchBlock, true) && DeobfuscateLdloc(switchBlock))
				return true;

			if (IsStLdlocBranch(switchBlock, true) && DeobfuscateStLdloc(switchBlock))
				return true;

			if (IsSwitchType1(switchBlock) && DeobfuscateType1(switchBlock))
				return true;

			if (IsSwitchType2(switchBlock) && DeobfuscateType2(switchBlock))
				return true;

			if (switchBlock.FirstInstr.IsLdloc() && FixSwitchBranch(switchBlock))
				return true;

			return false;
		}

		static bool IsSwitchTopOfStack(Block switchBlock) {
			return switchBlock.Instructions.Count == 1;
		}

		static bool IsLdlocBranch(Block switchBlock, bool isSwitch) {
			int numInstrs = 1 + (isSwitch ? 1 : 0);
			return switchBlock.Instructions.Count == numInstrs && switchBlock.Instructions[0].IsLdloc();
		}

		static bool IsSwitchType1(Block switchBlock) {
			return switchBlock.FirstInstr.IsLdloc();
		}

		bool IsSwitchType2(Block switchBlock) {
			Local local = null;
			foreach (var instr in switchBlock.Instructions) {
				if (!instr.IsLdloc())
					continue;
				local = Instr.GetLocalVar(blocks.Locals, instr);
				break;
			}
			if (local == null)
				return false;

			foreach (var source in switchBlock.Sources) {
				var instrs = source.Instructions;
				for (int i = 1; i < instrs.Count; i++) {
					var ldci4 = instrs[i - 1];
					if (!ldci4.IsLdcI4())
						continue;
					var stloc = instrs[i];
					if (!stloc.IsStloc())
						continue;
					if (Instr.GetLocalVar(blocks.Locals, stloc) != local)
						continue;

					return true;
				}
			}

			return false;
		}

		bool IsStLdlocBranch(Block switchBlock, bool isSwitch) {
			int numInstrs = 2 + (isSwitch ? 1 : 0);
			return switchBlock.Instructions.Count == numInstrs &&
				switchBlock.Instructions[0].IsStloc() &&
				switchBlock.Instructions[1].IsLdloc() &&
				Instr.GetLocalVar(blocks.Locals, switchBlock.Instructions[0]) == Instr.GetLocalVar(blocks.Locals, switchBlock.Instructions[1]);
		}

		bool DeobfuscateTOS(Block switchBlock) {
			bool modified = false;
			if (switchBlock.Targets == null)
				return modified;
			var targets = new List<Block>(switchBlock.Targets);

			modified |= DeobfuscateTOS(targets, switchBlock.FallThrough, switchBlock);

			return modified;
		}

		bool DeobfuscateLdloc(Block switchBlock) {
			bool modified = false;

			var switchVariable = Instr.GetLocalVar(blocks.Locals, switchBlock.Instructions[0]);
			if (switchVariable == null)
				return modified;

			if (switchBlock.Targets == null)
				return modified;
			var targets = new List<Block>(switchBlock.Targets);

			modified |= DeobfuscateLdloc(targets, switchBlock.FallThrough, switchBlock, switchVariable);

			return modified;
		}

		bool DeobfuscateStLdloc(Block switchBlock) {
			bool modified = false;

			var switchVariable = Instr.GetLocalVar(blocks.Locals, switchBlock.Instructions[0]);
			if (switchVariable == null)
				return modified;

			if (switchBlock.Targets == null)
				return modified;
			var targets = new List<Block>(switchBlock.Targets);

			modified |= DeobfuscateStLdloc(targets, switchBlock.FallThrough, switchBlock);

			return modified;
		}

		// Switch deobfuscation when block uses stloc N, ldloc N to load switch constant
		//	blk1:
		//		ldc.i4 X
		//		br swblk
		//	swblk:
		//		stloc N
		//		ldloc N
		//		switch (......)
		bool DeobfuscateStLdloc(IList<Block> switchTargets, Block switchFallThrough, Block block) {
			bool modified = false;
			foreach (var source in new List<Block>(block.Sources)) {
				if (!isBranchBlock(source))
					continue;
				instructionEmulator.Initialize(blocks, allBlocks[0] == source);
				instructionEmulator.Emulate(source.Instructions);

				var target = GetSwitchTarget(switchTargets, switchFallThrough, instructionEmulator.Pop());
				if (target == null)
					continue;
				source.ReplaceLastNonBranchWithBranch(0, target);
				source.Add(new Instr(OpCodes.Pop.ToInstruction()));
				modified = true;
			}
			return modified;
		}

		// Switch deobfuscation when block uses ldloc N to load switch constant
		//	blk1:
		//		ldc.i4 X
		//		stloc N
		//		br swblk / bcc swblk
		//	swblk:
		//		ldloc N
		//		switch (......)
		bool DeobfuscateLdloc(IList<Block> switchTargets, Block switchFallThrough, Block block, Local switchVariable) {
			bool modified = false;
			foreach (var source in new List<Block>(block.Sources)) {
				if (isBranchBlock(source)) {
					instructionEmulator.Initialize(blocks, allBlocks[0] == source);
					instructionEmulator.Emulate(source.Instructions);

					var target = GetSwitchTarget(switchTargets, switchFallThrough, instructionEmulator.GetLocal(switchVariable));
					if (target == null)
						continue;
					source.ReplaceLastNonBranchWithBranch(0, target);
					modified = true;
				}
				else if (IsBccBlock(source)) {
					instructionEmulator.Initialize(blocks, allBlocks[0] == source);
					instructionEmulator.Emulate(source.Instructions);

					var target = GetSwitchTarget(switchTargets, switchFallThrough, instructionEmulator.GetLocal(switchVariable));
					if (target == null)
						continue;
					if (source.Targets[0] == block) {
						source.SetNewTarget(0, target);
						modified = true;
					}
					if (source.FallThrough == block) {
						source.SetNewFallThrough(target);
						modified = true;
					}
				}
			}
			return modified;
		}

		// Switch deobfuscation when block has switch contant on TOS:
		//	blk1:
		//		ldc.i4 X
		//		br swblk
		//	swblk:
		//		switch (......)
		bool DeobfuscateTOS(IList<Block> switchTargets, Block switchFallThrough, Block block) {
			bool modified = false;
			foreach (var source in new List<Block>(block.Sources)) {
				if (!isBranchBlock(source))
					continue;
				instructionEmulator.Initialize(blocks, allBlocks[0] == source);
				instructionEmulator.Emulate(source.Instructions);

				var target = GetSwitchTarget(switchTargets, switchFallThrough, instructionEmulator.Pop());
				if (target == null) {
					modified |= DeobfuscateTos_Ldloc(switchTargets, switchFallThrough, source);
				}
				else {
					source.ReplaceLastNonBranchWithBranch(0, target);
					source.Add(new Instr(OpCodes.Pop.ToInstruction()));
					modified = true;
				}
			}
			return modified;
		}

		//		ldloc N
		//		br swblk
		// or
		//		stloc N
		//		ldloc N
		//		br swblk
		bool DeobfuscateTos_Ldloc(IList<Block> switchTargets, Block switchFallThrough, Block block) {
			if (IsLdlocBranch(block, false)) {
				var switchVariable = Instr.GetLocalVar(blocks.Locals, block.Instructions[0]);
				if (switchVariable == null)
					return false;
				return DeobfuscateLdloc(switchTargets, switchFallThrough, block, switchVariable);
			}
			else if (IsStLdlocBranch(block, false))
				return DeobfuscateStLdloc(switchTargets, switchFallThrough, block);

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

		static bool IsBccBlock(Block block) {
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

		bool DeobfuscateType1(Block switchBlock) {
			Block target;
			if (!EmulateGetTarget(switchBlock, out target) || target != null)
				return false;

			bool modified = false;

			foreach (var source in new List<Block>(switchBlock.Sources)) {
				if (!source.CanAppend(switchBlock))
					continue;
				if (!WillHaveKnownTarget(switchBlock, source))
					continue;

				source.Append(switchBlock);
				modified = true;
			}

			return modified;
		}

		bool DeobfuscateType2(Block switchBlock) {
			bool modified = false;

			var bccSources = new List<Block>();
			foreach (var source in new List<Block>(switchBlock.Sources)) {
				if (source.LastInstr.IsConditionalBranch()) {
					bccSources.Add(source);
					continue;
				}
				if (!source.CanAppend(switchBlock))
					continue;
				if (!WillHaveKnownTarget(switchBlock, source))
					continue;

				source.Append(switchBlock);
				modified = true;
			}

			foreach (var bccSource in bccSources) {
				if (!WillHaveKnownTarget(switchBlock, bccSource))
					continue;
				var consts = GetBccLocalConstants(bccSource);
				if (consts.Count == 0)
					continue;
				var newFallThrough = CreateBlock(consts, bccSource.FallThrough);
				var newTarget = CreateBlock(consts, bccSource.Targets[0]);
				var oldFallThrough = bccSource.FallThrough;
				var oldTarget = bccSource.Targets[0];
				bccSource.SetNewFallThrough(newFallThrough);
				bccSource.SetNewTarget(0, newTarget);
				newFallThrough.SetNewFallThrough(oldFallThrough);
				newTarget.SetNewFallThrough(oldTarget);
				modified = true;
			}

			return modified;
		}

		static Block CreateBlock(Dictionary<Local, int> consts, Block fallThrough) {
			var block = new Block();
			foreach (var kv in consts) {
				block.Instructions.Add(new Instr(Instruction.CreateLdcI4(kv.Value)));
				block.Instructions.Add(new Instr(OpCodes.Stloc.ToInstruction(kv.Key)));
			}
			fallThrough.Parent.Add(block);
			return block;
		}

		Dictionary<Local, int> GetBccLocalConstants(Block block) {
			var dict = new Dictionary<Local, int>();
			var instrs = block.Instructions;
			for (int i = 0; i < instrs.Count; i++) {
				var instr = instrs[i];
				if (instr.IsStloc()) {
					var local = Instr.GetLocalVar(blocks.Locals, instr);
					if (local == null)
						continue;
					var ldci4 = i == 0 ? null : instrs[i - 1];
					if (ldci4 == null || !ldci4.IsLdcI4())
						dict.Remove(local);
					else
						dict[local] = ldci4.GetLdcI4Value();
				}
				else if (instr.IsLdloc()) {
					var local = Instr.GetLocalVar(blocks.Locals, instr);
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

		bool EmulateGetTarget(Block switchBlock, out Block target) {
			instructionEmulator.Initialize(blocks, allBlocks[0] == switchBlock);
			try {
				instructionEmulator.Emulate(switchBlock.Instructions, 0, switchBlock.Instructions.Count - 1);
			}
			catch (NullReferenceException) {
				// Here if eg. invalid metadata token in a call instruction (operand is null)
				target = null;
				return false;
			}
			target = GetTarget(switchBlock);
			return true;
		}

		bool WillHaveKnownTarget(Block switchBlock, Block source) {
			instructionEmulator.Initialize(blocks, allBlocks[0] == source);
			try {
				instructionEmulator.Emulate(source.Instructions);
				instructionEmulator.Emulate(switchBlock.Instructions, 0, switchBlock.Instructions.Count - 1);
			}
			catch (NullReferenceException) {
				// Here if eg. invalid metadata token in a call instruction (operand is null)
				return false;
			}
			return GetTarget(switchBlock) != null;
		}

		Block GetTarget(Block switchBlock) {
			var val1 = instructionEmulator.Pop();
			if (!val1.IsInt32())
				return null;
			return CflowUtils.GetSwitchTarget(switchBlock.Targets, switchBlock.FallThrough, (Int32Value)val1);
		}

		static Block GetSwitchTarget(IList<Block> targets, Block fallThrough, Value value) {
			if (!value.IsInt32())
				return null;
			return CflowUtils.GetSwitchTarget(targets, fallThrough, (Int32Value)value);
		}

		static bool FixSwitchBranch(Block switchBlock) {
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

			bool modified = false;

			foreach (var commonSource in new List<Block>(switchBlock.Sources)) {
				if (commonSource.Instructions.Count != 1)
					continue;
				if (!commonSource.FirstInstr.IsStloc())
					continue;
				foreach (var blk in new List<Block>(commonSource.Sources)) {
					if (blk.CanAppend(commonSource)) {
						blk.Append(commonSource);
						modified = true;
					}
				}
			}

			return modified;
		}
	}
}
