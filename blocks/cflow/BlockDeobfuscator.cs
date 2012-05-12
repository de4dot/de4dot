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

using System;
using System.Collections.Generic;

namespace de4dot.blocks.cflow {
	public abstract class BlockDeobfuscator : IBlocksDeobfuscator {
		protected List<Block> allBlocks;
		protected Blocks blocks;

		public bool ExecuteOnNoChange { get; set; }

		public virtual void deobfuscateBegin(Blocks blocks) {
			this.blocks = blocks;
		}

		public bool deobfuscate(List<Block> allBlocks) {
			init(allBlocks);

			bool changed = false;
			foreach (var block in allBlocks) {
				try {
					changed |= deobfuscate(block);
				}
				catch (NullReferenceException) {
					// Here if eg. invalid metadata token in a call instruction (operand is null)
				}
			}
			return changed;
		}

		protected virtual void init(List<Block> allBlocks) {
			this.allBlocks = allBlocks;
		}

		protected abstract bool deobfuscate(Block block);
	}
}
