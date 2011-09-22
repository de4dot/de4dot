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

using System;
using System.Collections.Generic;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot {
	class SwitchControlFlowDeobfuscator {
		Blocks blocks;
		Dictionary<Block, bool> foundBlocks = new Dictionary<Block, bool>();

		class SwitchObfuscationInfo {
			public Block switchBlock;
			public VariableDefinition stateVar;
			Func<Instr, VariableDefinition> getLocalVar;
			Dictionary<Block, bool> switchTargetBlocks = new Dictionary<Block, bool>();

			public IEnumerable<Block> SwitchTargetBlocks {
				get { return switchTargetBlocks.Keys; }
			}

			internal SwitchObfuscationInfo(Func<Instr, VariableDefinition> getLocalVar) {
				this.getLocalVar = getLocalVar;
			}

			void findAllSwitchTargetBlocks() {
				addSwitchTargetBlock(switchBlock);
			}

			void addSwitchTargetBlock(Block block) {
				if (switchTargetBlocks.ContainsKey(block))
					return;
				switchTargetBlocks[block] = true;
				foreach (var source in block.Sources) {
					if (isNopBlock(source))
						addSwitchTargetBlock(source);
				}
			}

			bool isNopBlock(Block block) {
				foreach (var instr in block.Instructions) {
					if (instr.OpCode.Code != Code.Nop && instr.OpCode.Code != Code.Br && instr.OpCode.Code != Code.Br_S)
						return false;
				}

				return true;
			}

			public void fixSwitchBranches(ScopeBlock scopeBlock) {
				findAllSwitchTargetBlocks();
				foreach (var switchTargetBlock in new List<Block>(switchTargetBlocks.Keys)) {
					foreach (var block in new List<Block>(switchTargetBlock.Sources)) {
						int numInstrs;
						Block switchTarget;
						if (getSwitchIndex(block, out numInstrs, out switchTarget))
							block.replaceLastInstrsWithBranch(numInstrs, switchTarget);
					}
				}
			}

			bool getSwitchIndex(Block block, out int numInstrs, out Block switchTarget) {
				numInstrs = -1;
				switchTarget = null;

				if (block.Instructions.Count < 2)
					return false;

				int count = block.Instructions.Count;
				if (!block.Instructions[count - 2].isLdcI4())
					return false;
				if (!block.Instructions[count - 1].isStloc() || getLocalVar(block.Instructions[count - 1]) != stateVar)
					return false;
				if (!block.isFallThrough() || !switchTargetBlocks.ContainsKey(block.FallThrough))
					return false;

				int switchIndex = (int)block.Instructions[count - 2].getLdcI4Value();
				if (switchIndex < 0 || switchIndex >= switchBlock.Targets.Count)
					return false;

				numInstrs = 2;
				switchTarget = switchBlock.Targets[switchIndex];

				return true;
			}
		}

		public SwitchControlFlowDeobfuscator(Blocks blocks) {
			this.blocks = blocks;
		}

		public void deobfuscate(ScopeBlock scopeBlock) {
			while (true) {
				var switchObfuscationInfo = new SwitchObfuscationInfo((instr) => getLocalVar(instr));
				if (!findSwitchObfuscation(scopeBlock, switchObfuscationInfo))
					break;
				switchObfuscationInfo.fixSwitchBranches(scopeBlock);
				scopeBlock.removeDeadBlocks(new List<Block>(switchObfuscationInfo.SwitchTargetBlocks));
				scopeBlock.mergeBlocks();
			}
		}

		VariableDefinition getLocalVar(Instr instr) {
			return Instr.getLocalVar(blocks.Locals, instr);
		}

		bool findSwitchObfuscation(ScopeBlock scopeBlock, SwitchObfuscationInfo switchObfuscationInfo) {
			foreach (var bb in scopeBlock.getBaseBlocks()) {
				var block = bb as Block;
				if (block == null || foundBlocks.ContainsKey(block))
					continue;

				if (block.Instructions.Count != 2 || !block.Instructions[0].isLdloc() || block.Instructions[1].OpCode != OpCodes.Switch)
					continue;
				switchObfuscationInfo.switchBlock = block;
				switchObfuscationInfo.stateVar = getLocalVar(block.Instructions[0]);
				var typeName = switchObfuscationInfo.stateVar.VariableType.FullName;
				if (typeName != "System.Int32" && typeName != "System.UInt32")
					continue;

				foundBlocks[block] = true;
				return true;
			}
			return false;
		}
	}
}
