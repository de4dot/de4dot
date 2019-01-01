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

namespace de4dot.blocks.cflow {
	public abstract class BlockDeobfuscator : IBlocksDeobfuscator {
		protected List<Block> allBlocks;
		protected Blocks blocks;

		public bool ExecuteIfNotModified { get; set; }

		public virtual void DeobfuscateBegin(Blocks blocks) => this.blocks = blocks;

		public bool Deobfuscate(List<Block> allBlocks) {
			Initialize(allBlocks);

			bool modified = false;
			foreach (var block in allBlocks) {
				try {
					modified |= Deobfuscate(block);
				}
				catch (NullReferenceException) {
					// Here if eg. invalid metadata token in a call instruction (operand is null)
				}
			}
			return modified;
		}

		protected virtual void Initialize(List<Block> allBlocks) => this.allBlocks = allBlocks;
		protected abstract bool Deobfuscate(Block block);
	}
}
