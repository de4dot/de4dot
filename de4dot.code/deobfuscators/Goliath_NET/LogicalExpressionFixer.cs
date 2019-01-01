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

using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Goliath_NET {
	class LogicalExpressionFixer {
		public void Deobfuscate(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 1; i++) {
					var first = instrs[i];
					var second = instrs[i + 1];
					if (first.OpCode.Code == Code.Not && second.OpCode.Code == Code.Neg) {
						// It's increment
						instrs[i] = new Instr(OpCodes.Ldc_I4_1.ToInstruction());
						instrs[i + 1] = new Instr(OpCodes.Add.ToInstruction());
					}
					else if (first.OpCode.Code == Code.Neg && second.OpCode.Code == Code.Not) {
						// It's decrement
						instrs[i] = new Instr(OpCodes.Ldc_I4_1.ToInstruction());
						instrs[i + 1] = new Instr(OpCodes.Sub.ToInstruction());
					}
				}
			}
		}
	}
}
