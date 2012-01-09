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

using System;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class ResourceResolver {
		ModuleDefinition module;
		ResourceDecrypter resourceDecrypter;
		TypeDefinition resolverType;
		MethodDefinition resolverMethod;
		ResolverVersion resolverVersion = ResolverVersion.V1;
		bool mergedIt = false;

		enum ResolverVersion {
			V1,
			V2,
		}

		public TypeDefinition Type {
			get { return resolverType; }
		}

		public MethodDefinition Method {
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

			var resource = DotNetUtils.getResource(module, getResourceName()) as EmbeddedResource;
			if (resource == null)
				return null;

			DeobUtils.decryptAndAddResources(module, resource.Name, () => resourceDecrypter.decrypt(resource.GetResourceStream()));
			mergedIt = true;
			return resource;
		}

		string getResourceName() {
			switch (resolverVersion) {
			case ResolverVersion.V1: return module.Assembly.Name.Name;
			case ResolverVersion.V2: return string.Format("{0}{0}{0}", module.Assembly.Name.Name);
			default: throw new ApplicationException("Unknown version");
			}
		}

		bool checkType(TypeDefinition type, MethodDefinition initMethod) {
			if (!initMethod.HasBody)
				return false;
			if (DotNetUtils.findFieldType(type, "System.Reflection.Assembly", true) == null)
				return false;

			var instructions = initMethod.Body.Instructions;
			int foundCount = 0;
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

				foundCount++;
			}
			if (foundCount == 0)
				return false;

			switch (foundCount) {
			case 1:
				resolverVersion = ResolverVersion.V1;
				break;
			case 2:
				resolverVersion = ResolverVersion.V2;
				break;
			default:
				return false;
			}

			resolverType = type;
			resolverMethod = initMethod;
			return true;
		}
	}
}
