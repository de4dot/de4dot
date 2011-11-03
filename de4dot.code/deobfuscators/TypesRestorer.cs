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
		List<MethodDefinition> allMethods;
		Dictionary<ParameterDefinition, TypeInfo<ParameterDefinition>> argInfos = new Dictionary<ParameterDefinition, TypeInfo<ParameterDefinition>>();
		TypeInfo<ParameterDefinition> methodReturnInfo;
		Dictionary<FieldReferenceAndDeclaringTypeKey, TypeInfo<FieldDefinition>> fieldWrites = new Dictionary<FieldReferenceAndDeclaringTypeKey, TypeInfo<FieldDefinition>>();
		Dictionary<int, UpdatedMethod> updatedMethods = new Dictionary<int, UpdatedMethod>();
		Dictionary<int, UpdatedField> updatedFields = new Dictionary<int, UpdatedField>();

		class UpdatedMethod {
			public int token;
			public TypeReference[] newArgTypes;
			public TypeReference newReturnType;

			public UpdatedMethod(MethodDefinition method) {
				token = method.MetadataToken.ToInt32();
				newArgTypes = new TypeReference[DotNetUtils.getArgsCount(method)];
			}
		}

		class UpdatedField {
			public int token;
			public TypeReference newFieldType;

			public UpdatedField(FieldDefinition field) {
				token = field.MetadataToken.ToInt32();
			}
		}

		class TypeInfo<T> {
			public Dictionary<TypeReferenceKey, bool> types = new Dictionary<TypeReferenceKey, bool>();
			public TypeReference newType = null;
			public T arg;

			public TypeInfo(T arg) {
				this.arg = arg;
			}

			public bool updateNewType() {
				if (types.Count == 0)
					return false;

				TypeReference theNewType = null;
				foreach (var key in types.Keys) {
					if (theNewType == null) {
						theNewType = key.TypeReference;
						continue;
					}
					theNewType = getCommonBaseClass(theNewType, key.TypeReference);
					if (theNewType == null)
						break;
				}
				if (theNewType == null)
					return false;
				if (MemberReferenceHelper.compareTypes(theNewType, newType))
					return false;

				newType = theNewType;
				return true;
			}
		}

		public TypesRestorer(ModuleDefinition module) {
			this.module = module;
		}

		UpdatedMethod getUpdatedMethod(MethodDefinition method) {
			int token = method.MetadataToken.ToInt32();
			UpdatedMethod updatedMethod;
			if (updatedMethods.TryGetValue(token, out updatedMethod))
				return updatedMethod;
			return updatedMethods[token] = new UpdatedMethod(method);
		}

		UpdatedField getUpdatedField(FieldDefinition field) {
			int token = field.MetadataToken.ToInt32();
			UpdatedField updatedField;
			if (updatedFields.TryGetValue(token, out updatedField))
				return updatedField;
			return updatedFields[token] = new UpdatedField(field);
		}

		public void deobfuscate() {
			allMethods = new List<MethodDefinition>();
			foreach (var type in module.GetTypes())
				allMethods.AddRange(type.Methods);

			foreach (var type in module.GetTypes()) {
				foreach (var field in type.Fields) {
					if (!MemberReferenceHelper.isSystemObject(field.FieldType))
						continue;

					var key = new FieldReferenceAndDeclaringTypeKey(field);
					fieldWrites[key] = new TypeInfo<FieldDefinition>(field);
				}
			}

			for (int i = 0; i < 10; i++) {
				bool changed = false;
				changed |= deobfuscateFields();
				changed |= deobfuscateMethods();
				if (!changed)
					break;
			}

			var fields = new List<UpdatedField>(updatedFields.Values);
			if (fields.Count > 0) {
				Log.v("Changing field types from object -> real type");
				fields.Sort((a, b) => {
					if (a.token < b.token) return -1;
					if (a.token > b.token) return 1;
					return 0;
				});
				Log.indent();
				foreach (var updatedField in fields)
					Log.v("Field {0:X8}: type {1} ({2:X8})", updatedField.token, updatedField.newFieldType.FullName, updatedField.newFieldType.MetadataToken.ToInt32());
				Log.deIndent();
			}

			var methods = new List<UpdatedMethod>(updatedMethods.Values);
			if (methods.Count > 0) {
				Log.v("Changing method args and return types from object -> real type");
				methods.Sort((a, b) => {
					if (a.token < b.token) return -1;
					if (a.token > b.token) return 1;
					return 0;
				});
				Log.indent();
				foreach (var updatedMethod in methods) {
					Log.v("Method {0:X8}", updatedMethod.token);
					Log.indent();
					if (updatedMethod.newReturnType != null)
						Log.v("ret: {0} ({1:X8})", updatedMethod.newReturnType.FullName, updatedMethod.newReturnType.MetadataToken.ToInt32());
					for (int i = 0; i < updatedMethod.newArgTypes.Length; i++) {
						var updatedArg = updatedMethod.newArgTypes[i];
						if (updatedArg == null)
							continue;
						Log.v("arg {0}: {1} ({2:X8})", i, updatedArg.FullName, updatedArg.MetadataToken.ToInt32());
					}
					Log.deIndent();
				}
				Log.deIndent();
			}
		}

		bool deobfuscateMethods() {
			bool changed = false;
			foreach (var method in allMethods) {
				methodReturnInfo = new TypeInfo<ParameterDefinition>(method.MethodReturnType.Parameter2);
				deobfuscateMethod(method);

				if (methodReturnInfo.updateNewType()) {
					getUpdatedMethod(method).newReturnType = methodReturnInfo.newType;
					method.MethodReturnType.ReturnType = methodReturnInfo.newType;
					changed = true;
				}

				foreach (var info in argInfos.Values) {
					if (info.updateNewType()) {
						getUpdatedMethod(method).newArgTypes[DotNetUtils.getArgIndex(method, info.arg)] = info.newType;
						info.arg.ParameterType = info.newType;
						changed = true;
					}
				}
			}
			return changed;
		}

		static int sortTypeInfos(TypeInfo<ParameterDefinition> a, TypeInfo<ParameterDefinition> b) {
			if (a.arg.Method.MetadataToken.ToInt32() < b.arg.Method.MetadataToken.ToInt32()) return -1;
			if (a.arg.Method.MetadataToken.ToInt32() > b.arg.Method.MetadataToken.ToInt32()) return 1;

			if (a.arg.Index < b.arg.Index) return -1;
			if (a.arg.Index < b.arg.Index) return 1;

			return 0;
		}

		void deobfuscateMethod(MethodDefinition method) {
			if (!method.IsStatic || method.Body == null)
				return;

			bool fixReturnType = MemberReferenceHelper.isSystemObject(method.MethodReturnType.ReturnType);

			argInfos.Clear();
			foreach (var arg in method.Parameters) {
				if (arg.ParameterType == null || arg.ParameterType.IsValueType)
					continue;
				if (!MemberReferenceHelper.isSystemObject(arg.ParameterType))
					continue;
				argInfos[arg] = new TypeInfo<ParameterDefinition>(arg);
			}
			if (argInfos.Count == 0 && !fixReturnType)
				return;

			var methodParams = DotNetUtils.getParameters(method);
			var reversedMethodParams = new List<ParameterDefinition>(methodParams);
			reversedMethodParams.Reverse();
			List<Instruction> args;
			var instructions = method.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instr = instructions[i];
				switch (instr.OpCode.Code) {
				case Code.Ret:
					if (!fixReturnType)
						break;
					var type = getLoadedType(method, instructions, i);
					if (type == null)
						break;
					methodReturnInfo.types[new TypeReferenceKey(type)] = true;
					break;

				case Code.Call:
				case Code.Calli:
				case Code.Callvirt:
					args = getPushedArgInstructions(instructions, i);
					var calledMethod = instr.Operand as MethodReference;
					if (calledMethod == null)
						break;
					var calledMethodParams = DotNetUtils.getParameters(calledMethod);
					for (int j = 0; j < args.Count; j++) {
						int calledMethodParamIndex = calledMethodParams.Count - j - 1;
						var ldInstr = args[j];
						switch (ldInstr.OpCode.Code) {
						case Code.Ldarg:
						case Code.Ldarg_S:
						case Code.Ldarg_0:
						case Code.Ldarg_1:
						case Code.Ldarg_2:
						case Code.Ldarg_3:
							addMethodArgType(getParameter(methodParams, method, ldInstr), DotNetUtils.getParameter(calledMethodParams, calledMethodParamIndex));
							break;

						default:
							break;
						}
					}
					break;

				case Code.Castclass:
					args = getPushedArgInstructions(instructions, i);
					if (args.Count < 1)
						break;
					addMethodArgType(getParameter(methodParams, method, args[0]), instr.Operand as TypeReference);
					break;

				case Code.Stloc:
				case Code.Stloc_S:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
					args = getPushedArgInstructions(instructions, i);
					if (args.Count < 1)
						break;
					addMethodArgType(getParameter(methodParams, method, args[0]), DotNetUtils.getLocalVar(method.Body.Variables, instr));
					break;

				case Code.Stsfld:
					args = getPushedArgInstructions(instructions, i);
					if (args.Count < 1)
						break;
					addMethodArgType(getParameter(methodParams, method, args[0]), instr.Operand as FieldReference);
					break;

				case Code.Stfld:
					args = getPushedArgInstructions(instructions, i);
					if (args.Count >= 1) {
						var field = instr.Operand as FieldReference;
						addMethodArgType(getParameter(methodParams, method, args[0]), field);
						if (args.Count >= 2 && field != null)
							addMethodArgType(getParameter(methodParams, method, args[1]), field.DeclaringType);
					}
					break;

				case Code.Ldfld:
				case Code.Ldflda:
					args = getPushedArgInstructions(instructions, i);
					if (args.Count < 1)
						break;
					addMethodArgType(getParameter(methodParams, method, args[0]), instr.Operand as FieldReference);
					break;

				//TODO: For better results, these should be checked:
				case Code.Starg:
				case Code.Starg_S:

				case Code.Ldelema:
				case Code.Ldelem_Any:
				case Code.Ldelem_I:
				case Code.Ldelem_I1:
				case Code.Ldelem_I2:
				case Code.Ldelem_I4:
				case Code.Ldelem_I8:
				case Code.Ldelem_R4:
				case Code.Ldelem_R8:
				case Code.Ldelem_Ref:
				case Code.Ldelem_U1:
				case Code.Ldelem_U2:
				case Code.Ldelem_U4:

				case Code.Ldind_I:
				case Code.Ldind_I1:
				case Code.Ldind_I2:
				case Code.Ldind_I4:
				case Code.Ldind_I8:
				case Code.Ldind_R4:
				case Code.Ldind_R8:
				case Code.Ldind_Ref:
				case Code.Ldind_U1:
				case Code.Ldind_U2:
				case Code.Ldind_U4:

				case Code.Ldobj:

				case Code.Stelem_Any:
				case Code.Stelem_I:
				case Code.Stelem_I1:
				case Code.Stelem_I2:
				case Code.Stelem_I4:
				case Code.Stelem_I8:
				case Code.Stelem_R4:
				case Code.Stelem_R8:
				case Code.Stelem_Ref:

				case Code.Stind_I:
				case Code.Stind_I1:
				case Code.Stind_I2:
				case Code.Stind_I4:
				case Code.Stind_I8:
				case Code.Stind_R4:
				case Code.Stind_R8:
				case Code.Stind_Ref:

				case Code.Stobj:
				default:
					break;
				}
			}
		}

		static ParameterDefinition getParameter(IList<ParameterDefinition> parameters, MethodReference method, Instruction instr) {
			switch (instr.OpCode.Code) {
			case Code.Ldarg:
			case Code.Ldarg_S:
			case Code.Ldarg_0:
			case Code.Ldarg_1:
			case Code.Ldarg_2:
			case Code.Ldarg_3:
				return DotNetUtils.getParameter(parameters, method, instr);

			default:
				return null;
			}
		}

		bool addMethodArgType(ParameterDefinition methodParam, FieldReference field) {
			if (field == null)
				return false;
			return addMethodArgType(methodParam, field.FieldType);
		}

		bool addMethodArgType(ParameterDefinition methodParam, VariableDefinition otherLocal) {
			if (otherLocal == null)
				return false;
			return addMethodArgType(methodParam, otherLocal.VariableType);
		}

		bool addMethodArgType(ParameterDefinition methodParam, ParameterDefinition otherParam) {
			if (otherParam == null)
				return false;
			return addMethodArgType(methodParam, otherParam.ParameterType);
		}

		bool addMethodArgType(ParameterDefinition methodParam, TypeReference type) {
			if (methodParam == null || type == null)
				return false;

			if (!isValidType(type))
				return false;

			TypeInfo<ParameterDefinition> info;
			if (!argInfos.TryGetValue(methodParam, out info))
				return false;
			var key = new TypeReferenceKey(type);
			if (info.types.ContainsKey(key))
				return false;

			info.types[key] = true;
			return true;
		}

		// May not return all args. The args are returned in reverse order.
		List<Instruction> getPushedArgInstructions(IList<Instruction> instructions, int index) {
			int pushes, pops;
			DotNetUtils.calculateStackUsage(instructions[index], false, out pushes, out pops);
			if (pops == -1)
				return new List<Instruction>();
			return getPushedArgInstructions(instructions, index, pops);
		}

		// May not return all args. The args are returned in reverse order.
		List<Instruction> getPushedArgInstructions(IList<Instruction> instructions, int index, int numArgs) {
			List<Instruction> args = new List<Instruction>(numArgs);

			int skipPushes = 0;
			while (index >= 0 && args.Count < numArgs) {
				var instr = getPreviousInstruction(instructions, ref index);
				if (instr == null)
					break;

				int pushes, pops;
				DotNetUtils.calculateStackUsage(instr, false, out pushes, out pops);
				if (pops == -1)
					break;
				if (pushes > 1)
					break;	// dup

				if (skipPushes > 0) {
					skipPushes -= pushes;
					if (skipPushes < 0)
						break;
					skipPushes += pops;
				}
				else {
					if (pushes == 1)
						args.Add(instr);
					skipPushes += pops;
				}
			}

			return args;
		}

		bool deobfuscateFields() {
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
					TypeInfo<FieldDefinition> info;
					if (!fieldWrites.TryGetValue(new FieldReferenceAndDeclaringTypeKey(field), out info))
						continue;

					var fieldType = getLoadedType(method, instructions, i);
					if (fieldType == null)
						continue;

					info.types[new TypeReferenceKey(fieldType)] = true;
				}
			}

			bool changed = false;
			var removeThese = new List<FieldDefinition>();
			foreach (var info in fieldWrites.Values) {
				if (info.updateNewType()) {
					removeThese.Add(info.arg);
					getUpdatedField(info.arg).newFieldType = info.newType;
					info.arg.FieldType = info.newType;
					changed = true;
				}
			}
			foreach (var field in removeThese)
				fieldWrites.Remove(new FieldReferenceAndDeclaringTypeKey(field));
			return changed;
		}

		TypeReference getLoadedType(MethodDefinition method, IList<Instruction> instructions, int instrIndex) {
			var prev = getPreviousInstruction(instructions, ref instrIndex);
			if (prev == null)
				return null;

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
					return null;
				fieldType = calledMethod.MethodReturnType.ReturnType;
				break;

			case Code.Newarr:
				fieldType = prev.Operand as TypeReference;
				if (fieldType == null)
					return null;
				fieldType = new ArrayType(fieldType);
				break;

			case Code.Newobj:
				var ctor = prev.Operand as MethodReference;
				if (ctor == null)
					return null;
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
					return null;
				fieldType = local.VariableType;
				break;

			case Code.Ldfld:
			case Code.Ldsfld:
				var field2 = prev.Operand as FieldReference;
				if (field2 == null)
					return null;
				fieldType = field2.FieldType;
				break;

			default:
				return null;
			}

			if (!isValidType(fieldType))
				return null;

			return fieldType;
		}

		static bool isValidType(TypeReference type) {
			if (type == null)
				return false;
			if (type.IsValueType)
				return false;
			if (MemberReferenceHelper.isSystemObject(type))
				return false;
			if (MemberReferenceHelper.verifyType(type, "mscorlib", "System.Void"))
				return false;
			if (type is GenericParameter)
				return false;
			return true;
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
				if (instr.OpCode.OpCodeType == OpCodeType.Prefix)
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
