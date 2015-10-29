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

namespace de4dot.code.deobfuscators.CodeVeil {
	class TamperDetection {
		ModuleDefMD module;
		MainType mainType;
		TypeDef tamperDetectionType;
		List<MethodDef> tamperDetectionMethods = new List<MethodDef>();

		public TypeDef Type {
			get { return tamperDetectionType; }
		}

		public List<MethodDef> Methods {
			get { return tamperDetectionMethods; }
		}

		public TamperDetection(ModuleDefMD module, MainType mainType) {
			this.module = module;
			this.mainType = mainType;
		}

		public void Initialize() {
			if (!mainType.Detected)
				return;

			if (mainType.TamperCheckMethod == null)
				return;

			FindTamperDetectionTypes();
		}

		void FindTamperDetectionTypes() {
			foreach (var type in module.Types) {
				if (!type.HasNestedTypes)
					continue;
				if ((type.Attributes & ~TypeAttributes.Sealed) != 0)
					continue;

				if (!CheckTamperDetectionClasses(type.NestedTypes))
					continue;

				tamperDetectionType = type;
				FindTamperDetectionMethods();
				return;
			}
		}

		void FindTamperDetectionMethods() {
			foreach (var type in tamperDetectionType.NestedTypes) {
				foreach (var method in type.Methods) {
					if (!method.IsStatic || method.Body == null)
						continue;
					if (method.Name == ".cctor")
						continue;
					if (DotNetUtils.IsMethod(method, "System.Void", "()"))
						tamperDetectionMethods.Add(method);
				}
			}
		}

		bool CheckTamperDetectionClasses(IEnumerable<TypeDef> types) {
			foreach (var type in types) {
				if (!IsTamperDetectionClass(type))
					return false;
			}
			return true;
		}

		bool IsTamperDetectionClass(TypeDef type) {
			if (type.BaseType == null || type.BaseType.FullName != "System.Object")
				return false;
			if ((type.Attributes & ~TypeAttributes.Sealed) != TypeAttributes.NestedAssembly)
				return false;

			MethodDef cctor = null, initMethod = null;
			foreach (var method in type.Methods) {
				if (InvalidMethodsFinder.IsInvalidMethod(method))
					continue;
				if (!method.IsStatic || method.Body == null)
					return false;
				if (method.Name == ".cctor")
					cctor = method;
				else if (DotNetUtils.IsMethod(method, "System.Void", "()"))
					initMethod = method;
			}
			if (cctor == null || initMethod == null)
				return false;

			if (!CallsMainTypeTamperCheckMethod(cctor))
				return false;

			return true;
		}

		bool CallsMainTypeTamperCheckMethod(MethodDef method) {
			foreach (var calledMethod in DotNetUtils.GetCalledMethods(module, method)) {
				if (calledMethod == mainType.TamperCheckMethod)
					return true;
			}

			var instructions = method.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instrs = DotNetUtils.GetInstructions(instructions, i, OpCodes.Ldtoken, OpCodes.Call, OpCodes.Call, OpCodes.Ldc_I8, OpCodes.Call);
				if (instrs == null)
					continue;

				if (!CheckInvokeCall(instrs[1], "System.Type", "(System.RuntimeTypeHandle)"))
					continue;
				if (!CheckInvokeCall(instrs[2], "System.Reflection.Assembly", "(System.Object)"))
					continue;
				if (!CheckInvokeCall(instrs[4], "System.Void", "(System.Reflection.Assembly,System.UInt64)"))
					continue;

				return true;
			}

			return false;
		}

		static bool CheckInvokeCall(Instruction instr, string returnType, string parameters) {
			var method = instr.Operand as MethodDef;
			if (method == null)
				return false;
			if (method.Name != "Invoke")
				return false;
			return DotNetUtils.IsMethod(method, returnType, parameters);
		}
	}
}
