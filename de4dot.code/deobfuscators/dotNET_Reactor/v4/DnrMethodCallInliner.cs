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
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class DnrMethodCallInliner : MethodCallInliner {
		public DnrMethodCallInliner()
			: base(false) {
		}

		protected override Instruction GetFirstInstruction(IList<Instruction> instrs, ref int index) {
			var instr = GetFirstInstruction(instrs, index);
			if (instr != null)
				index = instrs.IndexOf(instr);
			return DotNetUtils.GetInstruction(instrs, ref index);
		}

		Instruction GetFirstInstruction(IList<Instruction> instrs, int index) {
			try {
				var instr = instrs[index];
				if (!instr.IsBr())
					return null;
				instr = instr.Operand as Instruction;
				if (instr == null)
					return null;
				if (!instr.IsLdcI4() || instr.GetLdcI4Value() != 0)
					return null;
				instr = instrs[instrs.IndexOf(instr) + 1];
				if (!instr.IsBrtrue())
					return null;
				return instrs[instrs.IndexOf(instr) + 1];
			}
			catch {
				return null;
			}
		}
	}
}
