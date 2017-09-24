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

namespace de4dot.code.deobfuscators.Dotfuscator {
	class CflowDecrypter {
		ModuleDefMD module;

		public CflowDecrypter(ModuleDefMD module) {
			this.module = module;
		}
		
		public void CflowClean() {
			foreach (TypeDef type in this.module.GetTypes()) {
				if (!type.HasMethods)
					continue;
				foreach(var method in type.Methods ) {
					CleanMethod(method);
				}
			}
		}

		public void CleanMethod(MethodDef method) {
			if (!method.HasBody)
				return;
			if (!method.Body.HasInstructions)
				return;
			if (method.Body.Instructions.Count < 4)
				return;
			if (method.Body.Variables.Count == 0)
				return;
			var ins = method.Body.Instructions;
			for (int i = 3; i < ins.Count; i++) {
				if (ins[i].OpCode.Code != Code.Ldind_I4)
					continue;
				if (!ins[i - 1].IsLdloc() && ins[i - 1].OpCode.Code != Code.Ldloca && ins[i - 1].OpCode.Code != Code.Ldloca_S)
					continue;
				if (!ins[i - 2].IsStloc())
					continue;
				if (!ins[i - 3].IsLdcI4())
					continue;
				if (ins[i - 1].Operand != ins[i - 2].Operand)
					continue;
				method.Body.Instructions[i].OpCode = OpCodes.Nop;
				method.Body.Instructions[i].Operand = null;
				method.Body.Instructions[i - 1].OpCode = OpCodes.Nop;
				method.Body.Instructions[i - 1].Operand = null;
				method.Body.Instructions[i - 2].OpCode = OpCodes.Nop;
				method.Body.Instructions[i - 2].Operand = null;
			}
		}
	}
}
