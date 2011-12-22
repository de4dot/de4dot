/*
    Copyright (C) 2011 de4dot@gmail.com

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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.SmartAssembly {
	abstract class ResolverInfoBase {
		protected ModuleDefinition module;
		ISimpleDeobfuscator simpleDeobfuscator;
		IDeobfuscator deob;
		TypeDefinition resolverType;
		TypeDefinition callResolverType;
		MethodDefinition callResolverMethod;

		public TypeDefinition Type {
			get { return resolverType; }
		}

		public TypeDefinition CallResolverType {
			get { return callResolverType; }
		}

		public MethodDefinition CallResolverMethod {
			get { return callResolverMethod; }
		}

		public ResolverInfoBase(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			this.module = module;
			this.simpleDeobfuscator = simpleDeobfuscator;
			this.deob = deob;
		}

		public bool findTypes() {
			if (resolverType != null)
				return true;

			if (findTypes(DotNetUtils.getMethod(DotNetUtils.getModuleType(module), ".cctor")))
				return true;
			if (findTypes(module.EntryPoint))
				return true;

			return false;
		}

		bool findTypes(MethodDefinition initMethod) {
			if (initMethod == null)
				return false;
			foreach (var tuple in DotNetUtils.getCalledMethods(module, initMethod)) {
				var method = tuple.Item2;
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (checkAttachAppType(tuple.Item1, method))
					return true;
			}

			return false;
		}

		bool checkAttachAppType(TypeDefinition type, MethodDefinition attachAppMethod) {
			callResolverType = null;
			if (!attachAppMethod.HasBody)
				return false;
			if (type.Fields.Count > 0 || type.Properties.Count > 0 || type.Events.Count > 0)
				return false;
			foreach (var m in type.Methods) {
				if (m.Name == ".cctor" || m.Name == ".ctor")
					continue;
				if (m == attachAppMethod)
					continue;
				return false;
			}

			foreach (var tuple in DotNetUtils.getCalledMethods(module, attachAppMethod)) {
				var method = tuple.Item2;
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (!checkResolverType(tuple.Item1, method))
					continue;

				callResolverMethod = attachAppMethod;
				callResolverType = type;
				return true;
			}

			if (hasLdftn(attachAppMethod)) {
				simpleDeobfuscator.deobfuscate(attachAppMethod);
				foreach (var resolverHandler in getResolverHandlers(type, attachAppMethod)) {
					if (!resolverHandler.HasBody)
						continue;
					if (!checkResolverType2(resolverHandler.DeclaringType))
						continue;
					deobfuscate(resolverHandler);
					if (checkHandlerMethod(resolverHandler)) {
						callResolverMethod = attachAppMethod;
						callResolverType = type;
						resolverType = resolverHandler.DeclaringType;
						return true;
					}
				}
			}

			return false;
		}

		static bool hasLdftn(MethodDefinition method) {
			if (method == null || method.Body == null)
				return false;
			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code == Code.Ldftn)
					return true;
			}
			return false;
		}

		bool checkResolverType2(TypeDefinition type) {
			if (type.Properties.Count > 1 || type.Events.Count > 0)
				return false;
			if (!checkResolverType(type))
				return false;

			return true;
		}

		bool checkResolverType(TypeDefinition type, MethodDefinition initMethod) {
			resolverType = null;
			if (!initMethod.HasBody)
				return false;
			if (!checkResolverType2(type))
				return false;

			deobfuscate(initMethod);
			foreach (var handlerDef in getResolverHandlers(type, initMethod)) {
				deobfuscate(handlerDef);
				if (checkHandlerMethod(handlerDef)) {
					resolverType = type;
					return true;
				}
			}

			return false;
		}

		void deobfuscate(MethodDefinition method) {
			simpleDeobfuscator.deobfuscate(method);
			simpleDeobfuscator.decryptStrings(method, deob);
		}

		protected abstract bool checkResolverType(TypeDefinition type);
		protected abstract bool checkHandlerMethod(MethodDefinition handler);

		static IEnumerable<MethodDefinition> getResolverHandlers(TypeDefinition type, MethodDefinition method) {
			int numHandlers = 0;
			var instructions = method.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instrs = DotNetUtils.getInstructions(instructions, i, OpCodes.Call, OpCodes.Ldnull, OpCodes.Ldftn, OpCodes.Newobj, OpCodes.Callvirt);
				if (instrs == null)
					continue;

				var call = instrs[0];
				if (!DotNetUtils.isMethod(call.Operand as MethodReference, "System.AppDomain", "()"))
					continue;

				var ldftn = instrs[2];
				var handlerDef = DotNetUtils.getMethod(type, ldftn.Operand as MethodReference);
				if (handlerDef == null)
					continue;

				var newobj = instrs[3];
				if (!DotNetUtils.isMethod(newobj.Operand as MethodReference, "System.Void", "(System.Object,System.IntPtr)"))
					continue;

				var callvirt = instrs[4];
				if (!DotNetUtils.isMethod(callvirt.Operand as MethodReference, "System.Void", "(System.ResolveEventHandler)"))
					continue;

				numHandlers++;
				yield return handlerDef;
			}

			// If no handlers found, it's possible that the method itself is the handler.
			if (numHandlers == 0)
				yield return method;
		}
	}
}
