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

using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.deobfuscators.SmartAssembly {
	class ResourceResolverInfo : ResolverInfoBase {
		EmbeddedResource embeddedAssembliesResource;

		public EmbeddedResource Resource {
			get { return embeddedAssembliesResource; }
		}

		public ResourceResolverInfo(ModuleDefinition module, ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob)
			: base(module, simpleDeobfuscator, deob) {
		}

		protected override bool checkResolverType(TypeDefinition type) {
			return DotNetUtils.findFieldType(type, "System.Reflection.Assembly", true) != null;
		}

		protected override bool checkHandlerMethod(MethodDefinition method) {
			if (!method.IsStatic || !method.HasBody)
				return false;

			EmbeddedResource resource = null;
			var instructions = method.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instrs = DotNetUtils.getInstructions(instructions, i, OpCodes.Ldstr, OpCodes.Call);
				if (instrs == null)
					continue;

				var s = instrs[0].Operand as string;
				var calledMethod = instrs[1].Operand as MethodReference;
				if (s == null || calledMethod == null)
					continue;

				resource = DotNetUtils.getResource(module, Utils.getAssemblySimpleName(s)) as EmbeddedResource;
				if (resource != null)
					break;
			}
			if (resource == null)
				return false;

			embeddedAssembliesResource = resource;
			Log.v("Found embedded assemblies resource {0}", Utils.toCsharpString(embeddedAssembliesResource.Name));
			return true;
		}
	}
}
