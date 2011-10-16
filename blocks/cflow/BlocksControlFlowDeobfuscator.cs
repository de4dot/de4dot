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

namespace de4dot.blocks.cflow {
	public class BlocksControlFlowDeobfuscator {
		BlockControlFlowDeobfuscator blockControlFlowDeobfuscator = new BlockControlFlowDeobfuscator();
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
			bool changed;
			do {
				changed = false;
				removeDeadBlocks();
				mergeBlocks();
				foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
					//TODO: Only do this if it's a bcc block. switch blocks should use other code.
					blockControlFlowDeobfuscator.init(block, blocks.Method.Parameters, blocks.Locals);
					changed |= blockControlFlowDeobfuscator.deobfuscate();
				}
			} while (changed);
		}

		void removeDeadBlocks() {
			numRemovedDeadBlocks += new DeadBlocksRemover(blocks.MethodBlocks).remove();
		}

		void mergeBlocks() {
			foreach (var scopeBlock in getAllScopeBlocks(blocks.MethodBlocks))
				scopeBlock.mergeBlocks();
		}

		IEnumerable<ScopeBlock> getAllScopeBlocks(ScopeBlock scopeBlock) {
			var list = new List<ScopeBlock>();
			list.Add(scopeBlock);
			list.AddRange(scopeBlock.getAllScopeBlocks());
			return list;
		}
	}
}
