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

using System.Collections.Generic;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Confuser {
	class ConstantsInliner : IBlocksDeobfuscator {
		Blocks blocks;
		Int32ValueInliner int32ValueInliner;
		Int64ValueInliner int64ValueInliner;
		SingleValueInliner singleValueInliner;
		DoubleValueInliner doubleValueInliner;

		public bool ExecuteIfNotModified { get; set; }

		public ConstantsInliner(Int32ValueInliner int32ValueInliner, Int64ValueInliner int64ValueInliner, SingleValueInliner singleValueInliner, DoubleValueInliner doubleValueInliner) {
			this.int32ValueInliner = int32ValueInliner;
			this.int64ValueInliner = int64ValueInliner;
			this.singleValueInliner = singleValueInliner;
			this.doubleValueInliner = doubleValueInliner;
		}

		public void DeobfuscateBegin(Blocks blocks) => this.blocks = blocks;

		public bool Deobfuscate(List<Block> allBlocks) {
			bool modified = false;
			foreach (var block in allBlocks) {
				modified |= int32ValueInliner.Decrypt(blocks.Method, allBlocks) != 0;
				modified |= int64ValueInliner.Decrypt(blocks.Method, allBlocks) != 0;
				modified |= singleValueInliner.Decrypt(blocks.Method, allBlocks) != 0;
				modified |= doubleValueInliner.Decrypt(blocks.Method, allBlocks) != 0;
			}
			return modified;
		}
	}
}
