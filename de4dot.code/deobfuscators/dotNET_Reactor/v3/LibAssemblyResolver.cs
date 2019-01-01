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
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v3 {
	// Find the assembly resolver that's used in lib mode (3.8+)
	class LibAssemblyResolver {
		ModuleDefMD module;
		MethodDef initMethod;
		List<EmbeddedResource> resources = new List<EmbeddedResource>();

		public TypeDef Type => initMethod?.DeclaringType;
		public MethodDef InitMethod => initMethod;
		public IEnumerable<EmbeddedResource> Resources => resources;
		public LibAssemblyResolver(ModuleDefMD module) => this.module = module;

		public void Find(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (CheckInitMethod(DotNetUtils.GetModuleTypeCctor(module), simpleDeobfuscator, deob))
				return;
			if (CheckInitMethod(module.EntryPoint, simpleDeobfuscator, deob))
				return;
		}

		bool CheckInitMethod(MethodDef checkMethod, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			var requiredFields = new string[] {
				"System.Collections.Hashtable",
				"System.Boolean",
			};

			foreach (var method in DotNetUtils.GetCalledMethods(module, checkMethod)) {
				if (method.Body == null)
					continue;
				if (!method.IsStatic)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Void", "()"))
					continue;

				var type = method.DeclaringType;
				if (!new FieldTypes(type).Exactly(requiredFields))
					continue;
				var ctor = type.FindMethod(".ctor");
				if (ctor == null)
					continue;
				var handler = DeobUtils.GetResolveMethod(ctor);
				if (handler == null)
					continue;
				simpleDeobfuscator.DecryptStrings(handler, deob);
				var resourcePrefix = GetResourcePrefix(handler);
				if (resourcePrefix == null)
					continue;

				for (int i = 0; ; i++) {
					var resource = DotNetUtils.GetResource(module, resourcePrefix + i.ToString("D5")) as EmbeddedResource;
					if (resource == null)
						break;
					resources.Add(resource);
				}

				initMethod = method;
				return true;
			}

			return false;
		}

		string GetResourcePrefix(MethodDef handler) {
			foreach (var s in DotNetUtils.GetCodeStrings(handler)) {
				if (DotNetUtils.GetResource(module, s + "00000") is EmbeddedResource resource)
					return s;
			}
			return null;
		}
	}
}
