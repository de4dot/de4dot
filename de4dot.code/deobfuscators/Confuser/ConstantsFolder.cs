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

using System.Collections.Generic;
using Mono.Cecil.Cil;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Confuser {
	class ConstantsFolder : BlockDeobfuscator {
		protected override bool deobfuscate(Block block) {
			bool modified = false;

			var instrs = block.Instructions;
			var constantsReader = createConstantsReader(instrs);
			for (int i = 0; i < instrs.Count; i++) {
				int index = 0;
				Instruction newInstr = null;
				var instr = instrs[i];
				if (constantsReader.isLoadConstant32(instr.Instruction)) {
					index = i;
					int val;
					if (!constantsReader.getInt32(ref index, out val))
						continue;
					newInstr = DotNetUtils.createLdci4(val);
				}
				else if (constantsReader.isLoadConstant64(instr.Instruction)) {
					index = i;
					long val;
					if (!constantsReader.getInt64(ref index, out val))
						continue;
					newInstr = Instruction.Create(OpCodes.Ldc_I8, val);
				}

				if (newInstr == null || index - i <= 1)
					continue;

				block.insert(index++, Instruction.Create(OpCodes.Pop));
				block.insert(index++, newInstr);
				i = index - 1;
				constantsReader = createConstantsReader(instrs);
				modified = true;
			}

			return modified;
		}

		static ConstantsReader createConstantsReader(IList<Instr> instrs) {
			return new ConstantsReader(instrs, false);
		}
	}
}
