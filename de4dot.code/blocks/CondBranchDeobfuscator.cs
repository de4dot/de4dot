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
using de4dot.blocks;

namespace de4dot {
	abstract class CondBranchDeobfuscator {
		ScopeBlock scopeBlock;
		IEnumerable<Block> blocks;

		public CondBranchDeobfuscator(ScopeBlock scopeBlock, IEnumerable<Block> blocks) {
			this.scopeBlock = scopeBlock;
			this.blocks = blocks;
		}

		protected abstract bool isTaken(int value);

		public bool deobfuscate() {
			bool removed = false;

			int value = 0;
			var deadBlocks = new List<Block>();
			foreach (var block in blocks) {
				if (block.Instructions.Count > 1) {
					if (getLdcValue(block.Instructions, block.Instructions.Count - 2, ref value)) {
						removed = true;
						if (isTaken(value)) {
							deadBlocks.Add(block.FallThrough);
							block.replaceLastInstrsWithBranch(2, block.Targets[0]);
						}
						else {
							deadBlocks.Add(block.Targets[0]);
							block.replaceLastInstrsWithBranch(2, block.FallThrough);
						}
					}
				}
				else {
					foreach (var source in new List<Block>(block.Sources)) {
						int count = source.Instructions.Count;
						if (count > 0 && getLdcValue(source.Instructions, count - 1, ref value)) {
							removed = true;
							if (isTaken(value))
								source.replaceLastNonBranchWithBranch(1, block.Targets[0]);
							else
								source.replaceLastNonBranchWithBranch(1, block.FallThrough);
						}
					}
					deadBlocks.Add(block);
				}
			}
			scopeBlock.removeDeadBlocks(deadBlocks);

			return removed;
		}

		bool getLdcValue(IList<Instr> instrs, int i, ref int value) {
			var instr = instrs[i];
			if (instr.OpCode != OpCodes.Dup)
				return scopeBlock.getLdcValue(instr, out value);

			if (i == 0)
				return false;
			return scopeBlock.getLdcValue(instrs[i - 1], out value);
		}
	}

	class BrFalseDeobfuscator : CondBranchDeobfuscator {
		public BrFalseDeobfuscator(ScopeBlock scopeBlock, IEnumerable<Block> blocks)
			: base(scopeBlock, blocks) {
		}

		protected override bool isTaken(int value) {
			return value == 0;
		}
	}

	class BrTrueDeobfuscator : CondBranchDeobfuscator {
		public BrTrueDeobfuscator(ScopeBlock scopeBlock, IEnumerable<Block> blocks)
			: base(scopeBlock, blocks) {
		}

		protected override bool isTaken(int value) {
			return value != 0;
		}
	}
}
