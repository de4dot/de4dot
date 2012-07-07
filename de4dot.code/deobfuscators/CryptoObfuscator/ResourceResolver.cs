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
using System.Collections.Generic;
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
			None,
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
			var cctor = DotNetUtils.getModuleTypeCctor(module);
			if (cctor == null)
				return;

			foreach (var method in DotNetUtils.getCalledMethods(module, cctor)) {
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (checkType(method))
					break;
			}
		}

		public EmbeddedResource mergeResources() {
			if (mergedIt)
				return null;

			var resource = DotNetUtils.getResource(module, getResourceNames()) as EmbeddedResource;
			if (resource == null)
				return null;

			DeobUtils.decryptAndAddResources(module, resource.Name, () => resourceDecrypter.decrypt(resource.GetResourceStream()));
			mergedIt = true;
			return resource;
		}

		IEnumerable<string> getResourceNames() {
			var names = new List<string>();

			switch (resolverVersion) {
			case ResolverVersion.V1:
				names.Add(module.Assembly.Name.Name);
				break;

			case ResolverVersion.V2:
				names.Add(string.Format("{0}{0}{0}", module.Assembly.Name.Name));
				names.Add(string.Format("{0}&", module.Assembly.Name.Name));
				break;

			default:
				throw new ApplicationException("Unknown version");
			}

			return names;
		}

		bool checkType(MethodDefinition initMethod) {
			if (!initMethod.HasBody)
				return false;
			if (DotNetUtils.findFieldType(initMethod.DeclaringType, "System.Reflection.Assembly", true) == null)
				return false;

			resolverVersion = checkSetupMethod(initMethod);
			if (resolverVersion == ResolverVersion.None)
				resolverVersion = checkSetupMethod(DotNetUtils.getMethod(initMethod.DeclaringType, ".cctor"));
			if (resolverVersion == ResolverVersion.None)
				return false;

			resolverType = initMethod.DeclaringType;
			resolverMethod = initMethod;
			return true;
		}

		ResolverVersion checkSetupMethod(MethodDefinition setupMethod) {
			var instructions = setupMethod.Body.Instructions;
			int foundCount = 0;
			for (int i = 0; i < instructions.Count; i++) {
				var instrs = DotNetUtils.getInstructions(instructions, i, OpCodes.Ldnull, OpCodes.Ldftn, OpCodes.Newobj);
				if (instrs == null)
					continue;

				MethodReference methodRef;
				var ldftn = instrs[1];
				var newobj = instrs[2];

				methodRef = ldftn.Operand as MethodReference;
				if (methodRef == null || !MemberReferenceHelper.compareTypes(setupMethod.DeclaringType, methodRef.DeclaringType))
					continue;

				methodRef = newobj.Operand as MethodReference;
				if (methodRef == null || methodRef.FullName != "System.Void System.ResolveEventHandler::.ctor(System.Object,System.IntPtr)")
					continue;

				foundCount++;
			}
			if (foundCount == 0)
				return ResolverVersion.None;

			switch (foundCount) {
			case 1: return ResolverVersion.V1;
			case 2: return ResolverVersion.V2;
			default: return ResolverVersion.None;
			}
		}
	}
}
