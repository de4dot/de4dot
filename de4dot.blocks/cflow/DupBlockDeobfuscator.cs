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
using dnlib.DotNet.Emit;

namespace de4dot.blocks.cflow {
	// If a block is just a dup followed by a bcc, try to append the block
	// to all its sources. Will fix some SA assemblies.
	class DupBlockCflowDeobfuscator : BlockDeobfuscator {
		protected override bool Deobfuscate(Block block) {
			if (block.Instructions.Count != 2)
				return false;
			if (block.Instructions[0].OpCode.Code != Code.Dup)
				return false;
			if (!block.LastInstr.IsConditionalBranch() && block.LastInstr.OpCode.Code != Code.Switch)
				return false;

			bool modified = false;
			foreach (var source in new List<Block>(block.Sources)) {
				if (source.GetOnlyTarget() != block)
					continue;
				if (!source.CanAppend(block))
					continue;

				source.Append(block);
				modified = true;
			}
			return modified;
		}
	}
}
