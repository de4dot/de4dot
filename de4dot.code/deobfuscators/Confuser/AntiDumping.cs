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

namespace de4dot.code.deobfuscators.Confuser {
	class AntiDumping {
		ModuleDefinition module;
		MethodDefinition initMethod;

		public MethodDefinition InitMethod {
			get { return initMethod; }
		}

		public TypeDefinition Type {
			get { return initMethod != null ? initMethod.DeclaringType : null; }
		}

		public bool Detected {
			get { return initMethod != null; }
		}

		public AntiDumping(ModuleDefinition module) {
			this.module = module;
		}

		public void find(ISimpleDeobfuscator simpleDeobfuscator) {
			if (checkMethod(simpleDeobfuscator, DotNetUtils.getModuleTypeCctor(module)))
				return;
		}

		bool checkMethod(ISimpleDeobfuscator simpleDeobfuscator, MethodDefinition method) {
			if (method == null || method.Body == null)
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				var calledMethod = instr.Operand as MethodDefinition;
				if (calledMethod == null)
					continue;
				if (!DotNetUtils.isMethod(calledMethod, "System.Void", "()"))
					continue;

				simpleDeobfuscator.deobfuscate(calledMethod, true);
				if (checkInitMethod_vXX(calledMethod) || checkInitMethod_v14_r58564(calledMethod)) {
					initMethod = calledMethod;
					return true;
				}
			}
			return false;
		}

		static bool checkInitMethod_v14_r58564(MethodDefinition method) {
			if (method == null || method.Body == null || !method.IsStatic)
				return false;
			if (!DotNetUtils.isMethod(method, "System.Void", "()"))
				return false;
			if (DotNetUtils.getPInvokeMethod(method.DeclaringType, "kernel32", "VirtualProtect") == null)
				return false;
			if (!DeobUtils.hasInteger(method, 224))
				return false;
			if (!DeobUtils.hasInteger(method, 240))
				return false;
			if (!DeobUtils.hasInteger(method, 267))
				return false;
			var type = method.DeclaringType;
			if (type.Methods.Count != 2)
				return false;
			if (type.Fields.Count != 0)
				return false;
			if (type.Properties.Count != 0)
				return false;

			return true;
		}

		static bool checkInitMethod_vXX(MethodDefinition method) {
			if (method == null || method.Body == null || !method.IsStatic)
				return false;
			if (!DotNetUtils.isMethod(method, "System.Void", "()"))
				return false;
			if (!DeobUtils.hasInteger(method, 0x3C))
				return false;
			if (!DeobUtils.hasInteger(method, 0x6c64746e))
				return false;
			if (!DeobUtils.hasInteger(method, 0x6c642e6c))
				return false;
			if (!DeobUtils.hasInteger(method, 0x6f43744e))
				return false;
			if (!DeobUtils.hasInteger(method, 0x6e69746e))
				return false;
			if (DotNetUtils.getPInvokeMethod(method.DeclaringType, "kernel32", "VirtualProtect") == null)
				return false;

			return true;
		}
	}
}
