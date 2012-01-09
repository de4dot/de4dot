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

using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Goliath_NET {
	class StrongNameChecker {
		ModuleDefinition module;
		TypeDefinition strongNameType;
		MethodDefinition strongNameCheckMethod;

		public bool Detected {
			get { return strongNameType != null;}
		}

		public TypeDefinition Type {
			get { return strongNameType; }
		}

		public MethodDefinition CheckerMethod {
			get { return strongNameCheckMethod; }
		}

		public StrongNameChecker(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				if (type.HasFields || type.HasEvents || type.HasProperties)
					continue;

				var checkMethod = getAntiTamperingDetectionMethod(type);
				if (checkMethod == null)
					continue;

				if (DotNetUtils.getMethod(type, "System.Byte[]", "(System.Reflection.Assembly)") == null)
					continue;
				if (DotNetUtils.getMethod(type, "System.String", "(System.Collections.Generic.Stack`1<System.Int32>)") == null)
					continue;
				if (DotNetUtils.getMethod(type, "System.Int32", "(System.Int32,System.Byte[])") == null)
					continue;

				strongNameType = type;
				strongNameCheckMethod = checkMethod;
				return;
			}
		}

		MethodDefinition getAntiTamperingDetectionMethod(TypeDefinition type) {
			var requiredLocals = new string[] {
				"System.Reflection.Assembly",
				"System.Collections.Generic.Stack`1<System.Int32>",
			};
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "(System.Type)"))
					continue;
				if (!new LocalTypes(method).all(requiredLocals))
					continue;
				if (!hasThrow(method))
					continue;

				return method;
			}
			return null;
		}

		static bool hasThrow(MethodDefinition method) {
			if (method == null || method.Body == null)
				return false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Throw)
					return true;
			}
			return false;
		}

		public bool deobfuscate(Blocks blocks) {
			if (blocks.Method.Name != ".cctor" && blocks.Method.Name != ".ctor")
				return false;
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count - 2; i++) {
					var ldtoken = instrs[i];
					if (ldtoken.OpCode.Code != Code.Ldtoken)
						continue;

					var call1 = instrs[i + 1];
					if (call1.OpCode.Code != Code.Call && call1.OpCode.Code != Code.Callvirt)
						continue;
					if (!DotNetUtils.isMethod(call1.Operand as MethodReference, "System.Type", "(System.RuntimeTypeHandle)"))
						continue;

					var call2 = instrs[i + 2];
					if (call2.OpCode.Code != Code.Call && call2.OpCode.Code != Code.Callvirt)
						continue;
					if (!MemberReferenceHelper.compareMethodReferenceAndDeclaringType(call2.Operand as MethodReference, strongNameCheckMethod))
						continue;

					block.remove(i, 3);
					return true;
				}
			}
			return false;
		}
	}
}
