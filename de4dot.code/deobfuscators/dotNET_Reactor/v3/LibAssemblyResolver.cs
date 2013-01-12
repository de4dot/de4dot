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

using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v3 {
	// Find the assembly resolver that's used in lib mode (3.8+)
	class LibAssemblyResolver {
		ModuleDefMD module;
		MethodDef initMethod;
		List<EmbeddedResource> resources = new List<EmbeddedResource>();

		public TypeDef Type {
			get { return initMethod == null ? null : initMethod.DeclaringType; }
		}

		public MethodDef InitMethod {
			get { return initMethod; }
		}

		public IEnumerable<EmbeddedResource> Resources {
			get { return resources; }
		}

		public LibAssemblyResolver(ModuleDefMD module) {
			this.module = module;
		}

		public void find(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (checkInitMethod(DotNetUtils.getModuleTypeCctor(module), simpleDeobfuscator, deob))
				return;
			if (checkInitMethod(module.EntryPoint, simpleDeobfuscator, deob))
				return;
		}

		bool checkInitMethod(MethodDef checkMethod, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			var requiredFields = new string[] {
				"System.Collections.Hashtable",
				"System.Boolean",
			};

			foreach (var method in DotNetUtils.getCalledMethods(module, checkMethod)) {
				if (method.Body == null)
					continue;
				if (!method.IsStatic)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;

				var type = method.DeclaringType;
				if (!new FieldTypes(type).exactly(requiredFields))
					continue;
				var ctor = type.FindMethod(".ctor");
				if (ctor == null)
					continue;
				var handler = DeobUtils.getResolveMethod(ctor);
				if (handler == null)
					continue;
				simpleDeobfuscator.decryptStrings(handler, deob);
				var resourcePrefix = getResourcePrefix(handler);
				if (resourcePrefix == null)
					continue;

				for (int i = 0; ; i++) {
					var resource = DotNetUtils.getResource(module, resourcePrefix + i.ToString("D5")) as EmbeddedResource;
					if (resource == null)
						break;
					resources.Add(resource);
				}

				initMethod = method;
				return true;
			}

			return false;
		}

		string getResourcePrefix(MethodDef handler) {
			foreach (var s in DotNetUtils.getCodeStrings(handler)) {
				var resource = DotNetUtils.getResource(module, s + "00000") as EmbeddedResource;
				if (resource != null)
					return s;
			}
			return null;
		}
	}
}
