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
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Spices_Net {
	class SpicesMethodCallInliner : MethodCallInliner {
		ModuleDefMD module;
		TypeDefDict<bool> methodsTypes = new TypeDefDict<bool>();
		MethodDefAndDeclaringTypeDict<MethodDef> classMethods = new MethodDefAndDeclaringTypeDict<MethodDef>();

		public SpicesMethodCallInliner(ModuleDefMD module)
			: base(false) {
			this.module = module;
		}

		protected override bool IsCompatibleType(int paramIndex, IType origType, IType newType) {
			if (new SigComparer(SigComparerOptions.IgnoreModifiers).Equals(origType, newType))
				return true;
			if (paramIndex == -1) {
				if (IsValueType(newType) || IsValueType(origType))
					return false;
			}
			return newType.FullName == "System.Object";
		}

		public bool CheckCanInline(MethodDef method) {
			return methodsTypes.Find(method.DeclaringType);
		}

		protected override bool CanInline(MethodDef method) {
			return CheckCanInline(method);
		}

		public void Initialize(ISimpleDeobfuscator simpleDeobfuscator) {
			InitializeMethodsTypes();
			RestoreMethodBodies(simpleDeobfuscator);
		}

		void RestoreMethodBodies(ISimpleDeobfuscator simpleDeobfuscator) {
			var methodToOrigMethods = new MethodDefAndDeclaringTypeDict<List<MethodDef>>();
			foreach (var t in module.Types) {
				var types = new List<TypeDef>(AllTypesHelper.Types(new List<TypeDef> { t }));
				foreach (var type in types) {
					if (methodsTypes.Find(type))
						continue;
					foreach (var method in type.Methods) {
						if (method.Name == ".ctor" || method.Name == ".cctor")
							continue;

						MethodDef calledMethod;
						if (!CheckRestoreBody(method, out calledMethod))
							continue;
						if (!CheckSameMethods(method, calledMethod))
							continue;
						if (!methodsTypes.Find(calledMethod.DeclaringType))
							continue;
						if (types.IndexOf(calledMethod.DeclaringType) < 0)
							continue;

						var list = methodToOrigMethods.Find(calledMethod);
						if (list == null)
							methodToOrigMethods.Add(calledMethod, list = new List<MethodDef>());
						list.Add(method);
					}
				}
			}

			foreach (var calledMethod in methodToOrigMethods.GetKeys()) {
				var list = methodToOrigMethods.Find(calledMethod);
				var method = list[0];

				Logger.v("Restored method body {0:X8} from method {1:X8}",
							method.MDToken.ToInt32(),
							calledMethod.MDToken.ToInt32());
				DotNetUtils.CopyBodyFromTo(calledMethod, method);
				classMethods.Add(calledMethod, method);
				simpleDeobfuscator.MethodModified(method);
			}
		}

		bool CheckRestoreBody(MethodDef method, out MethodDef calledMethod) {
			calledMethod = null;
			if (method.Body == null)
				return false;
			if (method.Body.Variables.Count > 0)
				return false;
			if (method.Body.ExceptionHandlers.Count > 0)
				return false;

			if (!CheckRestoreBody2(method, out calledMethod))
				return false;
			if (calledMethod == method)
				return false;
			if (!calledMethod.IsStatic)
				return false;
			if (calledMethod.GenericParameters.Count > 0)
				return false;
			if (calledMethod.Body == null || calledMethod.Body.Instructions.Count == 0)
				return false;

			return true;
		}

		bool CheckRestoreBody2(MethodDef instanceMethod, out MethodDef calledMethod) {
			calledMethod = null;

			var instrs = instanceMethod.Body.Instructions;
			int index;
			for (index = 0; index < instrs.Count; index++) {
				if (instrs[index].GetParameterIndex() != index)
					break;
			}
			var call = instrs[index++];
			if (call.OpCode.Code != Code.Call)
				return false;

			calledMethod = call.Operand as MethodDef;
			if (calledMethod == null)
				return false;

			if (instrs[index++].OpCode.Code != Code.Ret)
				return false;

			return true;
		}

		void InitializeMethodsTypes() {
			foreach (var type in module.GetTypes()) {
				if (CheckMethodsType(type))
					methodsTypes.Add(type, true);
			}
		}

		static bool CheckMethodsType(TypeDef type) {
			if (!type.IsNested)
				return false;
			if ((type.Attributes & ~TypeAttributes.BeforeFieldInit) != TypeAttributes.NestedAssembly)
				return false;
			if (type.HasProperties || type.HasEvents || type.HasFields || type.HasNestedTypes)
				return false;
			if (type.GenericParameters.Count > 0)
				return false;
			if (type.IsValueType || type.IsInterface)
				return false;
			if (type.BaseType == null || type.BaseType.FullName != "System.Object")
				return false;
			if (type.Interfaces.Count > 0)
				return false;
			if (!CheckMethods(type))
				return false;

			return true;
		}

		static bool CheckMethods(TypeDef type) {
			bool foundCtor = false;
			int numMethods = 0;

			foreach (var method in type.Methods) {
				if (method.Name == ".cctor")
					return false;
				if (method.Name == ".ctor") {
					if (method.MethodSig.GetParamCount() != 0)
						return false;
					foundCtor = true;
					continue;
				}
				if (method.Attributes != (MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig))
					return false;
				if (method.ImplMap != null)
					return false;
				if (method.GenericParameters.Count > 0)
					return false;

				numMethods++;
			}

			return numMethods > 0 && foundCtor;
		}

		public List<MethodDef> GetInlinedMethods() {
			var list = new List<MethodDef>();

			foreach (var type in methodsTypes.GetKeys())
				list.AddRange(type.Methods);

			return list;
		}

		public TypeDefDict<bool> GetInlinedTypes(IEnumerable<MethodDef> unusedMethods) {
			var unused = new MethodDefAndDeclaringTypeDict<bool>();
			foreach (var method in unusedMethods)
				unused.Add(method, true);

			var types = new TypeDefDict<bool>();
			foreach (var type in methodsTypes.GetKeys()) {
				if (CheckAllMethodsUnused(unused, type))
					types.Add(type, true);
			}
			return types;
		}

		static bool CheckAllMethodsUnused(MethodDefAndDeclaringTypeDict<bool> unused, TypeDef type) {
			foreach (var method in type.Methods) {
				if (!unused.Find(method))
					return false;
			}
			return true;
		}

		public void Deobfuscate(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var call = instrs[i];
					if (call.OpCode.Code != Code.Call)
						continue;
					var realInstanceMethod = classMethods.Find(call.Operand as IMethod);
					if (realInstanceMethod == null)
						continue;
					call.Operand = realInstanceMethod;
				}
			}
		}
	}
}
