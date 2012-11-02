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
using dot10.DotNet;
using de4dot.blocks;

namespace de4dot.code {
	class ExternalAssembly {
		AssemblyDef asmDef;

		public ExternalAssembly(AssemblyDef asmDef) {
			this.asmDef = asmDef;
		}

		public TypeDef resolve(ITypeDefOrRef type) {
			foreach (var module in asmDef.Modules) {
				var typeDef = module.Find(type);
				if (typeDef != null)
					return typeDef;
			}

			return null;
		}

		public void unload(string asmFullName) {
			foreach (var module in asmDef.Modules) {
				//TODO: DotNetUtils.typeCaches.invalidate(module);
				TheAssemblyResolver.Instance.removeModule(module);
			}
			TheAssemblyResolver.Instance.removeModule(asmFullName);
		}
	}

	// Loads assemblies that aren't renamed
	class ExternalAssemblies {
		Dictionary<string, ExternalAssembly> assemblies = new Dictionary<string, ExternalAssembly>(StringComparer.Ordinal);
		Dictionary<string, bool> failedLoads = new Dictionary<string, bool>(StringComparer.Ordinal);

		ExternalAssembly load(TypeRef type) {
			if (type == null || type.DefinitionAssembly == null)
				return null;
			var asmFullName = type.DefinitionAssembly.FullName;
			ExternalAssembly asm;
			if (assemblies.TryGetValue(asmFullName, out asm))
				return asm;

			var asmDef = TheAssemblyResolver.Instance.Resolve(type.DefinitionAssembly, type.OwnerModule);
			if (asmDef == null) {
				if (!failedLoads.ContainsKey(asmFullName))
					Log.w("Could not load assembly {0}", asmFullName);
				failedLoads[asmFullName] = true;
				return null;
			}
			if (assemblies.ContainsKey(asmDef.FullName)) {
				assemblies[asmFullName] = assemblies[asmDef.FullName];
				return assemblies[asmDef.FullName];
			}

			if (asmFullName == asmDef.FullName)
				Log.v("Loaded assembly {0}", asmFullName);
			else
				Log.v("Loaded assembly {0} (but wanted {1})", asmDef.FullName, asmFullName);

			asm = new ExternalAssembly(asmDef);
			assemblies[asmFullName] = asm;
			assemblies[asmDef.FullName] = asm;
			return asm;
		}

		public TypeDef resolve(TypeRef type) {
			if (type == null)
				return null;
			var asm = load(type);
			if (asm == null)
				return null;
			return asm.resolve(type);
		}

		public void unloadAll() {
			foreach (var pair in assemblies) {
				if (pair.Value == null)
					continue;
				pair.Value.unload(pair.Key);
			}
			assemblies.Clear();
			failedLoads.Clear();
		}
	}
}
