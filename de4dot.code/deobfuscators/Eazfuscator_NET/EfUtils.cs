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
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	static class EfUtils {
		public static bool getNextInt32(MethodDefinition method, ref int index, out int val) {
			for (; index < method.Body.Instructions.Count; index++) {
				var instr = method.Body.Instructions[index];
				if (instr.OpCode.Code != Code.Ldc_I4_S && instr.OpCode.Code != Code.Ldc_I4)
					continue;

				return getInt32(method, ref index, out val);
			}

			val = 0;
			return false;
		}

		public static bool getInt16(MethodDefinition method, ref int index, ref short s) {
			int val;
			if (!getInt32(method, ref index, out val))
				return false;
			s = (short)val;
			return true;
		}

		public static bool getInt32(MethodDefinition method, ref int index, out int val) {
			val = 0;
			var instrs = method.Body.Instructions;
			if (index >= instrs.Count)
				return false;
			var ldci4 = instrs[index];
			if (ldci4.OpCode.Code != Code.Ldc_I4_S && ldci4.OpCode.Code != Code.Ldc_I4)
				return false;

			var stack = new Stack<int>();
			stack.Push(DotNetUtils.getLdcI4Value(ldci4));

			index++;
			for (; index < instrs.Count; index++) {
				int l = stack.Count - 1;

				var instr = instrs[index];
				switch (instr.OpCode.Code) {
				case Code.Not:
					stack.Push(~stack.Pop());
					break;

				case Code.Neg:
					stack.Push(-stack.Pop());
					break;

				case Code.Ldc_I4:
				case Code.Ldc_I4_S:
				case Code.Ldc_I4_0:
				case Code.Ldc_I4_1:
				case Code.Ldc_I4_2:
				case Code.Ldc_I4_3:
				case Code.Ldc_I4_4:
				case Code.Ldc_I4_5:
				case Code.Ldc_I4_6:
				case Code.Ldc_I4_7:
				case Code.Ldc_I4_8:
				case Code.Ldc_I4_M1:
					stack.Push(DotNetUtils.getLdcI4Value(instr));
					break;

				case Code.Xor:
					if (stack.Count < 2)
						goto done;
					stack.Push(stack.Pop() ^ stack.Pop());
					break;

				default:
					goto done;
				}
			}
done:
			while (stack.Count > 1)
				stack.Pop();
			val = stack.Pop();
			return true;
		}
	}
}
