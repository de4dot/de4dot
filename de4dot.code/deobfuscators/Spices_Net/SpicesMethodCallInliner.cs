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

using Mono.Cecil;
using Mono.Cecil.Metadata;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.code.deobfuscators.Spices_Net {
	class SpicesMethodCallInliner : MethodCallInliner {
		ModuleDefinition module;
		TypeDefinitionDict<bool> validTypes = new TypeDefinitionDict<bool>();

		public SpicesMethodCallInliner(ModuleDefinition module)
			: base(false) {
			this.module = module;
		}

		public bool checkCanInline(MethodDefinition method) {
			return validTypes.find(method.DeclaringType);
		}

		protected override bool canInline(MethodDefinition method) {
			return checkCanInline(method);
		}

		public void initialize() {
			foreach (var type in module.GetTypes()) {
				if (checkValidType(type))
					validTypes.add(type, true);
			}
		}

		static bool checkValidType(TypeDefinition type) {
			if ((type.Attributes & ~TypeAttributes.BeforeFieldInit) != TypeAttributes.NestedAssembly)
				return false;
			if (type.HasProperties || type.HasEvents || type.HasFields || type.HasNestedTypes)
				return false;
			if (type.GenericParameters.Count > 0)
				return false;
			if (type.IsValueType || type.IsInterface)
				return false;
			if (type.BaseType == null || type.BaseType.EType != ElementType.Object)
				return false;
			if (type.Interfaces.Count > 0)
				return false;
			if (!checkValidTypeMethods(type))
				return false;

			return true;
		}

		static bool checkValidTypeMethods(TypeDefinition type) {
			bool foundCtor = false;
			int numMethods = 0;

			foreach (var method in type.Methods) {
				if (method.Name == ".cctor")
					return false;
				if (method.Name == ".ctor") {
					if (method.Parameters.Count != 0)
						return false;
					foundCtor = true;
					continue;
				}
				if (method.Attributes != (MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig))
					return false;
				if (method.HasPInvokeInfo || method.PInvokeInfo != null)
					return false;
				if (method.GenericParameters.Count > 0)
					return false;

				numMethods++;
			}

			return numMethods > 0 && foundCtor;
		}
	}
}
