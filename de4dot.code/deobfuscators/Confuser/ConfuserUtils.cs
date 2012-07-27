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

namespace de4dot.code.deobfuscators.Confuser {
	static class ConfuserUtils {
		public static int findCallMethod(IList<Instruction> instrs, int index, Code callCode, string methodFullName) {
			for (int i = index; i < instrs.Count; i++) {
				if (!isCallMethod(instrs[i], callCode, methodFullName))
					continue;

				return i;
			}
			return -1;
		}

		public static bool isCallMethod(Instruction instr, Code callCode, string methodFullName) {
			if (instr.OpCode.Code != callCode)
				return false;
			var calledMethod = instr.Operand as MethodReference;
			return calledMethod != null && calledMethod.FullName == methodFullName;
		}
	}
}
