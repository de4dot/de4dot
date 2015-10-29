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

using System;
using System.Collections.Generic;
using dnlib.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class ResourceResolver {
		ModuleDefMD module;
		ResourceDecrypter resourceDecrypter;
		TypeDef resolverType;
		MethodDef resolverMethod;
		ResolverVersion resolverVersion = ResolverVersion.V1;
		bool mergedIt = false;

		enum ResolverVersion {
			None,
			V1,
			V2,
		}

		public TypeDef Type {
			get { return resolverType; }
		}

		public MethodDef Method {
			get { return resolverMethod; }
		}

		public ResourceResolver(ModuleDefMD module, ResourceDecrypter resourceDecrypter) {
			this.module = module;
			this.resourceDecrypter = resourceDecrypter;
		}

		public void Find() {
			var cctor = DotNetUtils.GetModuleTypeCctor(module);
			if (cctor == null)
				return;

			foreach (var method in DotNetUtils.GetCalledMethods(module, cctor)) {
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.IsMethod(method, "System.Void", "()"))
					continue;
				if (CheckType(method))
					break;
			}
		}

		public EmbeddedResource MergeResources() {
			if (mergedIt)
				return null;

			var resource = DotNetUtils.GetResource(module, GetResourceNames()) as EmbeddedResource;
			if (resource == null)
				return null;

			resource.Data.Position = 0;
			DeobUtils.DecryptAndAddResources(module, resource.Name.String, () => resourceDecrypter.Decrypt(resource.Data.CreateStream()));
			mergedIt = true;
			return resource;
		}

		IEnumerable<string> GetResourceNames() {
			var names = new List<string>();

			switch (resolverVersion) {
			case ResolverVersion.V1:
				names.Add(module.Assembly.Name.String);
				break;

			case ResolverVersion.V2:
				names.Add(string.Format("{0}{0}{0}", module.Assembly.Name.String));
				names.Add(string.Format("{0}&", module.Assembly.Name.String));
				break;

			default:
				throw new ApplicationException("Unknown version");
			}

			return names;
		}

		bool CheckType(MethodDef initMethod) {
			if (!initMethod.HasBody)
				return false;
			if (DotNetUtils.FindFieldType(initMethod.DeclaringType, "System.Reflection.Assembly", true) == null)
				return false;

			resolverVersion = CheckSetupMethod(initMethod);
			if (resolverVersion == ResolverVersion.None)
				resolverVersion = CheckSetupMethod(initMethod.DeclaringType.FindStaticConstructor());
			if (resolverVersion == ResolverVersion.None)
				return false;

			resolverType = initMethod.DeclaringType;
			resolverMethod = initMethod;
			return true;
		}

		ResolverVersion CheckSetupMethod(MethodDef setupMethod) {
			var instructions = setupMethod.Body.Instructions;
			int foundCount = 0;
			for (int i = 0; i < instructions.Count; i++) {
				var instrs = DotNetUtils.GetInstructions(instructions, i, OpCodes.Ldnull, OpCodes.Ldftn, OpCodes.Newobj);
				if (instrs == null)
					continue;

				IMethod methodRef;
				var ldftn = instrs[1];
				var newobj = instrs[2];

				methodRef = ldftn.Operand as IMethod;
				if (methodRef == null || !new SigComparer().Equals(setupMethod.DeclaringType, methodRef.DeclaringType))
					continue;

				methodRef = newobj.Operand as IMethod;
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
