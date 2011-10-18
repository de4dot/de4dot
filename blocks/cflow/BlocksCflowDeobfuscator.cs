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
	public class BlocksCflowDeobfuscator {
		BlockCflowDeobfuscator blockControlFlowDeobfuscator = new BlockCflowDeobfuscator();
		Blocks blocks;
		int numRemovedDeadBlocks;

		public int NumberOfRemovedDeadBlocks {
			get { return numRemovedDeadBlocks; }
		}

		public void init(Blocks blocks) {
			this.blocks = blocks;
			numRemovedDeadBlocks = 0;
		}

		public void deobfuscate() {
			var allBlocks = new List<Block>();
			var switchCflowDeobfuscator = new SwitchCflowDeobfuscator();
			bool changed;
			do {
				changed = false;
				changed |= removeDeadBlocks();
				changed |= mergeBlocks();

				allBlocks.Clear();
				allBlocks.AddRange(blocks.MethodBlocks.getAllBlocks());

				foreach (var block in allBlocks) {
					var lastInstr = block.LastInstr;
					if (!DotNetUtils.isConditionalBranch(lastInstr.OpCode.Code) && lastInstr.OpCode.Code != Code.Switch)
						continue;
					blockControlFlowDeobfuscator.init(block, blocks.Method.Parameters, blocks.Locals);
					changed |= blockControlFlowDeobfuscator.deobfuscate();
				}

				switchCflowDeobfuscator.init(blocks, allBlocks);
				changed |= switchCflowDeobfuscator.deobfuscate();
			} while (changed);
		}

		bool removeDeadBlocks() {
			int count = new DeadBlocksRemover(blocks.MethodBlocks).remove();
			numRemovedDeadBlocks += count;
			return count > 0;
		}

		bool mergeBlocks() {
			bool changed = false;
			foreach (var scopeBlock in getAllScopeBlocks(blocks.MethodBlocks))
				changed |= scopeBlock.mergeBlocks() > 0;
			return changed;
		}

		IEnumerable<ScopeBlock> getAllScopeBlocks(ScopeBlock scopeBlock) {
			var list = new List<ScopeBlock>();
			list.Add(scopeBlock);
			list.AddRange(scopeBlock.getAllScopeBlocks());
			return list;
		}
	}
}
