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
		BlockCflowDeobfuscator blockCflowDeobfuscator = new BlockCflowDeobfuscator();
		Blocks blocks;
		List<Block> allBlocks = new List<Block>();
		SwitchCflowDeobfuscator switchCflowDeobfuscator = new SwitchCflowDeobfuscator();
		DeadCodeRemover deadCodeRemover = new DeadCodeRemover();
		DeadStoreRemover deadStoreRemover = new DeadStoreRemover();
		StLdlocFixer stLdlocFixer = new StLdlocFixer();
		int numRemovedDeadBlocks;

		public int NumberOfRemovedDeadBlocks {
			get { return numRemovedDeadBlocks; }
		}

		public void init(Blocks blocks) {
			this.blocks = blocks;
			numRemovedDeadBlocks = 0;
		}

		public void deobfuscate() {
			bool changed;
			do {
				changed = false;
				removeDeadBlocks();
				mergeBlocks();

				blocks.MethodBlocks.getAllBlocks(allBlocks);

				foreach (var block in allBlocks) {
					var lastInstr = block.LastInstr;
					if (!DotNetUtils.isConditionalBranch(lastInstr.OpCode.Code) && lastInstr.OpCode.Code != Code.Switch)
						continue;
					blockCflowDeobfuscator.init(blocks, block);
					changed |= blockCflowDeobfuscator.deobfuscate();
				}

				switchCflowDeobfuscator.init(blocks, allBlocks);
				changed |= switchCflowDeobfuscator.deobfuscate();

				deadStoreRemover.init(blocks, allBlocks);
				changed |= deadStoreRemover.remove();

				deadCodeRemover.init(allBlocks);
				changed |= deadCodeRemover.remove();

				if (!changed) {
					stLdlocFixer.init(allBlocks, blocks.Locals);
					changed |= stLdlocFixer.fix();
				}
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
