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

using System.Collections.Generic;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.renamer {
	class ExternalAssembly {
		AssemblyDefinition asmDef;

		public ExternalAssembly(AssemblyDefinition asmDef) {
			this.asmDef = asmDef;
		}

		public TypeDefinition resolve(TypeReference type) {
			foreach (var module in asmDef.Modules) {
				var typeDef = DotNetUtils.getType(module, type);
				if (typeDef != null)
					return typeDef;
			}

			return null;
		}

		public void unload() {
			foreach (var module in asmDef.Modules)
				DotNetUtils.typeCaches.invalidate(module);
		}
	}

	// Loads assemblies that aren't renamed
	class ExternalAssemblies {
		Dictionary<string, ExternalAssembly> assemblies = new Dictionary<string, ExternalAssembly>();

		ExternalAssembly load(TypeReference type) {
			var asmFullName = DotNetUtils.getFullAssemblyName(type.Scope);
			ExternalAssembly asm;
			if (assemblies.TryGetValue(asmFullName, out asm))
				return asm;

			AssemblyDefinition asmDef = null;
			try {
				asmDef = GlobalAssemblyResolver.Instance.Resolve(asmFullName);
			}
			catch (ResolutionException) {
			}
			catch (AssemblyResolutionException) {
			}
			if (asmDef == null) {
				// If we can't load it now, we can't load it later. Make sure above code returns null.
				assemblies[asmFullName] = null;
				Log.w("Could not load assembly {0}", asmFullName);
				return null;
			}
			Log.v("Loaded assembly {0}", asmFullName);

			return assemblies[asmFullName] = new ExternalAssembly(asmDef);
		}

		public TypeDefinition resolve(TypeReference type) {
			var asm = load(type);
			if (asm == null)
				return null;
			return asm.resolve(type);
		}

		public void unloadAll() {
			foreach (var asm in assemblies.Values)
				asm.unload();
			assemblies.Clear();
		}
	}
}
