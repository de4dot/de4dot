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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	static class DsUtils {
		public static IList<object> GetArgValues(IList<Instruction> instrs, int index) {
			return GetArgValues(DotNetUtils.GetArgPushes(instrs, index));
		}

		public static IList<object> GetArgValues(IList<Instruction> argInstrs) {
			if (argInstrs == null)
				return null;
			var args = new List<object>(argInstrs.Count);
			foreach (var argInstr in argInstrs) {
				object arg;
				GetArgValue(argInstr, out arg);
				args.Add(arg);
			}
			return args;
		}

		public static bool GetArgValue(MethodDef method, int index, out object arg) {
			return GetArgValue(method.Body.Instructions[index], out arg);
		}

		public static bool GetArgValue(Instruction instr, out object arg) {
			switch (instr.OpCode.Code) {
			case Code.Ldc_I4_S: arg = (int)(sbyte)instr.Operand; return true;
			case Code.Ldc_I4_M1: arg = -1; return true;
			case Code.Ldc_I4_0: arg = 0; return true;
			case Code.Ldc_I4_1: arg = 1; return true;
			case Code.Ldc_I4_2: arg = 2; return true;
			case Code.Ldc_I4_3: arg = 3; return true;
			case Code.Ldc_I4_4: arg = 4; return true;
			case Code.Ldc_I4_5: arg = 5; return true;
			case Code.Ldc_I4_6: arg = 6; return true;
			case Code.Ldc_I4_7: arg = 7; return true;
			case Code.Ldc_I4_8: arg = 8; return true;
			case Code.Ldnull: arg = null; return true;

			case Code.Ldstr:
			case Code.Ldc_I4:
			case Code.Ldc_I8:
			case Code.Ldc_R4:
			case Code.Ldc_R8:
				arg = instr.Operand;
				return true;

			case Code.Ldarg:
			case Code.Ldarg_S:
			case Code.Ldarg_0:
			case Code.Ldarg_1:
			case Code.Ldarg_2:
			case Code.Ldarg_3:
			case Code.Ldloc:
			case Code.Ldloc_S:
			case Code.Ldloc_0:
			case Code.Ldloc_1:
			case Code.Ldloc_2:
			case Code.Ldloc_3:
				arg = null;
				return true;

			default:
				arg = null;
				return false;
			}
		}
	}
}
