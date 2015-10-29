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

namespace de4dot.code.deobfuscators.SmartAssembly {
	abstract class ResolverInfoBase {
		protected ModuleDefMD module;
		ISimpleDeobfuscator simpleDeobfuscator;
		IDeobfuscator deob;
		TypeDef resolverType;
		MethodDef callResolverMethod;

		public TypeDef Type {
			get { return resolverType; }
		}

		public TypeDef CallResolverType {
			get {
				if (callResolverMethod == null)
					return null;
				if (!HasOnlyThisMethod(callResolverMethod.DeclaringType, callResolverMethod))
					return null;
				return callResolverMethod.DeclaringType;
			}
		}

		public MethodDef CallResolverMethod {
			get { return callResolverMethod; }
		}

		public ResolverInfoBase(ModuleDefMD module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
			this.deob = deob;
		}

		public bool FindTypes() {
			if (resolverType != null)
				return true;

			if (FindTypes(DotNetUtils.GetModuleTypeCctor(module)))
				return true;
			if (FindTypes(module.EntryPoint))
				return true;

			return false;
		}

		bool FindTypes(MethodDef initMethod) {
			if (initMethod == null)
				return false;
			foreach (var method in DotNetUtils.GetCalledMethods(module, initMethod)) {
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.IsMethod(method, "System.Void", "()"))
					continue;
				if (CheckAttachAppMethod(method))
					return true;
			}

			return false;
		}

		bool CheckAttachAppMethod(MethodDef attachAppMethod) {
			callResolverMethod = null;
			if (!attachAppMethod.HasBody)
				return false;

			foreach (var method in DotNetUtils.GetCalledMethods(module, attachAppMethod)) {
				if (attachAppMethod == method)
					continue;
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.IsMethod(method, "System.Void", "()"))
					continue;
				if (!CheckResolverInitMethod(method))
					continue;

				callResolverMethod = attachAppMethod;
				return true;
			}

			if (HasLdftn(attachAppMethod)) {
				simpleDeobfuscator.Deobfuscate(attachAppMethod);
				foreach (var resolverHandler in GetResolverHandlers(attachAppMethod)) {
					if (!resolverHandler.HasBody)
						continue;
					var resolverTypeTmp = GetResolverType(resolverHandler);
					if (resolverTypeTmp == null)
						continue;
					Deobfuscate(resolverHandler);
					if (CheckHandlerMethod(resolverHandler)) {
						callResolverMethod = attachAppMethod;
						resolverType = resolverTypeTmp;
						return true;
					}
				}
			}

			return false;
		}

		static bool HasLdftn(MethodDef method) {
			if (method == null || method.Body == null)
				return false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Ldftn)
					return true;
			}
			return false;
		}

		bool CheckResolverInitMethod(MethodDef initMethod) {
			resolverType = null;
			if (!initMethod.HasBody)
				return false;

			Deobfuscate(initMethod);
			foreach (var handlerDef in GetResolverHandlers(initMethod)) {
				Deobfuscate(handlerDef);

				var resolverTypeTmp = GetResolverType(handlerDef);
				if (resolverTypeTmp == null)
					continue;

				if (CheckHandlerMethod(handlerDef)) {
					resolverType = resolverTypeTmp;
					return true;
				}
			}

			return false;
		}

		void Deobfuscate(MethodDef method) {
			simpleDeobfuscator.Deobfuscate(method);
			simpleDeobfuscator.DecryptStrings(method, deob);
		}

		TypeDef GetResolverType(MethodDef resolveHandler) {
			if (resolveHandler.Body == null)
				return null;
			foreach (var instr in resolveHandler.Body.Instructions) {
				if (instr.OpCode.Code != Code.Ldsfld && instr.OpCode.Code != Code.Stsfld)
					continue;
				var field = DotNetUtils.GetField(module, instr.Operand as IField);
				if (field == null)
					continue;
				if (!CheckResolverType(field.DeclaringType))
					continue;

				return field.DeclaringType;
			}

			if (CheckResolverType(resolveHandler.DeclaringType))
				return resolveHandler.DeclaringType;

			return null;
		}

		protected abstract bool CheckResolverType(TypeDef type);
		protected abstract bool CheckHandlerMethod(MethodDef handler);

		IEnumerable<MethodDef> GetResolverHandlers(MethodDef method) {
			int numHandlers = 0;
			var instructions = method.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instrs = DotNetUtils.GetInstructions(instructions, i, OpCodes.Call, OpCodes.Ldnull, OpCodes.Ldftn, OpCodes.Newobj, OpCodes.Callvirt);
				if (instrs == null)
					continue;

				var call = instrs[0];
				if (!DotNetUtils.IsMethod(call.Operand as IMethod, "System.AppDomain", "()"))
					continue;

				var ldftn = instrs[2];
				var handlerDef = DotNetUtils.GetMethod(module, ldftn.Operand as IMethod);
				if (handlerDef == null)
					continue;

				var newobj = instrs[3];
				if (!DotNetUtils.IsMethod(newobj.Operand as IMethod, "System.Void", "(System.Object,System.IntPtr)"))
					continue;

				var callvirt = instrs[4];
				if (!DotNetUtils.IsMethod(callvirt.Operand as IMethod, "System.Void", "(System.ResolveEventHandler)"))
					continue;

				numHandlers++;
				yield return handlerDef;
			}

			// If no handlers found, it's possible that the method itself is the handler.
			if (numHandlers == 0)
				yield return method;
		}

		static bool HasOnlyThisMethod(TypeDef type, MethodDef method) {
			if (type == null || method == null)
				return false;
			foreach (var m in type.Methods) {
				if (m.Name == ".cctor" || m.Name == ".ctor")
					continue;
				if (m == method)
					continue;
				return false;
			}
			return true;
		}
	}
}
