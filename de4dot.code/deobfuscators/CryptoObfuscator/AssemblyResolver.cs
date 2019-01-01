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
		string asmSeparator;

		public class AssemblyInfo {
			public string assemblyName;
			public EmbeddedResource resource;
			public EmbeddedResource symbolsResource;
			public AssemblyInfo(string assemblyName, EmbeddedResource resource, EmbeddedResource symbolsResource) {
				this.assemblyName = assemblyName;
				this.resource = resource;
				this.symbolsResource = symbolsResource;
			}

			public override string ToString() => $"{{{assemblyName} => {resource.Name}}}";
		}

		public bool Detected => resolverType != null;
		public List<AssemblyInfo> AssemblyInfos => assemblyInfos;
		public TypeDef Type => resolverType;
		public MethodDef Method => resolverMethod;
		public AssemblyResolver(ModuleDefMD module) => this.module = module;

		public void Find(ISimpleDeobfuscator simpleDeobfuscator) {
			var cctor = DotNetUtils.GetModuleTypeCctor(module);
			if (cctor == null)
				return;

			foreach (var method in DotNetUtils.GetCalledMethods(module, cctor)) {
				if (method.Name == ".cctor" || method.Name == ".ctor")
					continue;
				if (!method.IsStatic || !DotNetUtils.IsMethod(method, "System.Void", "()"))
					continue;
				if (CheckType(method.DeclaringType, method, simpleDeobfuscator))
					break;
			}
		}

		bool CheckType(TypeDef type, MethodDef initMethod, ISimpleDeobfuscator simpleDeobfuscator) {
			if (DotNetUtils.FindFieldType(type, "System.Collections.Hashtable", true) == null)
				return false;
			simpleDeobfuscator.Deobfuscate(initMethod);
			if (!CheckInitMethod(initMethod))
				return false;
			if ((asmSeparator = FindAssemblySeparator(initMethod)) == null)
				return false;

			List<AssemblyInfo> newAssemblyInfos = null;
			foreach (var s in DotNetUtils.GetCodeStrings(initMethod)) {
				newAssemblyInfos = InitializeEmbeddedAssemblies(s);
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

		bool CheckInitMethod(MethodDef initMethod) {
			if (!initMethod.HasBody)
				return false;

			var instructions = initMethod.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instrs = DotNetUtils.GetInstructions(instructions, i, OpCodes.Ldnull, OpCodes.Ldftn, OpCodes.Newobj);
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

		List<AssemblyInfo> InitializeEmbeddedAssemblies(string s) {
			var sb = new StringBuilder(s.Length);
			foreach (var c in s)
				sb.Append((char)~c);
			var tmpAssemblyInfos = sb.ToString().Split(new string[] { asmSeparator }, StringSplitOptions.RemoveEmptyEntries);
			if (tmpAssemblyInfos.Length == 0 || (tmpAssemblyInfos.Length & 1) == 1)
				return null;

			var newAssemblyInfos = new List<AssemblyInfo>(tmpAssemblyInfos.Length / 2);
			for (int i = 0; i < tmpAssemblyInfos.Length; i += 2) {
				var assemblyName = tmpAssemblyInfos[i];
				var resourceName = tmpAssemblyInfos[i + 1];
				var resource = DotNetUtils.GetResource(module, resourceName) as EmbeddedResource;
				var symbolsResource = DotNetUtils.GetResource(module, resourceName + "#") as EmbeddedResource;
				if (resource == null)
					return null;
				newAssemblyInfos.Add(new AssemblyInfo(assemblyName, resource, symbolsResource));
			}

			return newAssemblyInfos;
		}

		string FindAssemblySeparator(MethodDef initMethod) {
			if (!initMethod.HasBody)
				return null;

			foreach (var instr in initMethod.Body.Instructions) {
				if (instr.OpCode.Code != Code.Newarr)
					continue;
				var op = module.CorLibTypes.GetCorLibTypeSig(instr.Operand as ITypeDefOrRef);
				if (op == null)
					continue;
				if (op.ElementType == ElementType.String)
					return "##";
				if (op.ElementType == ElementType.Char)
					return "`";
			}

			return null;
		}
	}
}
