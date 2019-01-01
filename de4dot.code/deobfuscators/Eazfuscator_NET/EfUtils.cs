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

using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	static class EfUtils {
		public static int FindOpCodeIndex(MethodDef method, int index, Code code) {
			for (; index < method.Body.Instructions.Count; index++) {
				var instr = method.Body.Instructions[index];
				if (instr.OpCode.Code != code)
					continue;

				return index;
			}
			return -1;
		}

		public static int FindOpCodeIndex(MethodDef method, int index, Code code, string operandString) {
			while (index < method.Body.Instructions.Count) {
				index = FindOpCodeIndex(method, index, code);
				if (index < 0)
					break;
				var instr = method.Body.Instructions[index];
				if (instr.Operand.ToString() == operandString)
					return index;

				index++;
			}
			return -1;
		}

		public static Instruction GetNextStore(MethodDef method, ref int index) {
			for (; index < method.Body.Instructions.Count; index++) {
				var instr = method.Body.Instructions[index];

				switch (instr.OpCode.Code) {
				case Code.Starg:
				case Code.Starg_S:
				case Code.Stelem:
				case Code.Stelem_I:
				case Code.Stelem_I1:
				case Code.Stelem_I2:
				case Code.Stelem_I4:
				case Code.Stelem_I8:
				case Code.Stelem_R4:
				case Code.Stelem_R8:
				case Code.Stelem_Ref:
				case Code.Stfld:
				case Code.Stind_I:
				case Code.Stind_I1:
				case Code.Stind_I2:
				case Code.Stind_I4:
				case Code.Stind_I8:
				case Code.Stind_R4:
				case Code.Stind_R8:
				case Code.Stind_Ref:
				case Code.Stloc:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
				case Code.Stloc_S:
				case Code.Stobj:
				case Code.Stsfld:
					return instr;
				}

				if (instr.OpCode.FlowControl != FlowControl.Next)
					break;
			}

			return null;
		}
	}
}
