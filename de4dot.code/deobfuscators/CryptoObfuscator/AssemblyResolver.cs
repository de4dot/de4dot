/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class AssemblyResolver {
		ModuleDefMD module;
		TypeDef resolverType;
		MethodDef resolverMethod;
		List<AssemblyInfo> assemblyInfos = new List<AssemblyInfo>();

		public class AssemblyInfo {
			public string assemblyName;
			public EmbeddedResource resource;
			public EmbeddedResource symbolsResource;
			public AssemblyInfo(string assemblyName, EmbeddedResource resource, EmbeddedResource symbolsResource) {
				this.assemblyName = assemblyName;
				this.resource = resource;
				this.symbolsResource = symbolsResource;
			}

			public override string ToString() {
				return string.Format("{{{0} => {1}}}", assemblyName, resource.Name);
			}
		}

		public bool Detected {
			get { return resolverType != null; }
		}

		public List<AssemblyInfo> AssemblyInfos {
			get { return assemblyInfos; }
		}

		public TypeDef Type {
			get { return resolverType; }
		}

		public MethodDef Method {
			get { return resolverMethod; }
		}

		public AssemblyResolver(ModuleDefMD module) {
			this.module = module;
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
				if (checkType(method.DeclaringType, method))
					break;
			}
		}

		bool checkType(TypeDef type, MethodDef initMethod) {
			if (DotNetUtils.findFieldType(type, "System.Collections.Hashtable", true) == null)
				return false;
			if (!checkInitMethod(initMethod))
				return false;

			List<AssemblyInfo> newAssemblyInfos = null;
			foreach (var s in DotNetUtils.getCodeStrings(initMethod)) {
				newAssemblyInfos = initializeEmbeddedAssemblies(s);
				if (newAssemblyInfos != null)
					break;
			}
			if (newAssemblyInfos == null)
				return false;

			resolverType = type;
			resolverMethod = initMethod;
			assemblyInfos = newAssemblyInfos;
			return true;
		}

		bool checkInitMethod(MethodDef initMethod) {
			if (!initMethod.HasBody)
				return false;

			var instructions = initMethod.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instrs = DotNetUtils.getInstructions(instructions, i, OpCodes.Ldnull, OpCodes.Ldftn, OpCodes.Newobj);
				if (instrs == null)
					continue;

				IMethod methodRef;
				var ldftn = instrs[1];
				var newobj = instrs[2];

				methodRef = ldftn.Operand as IMethod;
				if (methodRef == null || !new SigComparer().Equals(initMethod.DeclaringType, methodRef.DeclaringType))
					continue;

				methodRef = newobj.Operand as IMethod;
				if (methodRef == null || methodRef.FullName != "System.Void System.ResolveEventHandler::.ctor(System.Object,System.IntPtr)")
					continue;

				return true;
			}

			return false;
		}

		List<AssemblyInfo> initializeEmbeddedAssemblies(string s) {
			var sb = new StringBuilder(s.Length);
			foreach (var c in s)
				sb.Append((char)~c);
			var tmpAssemblyInfos = sb.ToString().Split(new string[] { "##" }, StringSplitOptions.RemoveEmptyEntries);
			if (tmpAssemblyInfos.Length == 0 || (tmpAssemblyInfos.Length & 1) == 1)
				return null;

			var newAssemblyInfos = new List<AssemblyInfo>(tmpAssemblyInfos.Length / 2);
			for (int i = 0; i < tmpAssemblyInfos.Length; i += 2) {
				var assemblyName = tmpAssemblyInfos[i];
				var resourceName = tmpAssemblyInfos[i + 1];
				var resource = DotNetUtils.getResource(module, resourceName) as EmbeddedResource;
				var symbolsResource = DotNetUtils.getResource(module, resourceName + "#") as EmbeddedResource;
				if (resource == null)
					return null;
				newAssemblyInfos.Add(new AssemblyInfo(assemblyName, resource, symbolsResource));
			}

			return newAssemblyInfos;
		}
	}
}
