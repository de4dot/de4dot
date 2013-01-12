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
using System.IO;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Eazfuscator_NET {
	class ResourceResolver {
		ModuleDefMD module;
		AssemblyResolver assemblyResolver;
		TypeDef resolverType;
		MethodDef initMethod;
		MethodDef handlerMethod;
		List<string> resourceInfos = new List<string>();

		public TypeDef Type {
			get { return resolverType; }
		}

		public MethodDef InitMethod {
			get { return initMethod; }
		}

		public bool Detected {
			get { return resolverType != null; }
		}

		public ResourceResolver(ModuleDefMD module, AssemblyResolver assemblyResolver) {
			this.module = module;
			this.assemblyResolver = assemblyResolver;
		}

		public void find() {
			if (!assemblyResolver.Detected)
				return;
			checkCalledMethods(DotNetUtils.getModuleTypeCctor(module));
		}

		bool checkCalledMethods(MethodDef method) {
			if (method == null || method.Body == null)
				return false;

			foreach (var instr in method.Body.Instructions) {
				if (instr.OpCode.Code != Code.Call)
					continue;
				if (!checkInitMethod(instr.Operand as MethodDef))
					continue;

				return true;
			}

			return false;
		}

		bool checkInitMethod(MethodDef method) {
			if (method == null || !method.IsStatic || method.Body == null)
				return false;
			if (!DotNetUtils.isMethod(method, "System.Void", "()"))
				return false;
			var type = method.DeclaringType;
			if (type.NestedTypes.Count != 1)
				return false;
			if (DotNetUtils.getField(type, "System.Reflection.Assembly") == null)
				return false;

			var resolveHandler = DeobUtils.getResolveMethod(method);
			if (resolveHandler == null)
				return false;

			initMethod = method;
			resolverType = type;
			handlerMethod = resolveHandler;
			return true;
		}

		public void initialize(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (!initializeInfos(simpleDeobfuscator, deob))
				throw new ApplicationException("Could not initialize resource decrypter");
		}

		bool initializeInfos(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (handlerMethod == null)
				return true;

			foreach (var method in resolverType.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (!DeobUtils.hasInteger(method, ':') || !DeobUtils.hasInteger(method, '|'))
					continue;

				simpleDeobfuscator.deobfuscate(method);
				simpleDeobfuscator.decryptStrings(method, deob);
				if (!initializeInfos(method))
					continue;

				return true;
			}

			return false;
		}

		bool initializeInfos(MethodDef method) {
			foreach (var s in DotNetUtils.getCodeStrings(method)) {
				if (string.IsNullOrEmpty(s))
					continue;
				var ary = s.Split(':');

				foreach (var asmInfo in ary)
					resourceInfos.Add(asmInfo.Split('|')[0]);

				return true;
			}

			return false;
		}

		public List<AssemblyResolver.AssemblyInfo> mergeResources() {
			var list = new List<AssemblyResolver.AssemblyInfo>();
			foreach (var asmName in resourceInfos) {
				var asmInfo = assemblyResolver.get(asmName);
				if (asmInfo == null)
					throw new ApplicationException(string.Format("Could not find resource assembly {0}", Utils.toCsharpString(asmName)));

				DeobUtils.decryptAndAddResources(module, asmInfo.ResourceName, () => asmInfo.Data);
				list.Add(asmInfo);
			}
			resourceInfos.Clear();
			return list;
		}
	}
}
