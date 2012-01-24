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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	// DS 4.x can move fields from a class to a struct. This class restores the fields.
	class FieldsRestorer {
		ModuleDefinition module;
		TypeDefinitionDict<TypeDefinition> structToOwner = new TypeDefinitionDict<TypeDefinition>();
		Dictionary<int, bool> oldFieldToken = new Dictionary<int, bool>();

		public List<TypeDefinition> FieldStructs {
			get {
				var list = new List<TypeDefinition>(structToOwner.Count);
				foreach (var structType in structToOwner.getKeys()) {
					if (structType.Methods.Count != 0)
						continue;

					list.Add(structType);
				}
				return list;
			}
		}

		public FieldsRestorer(ModuleDefinition module) {
			this.module = module;
		}

		public void initialize() {
			foreach (var kv in getMovedTypes()) {
				var structType = kv.Key;
				var ownerType = kv.Value;
				structToOwner.add(structType, ownerType);

				for (int i = 0; i < ownerType.Fields.Count; i++) {
					if (DotNetUtils.getType(module, ownerType.Fields[i].FieldType) != structType)
						continue;
					oldFieldToken[ownerType.Fields[i].MetadataToken.ToInt32()] = true;
					ownerType.Fields.RemoveAt(i);
					break;
				}

				var structTypeFields = new List<FieldDefinition>(structType.Fields);
				structType.Fields.Clear();
				foreach (var field in structTypeFields)
					ownerType.Fields.Add(field);

				// Add a field so peverify won't complain if this type isn't removed
				structType.Fields.Add(new FieldDefinition("a", FieldAttributes.Public, module.TypeSystem.Byte));
			}
		}

		Dictionary<TypeDefinition, TypeDefinition> getMovedTypes() {
			var fieldTypeToTypes = new Dictionary<TypeDefinition, List<TypeDefinition>>();
			foreach (var type in module.GetTypes()) {
				foreach (var field in type.Fields) {
					var fieldType = DotNetUtils.getType(module, field.FieldType);
					if (fieldType == null || !fieldType.IsValueType)
						continue;
					if ((fieldType.Attributes & ~TypeAttributes.Sealed) != TypeAttributes.NestedAssembly)
						continue;
					if (fieldType.NestedTypes.Count > 0)
						continue;
					if (fieldType.GenericParameters.Count > 0)
						continue;
					if (fieldType.Fields.Count == 0)
						continue;
					if (hasNonStaticMethods(fieldType))
						continue;

					List<TypeDefinition> list;
					if (!fieldTypeToTypes.TryGetValue(fieldType, out list))
						fieldTypeToTypes[fieldType] = list = new List<TypeDefinition>();
					list.Add(type);
				}
			}

			var candidates = new Dictionary<TypeDefinition, TypeDefinition>();
			foreach (var kv in fieldTypeToTypes) {
				if (kv.Value.Count != 1)
					continue;
				candidates[kv.Key] = kv.Value[0];
			}

			foreach (var type in module.GetTypes()) {
				foreach (var field in type.Fields) {
					if (field.DeclaringType != type)
						removeType(candidates, field.FieldType);
				}
				foreach (var method in type.Methods) {
					removeType(candidates, method.MethodReturnType.ReturnType);
					foreach (var parameter in method.Parameters)
						removeType(candidates, parameter.ParameterType);
					if (method.Body != null) {
						foreach (var local in method.Body.Variables)
							removeType(candidates, local.VariableType);
					}
				}
			}

			return candidates;
		}

		void removeType(Dictionary<TypeDefinition, TypeDefinition> candidates, TypeReference type) {
			var typeDef = DotNetUtils.getType(module, type);
			if (typeDef == null)
				return;
			candidates.Remove(typeDef);
		}

		static bool hasNonStaticMethods(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (method.Name == ".cctor")
					continue;
				if (!method.IsStatic)
					return true;
				if (method.GenericParameters.Count > 0)
					continue;
				if (method.Body == null)
					return true;
				if (method.HasPInvokeInfo || method.PInvokeInfo != null)
					return true;
			}
			return false;
		}

		public void deobfuscate(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = instrs.Count - 1; i >= 0; i--) {
					var instr = instrs[i];
					if (instr.OpCode.Code != Code.Ldflda)
						continue;
					var field = instr.Operand as FieldReference;
					if (field == null)
						continue;
					if (!oldFieldToken.ContainsKey(field.MetadataToken.ToInt32()))
						continue;
					instrs.RemoveAt(i);
				}
			}
		}
	}
}
