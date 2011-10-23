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

using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.deobfuscators.CryptoObfuscator {
	class ResourceResolver {
		ModuleDefinition module;
		ResourceDecrypter resourceDecrypter;
		TypeDefinition resolverType;
		MethodDefinition resolverMethod;
		bool mergedIt = false;

		public TypeDefinition ResolverType {
			get { return resolverType; }
		}

		public MethodDefinition ResolverMethod {
			get { return resolverMethod; }
		}

		public ResourceResolver(ModuleDefinition module, ResourceDecrypter resourceDecrypter) {
			this.module = module;
			this.resourceDecrypter = resourceDecrypter;
		}

		public void find() {
			var cctor = DotNetUtils.getMethod(DotNetUtils.getModuleType(module), ".cctor");
			if (cctor == null)
				return;

			foreach (var tuple in DotNetUtils.getCalledMethods(module, cctor)) {
				var method = tuple.Item2;
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (checkType(tuple.Item1, method))
					break;
			}
		}

		public EmbeddedResource mergeResources() {
			if (mergedIt)
				return null;

			var resource = DotNetUtils.getResource(module, module.Assembly.Name.Name) as EmbeddedResource;
			if (resource == null)
				return null;

			DeobUtils.decryptAndAddResources(module, resource.Name, () => resourceDecrypter.decrypt(resource.GetResourceStream()));
			mergedIt = true;
			return resource;
		}

		bool checkType(TypeDefinition type, MethodDefinition initMethod) {
			if (!initMethod.HasBody)
				return false;
			if (DotNetUtils.findFieldType(type, "System.Reflection.Assembly", true) == null)
				return false;

			var instructions = initMethod.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instrs = DotNetUtils.getInstructions(instructions, i, OpCodes.Ldnull, OpCodes.Ldftn, OpCodes.Newobj);
				if (instrs == null)
					continue;

				MethodReference methodRef;
				var ldftn = instrs[1];
				var newobj = instrs[2];

				methodRef = ldftn.Operand as MethodReference;
				if (methodRef == null || !MemberReferenceHelper.compareTypes(type, methodRef.DeclaringType))
					continue;

				methodRef = newobj.Operand as MethodReference;
				if (methodRef == null || methodRef.FullName != "System.Void System.ResolveEventHandler::.ctor(System.Object,System.IntPtr)")
					continue;

				resolverType = type;
				resolverMethod = initMethod;
				return true;
			}

			return false;
		}
	}
}
