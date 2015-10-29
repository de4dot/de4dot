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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Goliath_NET {
	class StrongNameChecker {
		ModuleDefMD module;
		TypeDef strongNameType;
		MethodDef strongNameCheckMethod;

		public bool Detected {
			get { return strongNameType != null; }
		}

		public TypeDef Type {
			get { return strongNameType; }
		}

		public MethodDef CheckerMethod {
			get { return strongNameCheckMethod; }
		}

		public StrongNameChecker(ModuleDefMD module) {
			this.module = module;
		}

		public void Find() {
			foreach (var type in module.Types) {
				if (type.HasFields || type.HasEvents || type.HasProperties)
					continue;

				var checkMethod = GetAntiTamperingDetectionMethod(type);
				if (checkMethod == null)
					continue;

				if (DotNetUtils.GetMethod(type, "System.Byte[]", "(System.Reflection.Assembly)") == null)
					continue;
				if (DotNetUtils.GetMethod(type, "System.String", "(System.Collections.Generic.Stack`1<System.Int32>)") == null)
					continue;
				if (DotNetUtils.GetMethod(type, "System.Int32", "(System.Int32,System.Byte[])") == null)
					continue;

				strongNameType = type;
				strongNameCheckMethod = checkMethod;
				return;
			}
		}

		MethodDef GetAntiTamperingDetectionMethod(TypeDef type) {
			var requiredLocals = new string[] {
				"System.Reflection.Assembly",
				"System.Collections.Generic.Stack`1<System.Int32>",
			};
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Void", "(System.Type)"))
					continue;
				if (!new LocalTypes(method).All(requiredLocals))
					continue;
				if (!HasThrow(method))
					continue;

				return method;
			}
			return null;
		}

		static bool HasThrow(MethodDef method) {
			if (method == null || method.Body == null)
				return false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Throw)
					return true;
			}
			return false;
		}

		public bool Deobfuscate(Blocks blocks) {
			if (blocks.Method.Name != ".cctor" && blocks.Method.Name != ".ctor")
				return false;
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 2; i++) {
					var ldtoken = instrs[i];
					if (ldtoken.OpCode.Code != Code.Ldtoken)
						continue;

					var call1 = instrs[i + 1];
					if (call1.OpCode.Code != Code.Call && call1.OpCode.Code != Code.Callvirt)
						continue;
					if (!DotNetUtils.IsMethod(call1.Operand as IMethod, "System.Type", "(System.RuntimeTypeHandle)"))
						continue;

					var call2 = instrs[i + 2];
					if (call2.OpCode.Code != Code.Call && call2.OpCode.Code != Code.Callvirt)
						continue;
					if (!MethodEqualityComparer.CompareDeclaringTypes.Equals(call2.Operand as IMethod, strongNameCheckMethod))
						continue;

					block.Remove(i, 3);
					return true;
				}
			}
			return false;
		}
	}
}
