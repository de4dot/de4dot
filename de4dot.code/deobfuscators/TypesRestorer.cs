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
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.deobfuscators {
	// Restore the type of all fields / parameters that have had their type turned into object.
	// This thing requires a lot more code than I have time to do now (similar to symbol renaming)
	// so it will be a basic implementation only.
	class TypesRestorer {
		ModuleDefinition module;
		Dictionary<FieldReferenceAndDeclaringTypeKey, FieldWriteInfo> fieldWrites = new Dictionary<FieldReferenceAndDeclaringTypeKey, FieldWriteInfo>();

		class FieldWriteInfo {
			public Dictionary<TypeReferenceKey, bool> types = new Dictionary<TypeReferenceKey, bool>();
			public FieldDefinition field;
			public TypeReference newFieldType = null;

			public FieldWriteInfo(FieldDefinition field) {
				this.field = field;
			}
		}

		public TypesRestorer(ModuleDefinition module) {
			this.module = module;
		}

		public void deobfuscate() {
			foreach (var type in module.GetTypes()) {
				foreach (var field in type.Fields) {
					if (!MemberReferenceHelper.isSystemObject(field.FieldType))
						continue;

					var key = new FieldReferenceAndDeclaringTypeKey(field);
					fieldWrites[key] = new FieldWriteInfo(field);
				}
			}

			var allMethods = new List<MethodDefinition>();
			foreach (var type in module.GetTypes())
				allMethods.AddRange(type.Methods);

			for (int i = 0; i < 10; i++) {
				if (!updateFields(allMethods))
					break;
			}

			var infos = new List<FieldWriteInfo>(fieldWrites.Values);
			infos.Sort((a, b) => {
				if (a.field.DeclaringType.MetadataToken.ToInt32() < b.field.DeclaringType.MetadataToken.ToInt32()) return -1;
				if (a.field.DeclaringType.MetadataToken.ToInt32() > b.field.DeclaringType.MetadataToken.ToInt32()) return 1;

				if (a.field.MetadataToken.ToInt32() < b.field.MetadataToken.ToInt32()) return -1;
				if (a.field.MetadataToken.ToInt32() > b.field.MetadataToken.ToInt32()) return 1;

				return 0;
			});

			Log.v("Changing field types from object -> real type");
			Log.indent();
			foreach (var info in infos) {
				if (info.newFieldType == null || MemberReferenceHelper.isSystemObject(info.newFieldType))
					continue;
				Log.v("{0:X8}: new type: {1} ({2:X8})", info.field.MetadataToken.ToInt32(), info.newFieldType, info.newFieldType.MetadataToken.ToInt32());
				info.field.FieldType = info.newFieldType;
			}
			Log.deIndent();
		}

		bool updateFields(IEnumerable<MethodDefinition> allMethods) {
			foreach (var info in fieldWrites.Values)
				info.types.Clear();

			foreach (var method in allMethods) {
				if (method.Body == null)
					continue;
				var instructions = method.Body.Instructions;
				for (int i = 0; i < instructions.Count; i++) {
					var instr = instructions[i];
					if (instr.OpCode.Code != Code.Stfld && instr.OpCode.Code != Code.Stsfld)
						continue;

					var field = instr.Operand as FieldReference;
					FieldWriteInfo info;
					if (!fieldWrites.TryGetValue(new FieldReferenceAndDeclaringTypeKey(field), out info))
						continue;

					int instrIndex = i;
					var prev = getPreviousInstruction(instructions, ref instrIndex);
					if (prev == null)
						continue;

					TypeReference fieldType;
					switch (prev.OpCode.Code) {
					case Code.Ldstr:
						fieldType = module.TypeSystem.String;
						break;

					case Code.Call:
					case Code.Calli:
					case Code.Callvirt:
						var calledMethod = prev.Operand as MethodReference;
						if (calledMethod == null)
							continue;
						fieldType = calledMethod.MethodReturnType.ReturnType;
						break;

					case Code.Newarr:
						fieldType = prev.Operand as TypeReference;
						if (fieldType == null)
							continue;
						fieldType = new ArrayType(fieldType);
						break;

					case Code.Newobj:
						var ctor = prev.Operand as MethodReference;
						if (ctor == null)
							continue;
						fieldType = ctor.DeclaringType;
						break;

					case Code.Castclass:
					case Code.Isinst:
						fieldType = prev.Operand as TypeReference;
						break;

					case Code.Ldarg:
					case Code.Ldarg_S:
					case Code.Ldarg_0:
					case Code.Ldarg_1:
					case Code.Ldarg_2:
					case Code.Ldarg_3:
						fieldType = DotNetUtils.getArgType(method, prev);
						break;

					case Code.Ldloc:
					case Code.Ldloc_S:
					case Code.Ldloc_0:
					case Code.Ldloc_1:
					case Code.Ldloc_2:
					case Code.Ldloc_3:
						var local = DotNetUtils.getLocalVar(method.Body.Variables, prev);
						if (local == null)
							continue;
						fieldType = local.VariableType;
						break;

					case Code.Ldfld:
					case Code.Ldsfld:
						var field2 = prev.Operand as FieldReference;
						if (field2 == null)
							continue;
						fieldType = field2.FieldType;
						break;

					default:
						continue;
					}

					if (fieldType == null)
						continue;
					if (fieldType.IsValueType)
						continue;
					if (MemberReferenceHelper.isSystemObject(fieldType))
						continue;
					if (MemberReferenceHelper.verifyType(fieldType, "mscorlib", "System.Void"))
						continue;
					if (fieldType is GenericParameter)
						continue;

					info.types[new TypeReferenceKey(fieldType)] = true;
				}
			}

			bool changed = false;
			foreach (var info in fieldWrites.Values) {
				if (info.types.Count == 0)
					continue;

				TypeReference newType = null;
				foreach (var key in info.types.Keys) {
					if (newType == null) {
						newType = key.TypeReference;
						continue;
					}
					newType = getCommonBaseClass(newType, key.TypeReference);
					if (newType == null)
						break;
				}
				if (newType == null)
					continue;
				if (MemberReferenceHelper.compareTypes(newType, info.newFieldType))
					continue;

				info.newFieldType = newType;
				changed = true;
			}

			return changed;
		}

		static TypeReference getCommonBaseClass(TypeReference a, TypeReference b) {
			return null;	//TODO:
		}

		static Instruction getPreviousInstruction(IList<Instruction> instructions, ref int instrIndex) {
			while (true) {
				instrIndex--;
				if (instrIndex < 0)
					return null;
				var instr = instructions[instrIndex];
				if (instr.OpCode.Code == Code.Nop)
					continue;
				switch (instr.OpCode.FlowControl) {
				case FlowControl.Next:
				case FlowControl.Call:
					return instr;
				default:
					return null;
				}
			}
		}
	}
}
