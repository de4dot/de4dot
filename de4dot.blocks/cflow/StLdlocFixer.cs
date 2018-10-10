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
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.blocks.cflow {
	// Replace stloc + ldloc with dup + stloc
	class StLdlocFixer : BlockDeobfuscator {
		IList<Local> locals;

		protected override void Initialize(List<Block> allBlocks) {
			base.Initialize(allBlocks);
			locals = blocks.Locals;
		}

		protected override bool Deobfuscate(Block block) {
			bool modified = false;
			var instructions = block.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instr = instructions[i];
				switch (instr.OpCode.Code) {
				// Xenocode generates stloc + ldloc (bool). Replace it with dup + stloc. It will eventually
				// become dup + pop and be removed.
				case Code.Stloc:
				case Code.Stloc_S:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
					if (i + 1 >= instructions.Count)
						break;
					if (!instructions[i + 1].IsLdloc())
						break;
					var local = Instr.GetLocalVar(locals, instr);
					if (local.Type.ElementType != ElementType.Boolean)
						continue;
					if (local != Instr.GetLocalVar(locals, instructions[i + 1]))
						break;
					instructions[i] = new Instr(OpCodes.Dup.ToInstruction());
					instructions[i + 1] = instr;
					modified = true;
					break;

				default:
					break;
				}
			}

			return modified;
		}
	}
}
