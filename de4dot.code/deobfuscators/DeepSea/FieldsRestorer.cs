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
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Metadata;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.DeepSea {
	// DS 4.x can move fields from a class to a struct. This class restores the fields.
	class FieldsRestorer {
		ModuleDefinition module;
		TypeDefinitionDict<List<TypeDefinition>> structToOwners = new TypeDefinitionDict<List<TypeDefinition>>();
		FieldDefinitionAndDeclaringTypeDict<bool> structFieldsToFix = new FieldDefinitionAndDeclaringTypeDict<bool>();
		TypeDefinitionDict<FieldDefinitionAndDeclaringTypeDict<FieldDefinition>> typeToFieldsDict = new TypeDefinitionDict<FieldDefinitionAndDeclaringTypeDict<FieldDefinition>>();

		public List<TypeDefinition> FieldStructs {
			get {
				var list = new List<TypeDefinition>(structToOwners.Count);
				foreach (var structType in structToOwners.getKeys()) {
					if (!hasNoMethods(structType))
						continue;

					list.Add(structType);
				}
				return list;
			}
		}

		static bool hasNoMethods(TypeDefinition type) {
			if (type.Methods.Count == 0)
				return true;
			if (type.BaseType == null)
				return false;
			if (type.BaseType.EType != ElementType.Object)
				return false;
			if (type.Methods.Count != 1)
				return false;
			var ctor = type.Methods[0];
			if (ctor.Name != ".ctor" || ctor.Parameters.Count != 0)
				return false;
			return true;
		}

		public FieldsRestorer(ModuleDefinition module) {
			this.module = module;
		}

		public void initialize() {
			foreach (var kv in getMovedTypes()) {
				var structType = kv.Key;
				structToOwners.add(structType, kv.Value);

				foreach (var ownerType in kv.Value) {
					foreach (var ownerField in ownerType.Fields) {
						if (DotNetUtils.getType(module, ownerField.FieldType) != structType)
							continue;
						structFieldsToFix.add(ownerField, true);
						break;
					}

					var fieldsDict = new FieldDefinitionAndDeclaringTypeDict<FieldDefinition>();
					typeToFieldsDict.add(ownerType, fieldsDict);
					foreach (var structField in structType.Fields) {
						var newField = DotNetUtils.createFieldDefinition(structField.Name, structField.Attributes, structField.FieldType);
						ownerType.Fields.Add(newField);
						fieldsDict.add(structField, newField);
					}
				}
			}
		}

		Dictionary<TypeDefinition, List<TypeDefinition>> getMovedTypes() {
			var candidates = new Dictionary<TypeDefinition, List<TypeDefinition>>();
			var typeToStruct = new Dictionary<TypeDefinition, TypeDefinition>();
			foreach (var type in module.GetTypes()) {
				foreach (var field in getPossibleFields(type)) {
					var fieldType = DotNetUtils.getType(module, field.FieldType);
					if (fieldType == null)
						continue;
					if (!checkBaseType(fieldType))
						continue;
					if ((fieldType.Attributes & ~TypeAttributes.Sealed) != TypeAttributes.NestedAssembly)
						continue;
					if (fieldType.NestedTypes.Count > 0)
						continue;
					if (fieldType.GenericParameters.Count > 0)
						continue;
					if (fieldType.Fields.Count == 0)
						continue;
					if (fieldType.HasEvents || fieldType.HasProperties || fieldType.HasInterfaces)
						continue;
					if (checkMethods(fieldType))
						continue;
					if (!checkFields(fieldType))
						continue;

					List<TypeDefinition> list;
					if (!candidates.TryGetValue(fieldType, out list))
						candidates[fieldType] = list = new List<TypeDefinition>();
					list.Add(type);
					typeToStruct[type] = fieldType;
					break;
				}
			}

			foreach (var type in module.GetTypes()) {
				TypeDefinition structType;
				typeToStruct.TryGetValue(type, out structType);

				foreach (var field in type.Fields) {
					if (field.IsStatic || field.FieldType != structType)
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

		IEnumerable<FieldDefinition> getPossibleFields(TypeDefinition type) {
			var typeToFields = new TypeDefinitionDict<List<FieldDefinition>>();
			foreach (var field in type.Fields) {
				if (field.Attributes != FieldAttributes.Private)
					continue;
				var fieldType = DotNetUtils.getType(module, field.FieldType);
				if (fieldType == null)
					continue;
				if (!checkBaseType(fieldType))
					continue;
				var list = typeToFields.find(fieldType);
				if (list == null)
					typeToFields.add(fieldType, list = new List<FieldDefinition>());
				list.Add(field);
			}

			foreach (var list in typeToFields.getValues()) {
				if (list.Count == 1)
					yield return list[0];
			}
		}

		static bool checkBaseType(TypeDefinition type) {
			if (type == null || type.BaseType == null)
				return false;
			return type.BaseType.FullName == "System.ValueType" || type.BaseType.EType == ElementType.Object;
		}

		void removeType(Dictionary<TypeDefinition, List<TypeDefinition>> candidates, TypeReference type) {
			var typeDef = DotNetUtils.getType(module, type);
			if (typeDef == null)
				return;
			candidates.Remove(typeDef);
		}

		static bool checkMethods(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (method.Name == ".cctor")
					continue;
				if (type.BaseType != null && type.BaseType.EType == ElementType.Object && method.Name == ".ctor" && method.Parameters.Count == 0)
					continue;
				if (!method.IsStatic)
					return true;
				if (method.GenericParameters.Count > 0)
					return true;
				if (method.Body == null)
					return true;
				if (method.HasPInvokeInfo || method.PInvokeInfo != null)
					return true;
			}
			return false;
		}

		static bool checkFields(TypeDefinition type) {
			if (type.Fields.Count == 0)
				return false;
			foreach (var field in type.Fields) {
				if (field.IsStatic)
					return false;
				if (!field.IsAssembly)
					return false;
			}
			return true;
		}

		public void deobfuscate(Blocks blocks) {
			deobfuscateNormal(blocks);
			fixFieldCtorCalls(blocks);
		}

		void deobfuscateNormal(Blocks blocks) {
			var instrsToRemove = new List<int>();
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				instrsToRemove.Clear();
				var instrs = block.Instructions;
				for (int i = instrs.Count - 1; i >= 0; i--) {
					var instr = instrs[i];
					if (instr.OpCode.Code != Code.Ldflda && instr.OpCode.Code != Code.Ldfld)
						continue;
					var structField = instr.Operand as FieldReference;
					if (structField == null || !structFieldsToFix.find(structField))
						continue;

					var ldStFld = instrs[findLdStFieldIndex(instrs, i + 1)];
					ldStFld.Operand = getNewField(structField, ldStFld.Operand as FieldReference);
					instrsToRemove.Add(i);
				}
				if (instrsToRemove.Count > 0)
					block.remove(instrsToRemove);
			}
		}

		void fixFieldCtorCalls(Blocks blocks) {
			if (blocks.Method.Name != ".ctor")
				return;
			var instrsToRemove = new List<int>();
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var stfld = instrs[i];
					if (stfld.OpCode.Code != Code.Stfld)
						continue;
					var field = stfld.Operand as FieldReference;
					if (field == null)
						continue;
					if (!structFieldsToFix.find(field))
						continue;
					var instrs2 = toInstructionList(instrs);
					var instrPushes = DotNetUtils.getArgPushes(instrs2, i);
					if (instrPushes == null || instrPushes.Count != 2)
						continue;
					block.remove(i, 1);
					block.remove(instrs2.IndexOf(instrPushes[1]), 1);
					block.remove(instrs2.IndexOf(instrPushes[0]), 1);
					i -= 3;
				}
			}
		}

		static IList<Instruction> toInstructionList(IEnumerable<Instr> instrs) {
			var newInstrs = new List<Instruction>();
			foreach (var instr in instrs)
				newInstrs.Add(instr.Instruction);
			return newInstrs;
		}

		FieldDefinition getNewField(FieldReference structField, FieldReference oldFieldRef) {
			var fieldsDict = typeToFieldsDict.find(structField.DeclaringType);
			if (fieldsDict == null)
				throw new ApplicationException("Could not find structField declaringType");
			var newField = fieldsDict.find(oldFieldRef);
			if (newField == null)
				throw new ApplicationException("Could not find new field");
			return newField;
		}

		static int findLdStFieldIndex(IList<Instr> instrs, int index) {
			int stack = 0;
			for (int i = index; i < instrs.Count; i++) {
				var instr = instrs[i];

				if (stack == 0 && (instr.OpCode.Code == Code.Ldfld || instr.OpCode.Code == Code.Ldflda))
					return i;
				if (stack == 1 && instr.OpCode.Code == Code.Stfld)
					return i;

				int pushes, pops;
				DotNetUtils.calculateStackUsage(instr.Instruction, false, out pushes, out pops);
				stack -= pops;
				if (stack < 0)
					break;
				stack += pushes;
			}
			throw new ApplicationException("Could not find ldfld/stfld");
		}

		public void cleanUp() {
			foreach (var field in structFieldsToFix.getKeys())
				field.DeclaringType.Fields.Remove(field);
		}
	}
}
