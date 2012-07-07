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

namespace de4dot.code.deobfuscators {
	// Restore the type of all fields / parameters that have had their type turned into object.
	// This thing requires a lot more code than I have time to do now (similar to symbol renaming)
	// so it will be a basic implementation only.
	abstract class TypesRestorerBase {
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
			Dictionary<TypeReferenceKey, bool> types = new Dictionary<TypeReferenceKey, bool>();
			public TypeReference newType = null;
			public T arg;
			bool newobjTypes;

			public Dictionary<TypeReferenceKey, bool> Types {
				get { return types; }
			}

			public TypeInfo(T arg) {
				this.arg = arg;
			}

			public void add(TypeReference type) {
				add(type, false);
			}

			public void add(TypeReference type, bool wasNewobj) {
				if (wasNewobj) {
					if (!newobjTypes)
						clear();
					newobjTypes = true;
				}
				else if (newobjTypes)
					return;
				types[new TypeReferenceKey(type)] = true;
			}

			public void clear() {
				types.Clear();
			}

			public bool updateNewType(ModuleDefinition module) {
				if (types.Count == 0)
					return false;

				TypeReference theNewType = null;
				foreach (var key in types.Keys) {
					if (theNewType == null) {
						theNewType = key.TypeReference;
						continue;
					}
					theNewType = getCommonBaseClass(module, theNewType, key.TypeReference);
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

		public TypesRestorerBase(ModuleDefinition module) {
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

			addAllMethods();
			addAllFields();

			deobfuscateLoop();

			restoreFieldTypes();
			restoreMethodTypes();
		}

		void addAllMethods() {
			foreach (var type in module.GetTypes())
				addMethods(type.Methods);
		}

		void addMethods(IEnumerable<MethodDefinition> methods) {
			allMethods.AddRange(methods);
		}

		void addMethod(MethodDefinition method) {
			allMethods.Add(method);
		}

		void addAllFields() {
			foreach (var type in module.GetTypes()) {
				foreach (var field in type.Fields) {
					if (!isUnknownType(field))
						continue;

					var key = new FieldReferenceAndDeclaringTypeKey(field);
					fieldWrites[key] = new TypeInfo<FieldDefinition>(field);
				}
			}
		}

		void deobfuscateLoop() {
			for (int i = 0; i < 10; i++) {
				bool changed = false;
				changed |= deobfuscateFields();
				changed |= deobfuscateMethods();
				if (!changed)
					break;
			}
		}

		void restoreFieldTypes() {
			var fields = new List<UpdatedField>(updatedFields.Values);
			if (fields.Count == 0)
				return;

			Log.v("Changing field types to real type");
			fields.Sort((a, b) => Utils.compareInt32(a.token, b.token));
			Log.indent();
			foreach (var updatedField in fields)
				Log.v("Field {0:X8}: type {1} ({2:X8})", updatedField.token, Utils.removeNewlines(updatedField.newFieldType.FullName), updatedField.newFieldType.MetadataToken.ToInt32());
			Log.deIndent();
		}

		void restoreMethodTypes() {
			var methods = new List<UpdatedMethod>(updatedMethods.Values);
			if (methods.Count == 0)
				return;

			Log.v("Changing method args and return types to real type");
			methods.Sort((a, b) => Utils.compareInt32(a.token, b.token));
			Log.indent();
			foreach (var updatedMethod in methods) {
				Log.v("Method {0:X8}", updatedMethod.token);
				Log.indent();
				if (updatedMethod.newReturnType != null) {
					Log.v("ret: {0} ({1:X8})",
							Utils.removeNewlines(updatedMethod.newReturnType.FullName),
							updatedMethod.newReturnType.MetadataToken.ToInt32());
				}
				for (int i = 0; i < updatedMethod.newArgTypes.Length; i++) {
					var updatedArg = updatedMethod.newArgTypes[i];
					if (updatedArg == null)
						continue;
					Log.v("arg {0}: {1} ({2:X8})",
							i,
							Utils.removeNewlines(updatedArg.FullName),
							updatedArg.MetadataToken.ToInt32());
				}
				Log.deIndent();
			}
			Log.deIndent();
		}

		bool deobfuscateMethods() {
			bool changed = false;
			foreach (var method in allMethods) {
				methodReturnInfo = new TypeInfo<ParameterDefinition>(method.MethodReturnType.Parameter2);
				deobfuscateMethod(method);

				if (methodReturnInfo.updateNewType(module)) {
					getUpdatedMethod(method).newReturnType = methodReturnInfo.newType;
					method.MethodReturnType.ReturnType = methodReturnInfo.newType;
					changed = true;
				}

				foreach (var info in argInfos.Values) {
					if (info.updateNewType(module)) {
						getUpdatedMethod(method).newArgTypes[DotNetUtils.getArgIndex(info.arg)] = info.newType;
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

			return Utils.compareInt32(a.arg.Sequence, b.arg.Sequence);
		}

		void deobfuscateMethod(MethodDefinition method) {
			if (!method.IsStatic || method.Body == null)
				return;

			bool fixReturnType = isUnknownType(method.MethodReturnType);

			argInfos.Clear();
			foreach (var arg in method.Parameters) {
				if (!isUnknownType(arg))
					continue;
				argInfos[arg] = new TypeInfo<ParameterDefinition>(arg);
			}
			if (argInfos.Count == 0 && !fixReturnType)
				return;

			var methodParams = DotNetUtils.getParameters(method);
			PushedArgs pushedArgs;
			var instructions = method.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instr = instructions[i];
				switch (instr.OpCode.Code) {
				case Code.Ret:
					if (!fixReturnType)
						break;
					bool wasNewobj;
					var type = getLoadedType(method, method, instructions, i, out wasNewobj);
					if (type == null)
						break;
					methodReturnInfo.add(type);
					break;

				case Code.Call:
				case Code.Calli:
				case Code.Callvirt:
				case Code.Newobj:
					pushedArgs = MethodStack.getPushedArgInstructions(instructions, i);
					var calledMethod = instr.Operand as MethodReference;
					if (calledMethod == null)
						break;
					var calledMethodParams = DotNetUtils.getParameters(calledMethod);
					for (int j = 0; j < pushedArgs.NumValidArgs; j++) {
						int calledMethodParamIndex = calledMethodParams.Count - j - 1;
						var ldInstr = pushedArgs.getEnd(j);
						switch (ldInstr.OpCode.Code) {
						case Code.Ldarg:
						case Code.Ldarg_S:
						case Code.Ldarg_0:
						case Code.Ldarg_1:
						case Code.Ldarg_2:
						case Code.Ldarg_3:
							addMethodArgType(method, getParameter(methodParams, ldInstr), DotNetUtils.getParameter(calledMethodParams, calledMethodParamIndex));
							break;

						default:
							break;
						}
					}
					break;

				case Code.Castclass:
					pushedArgs = MethodStack.getPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs < 1)
						break;
					addMethodArgType(method, getParameter(methodParams, pushedArgs.getEnd(0)), instr.Operand as TypeReference);
					break;

				case Code.Stloc:
				case Code.Stloc_S:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
					pushedArgs = MethodStack.getPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs < 1)
						break;
					addMethodArgType(method, getParameter(methodParams, pushedArgs.getEnd(0)), DotNetUtils.getLocalVar(method.Body.Variables, instr));
					break;

				case Code.Stsfld:
					pushedArgs = MethodStack.getPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs < 1)
						break;
					addMethodArgType(method, getParameter(methodParams, pushedArgs.getEnd(0)), instr.Operand as FieldReference);
					break;

				case Code.Stfld:
					pushedArgs = MethodStack.getPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs >= 1) {
						var field = instr.Operand as FieldReference;
						addMethodArgType(method, getParameter(methodParams, pushedArgs.getEnd(0)), field);
						if (pushedArgs.NumValidArgs >= 2 && field != null)
							addMethodArgType(method, getParameter(methodParams, pushedArgs.getEnd(1)), field.DeclaringType);
					}
					break;

				case Code.Ldfld:
				case Code.Ldflda:
					pushedArgs = MethodStack.getPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs < 1)
						break;
					addMethodArgType(method, getParameter(methodParams, pushedArgs.getEnd(0)), instr.Operand as FieldReference);
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

		static ParameterDefinition getParameter(IList<ParameterDefinition> parameters, Instruction instr) {
			switch (instr.OpCode.Code) {
			case Code.Ldarg:
			case Code.Ldarg_S:
			case Code.Ldarg_0:
			case Code.Ldarg_1:
			case Code.Ldarg_2:
			case Code.Ldarg_3:
				return DotNetUtils.getParameter(parameters, instr);

			default:
				return null;
			}
		}

		bool addMethodArgType(IGenericParameterProvider gpp, ParameterDefinition methodParam, FieldReference field) {
			if (field == null)
				return false;
			return addMethodArgType(gpp, methodParam, field.FieldType);
		}

		bool addMethodArgType(IGenericParameterProvider gpp, ParameterDefinition methodParam, VariableDefinition otherLocal) {
			if (otherLocal == null)
				return false;
			return addMethodArgType(gpp, methodParam, otherLocal.VariableType);
		}

		bool addMethodArgType(IGenericParameterProvider gpp, ParameterDefinition methodParam, ParameterDefinition otherParam) {
			if (otherParam == null)
				return false;
			return addMethodArgType(gpp, methodParam, otherParam.ParameterType);
		}

		bool addMethodArgType(IGenericParameterProvider gpp, ParameterDefinition methodParam, TypeReference type) {
			if (methodParam == null || type == null)
				return false;

			if (!isValidType(gpp, type))
				return false;

			TypeInfo<ParameterDefinition> info;
			if (!argInfos.TryGetValue(methodParam, out info))
				return false;
			if (info.Types.ContainsKey(new TypeReferenceKey(type)))
				return false;

			info.add(type);
			return true;
		}

		bool deobfuscateFields() {
			foreach (var info in fieldWrites.Values)
				info.clear();

			foreach (var method in allMethods) {
				if (method.Body == null)
					continue;
				var instructions = method.Body.Instructions;
				for (int i = 0; i < instructions.Count; i++) {
					var instr = instructions[i];
					TypeReference fieldType = null;
					TypeInfo<FieldDefinition> info = null;
					FieldReference field;
					switch (instr.OpCode.Code) {
					case Code.Stfld:
					case Code.Stsfld:
						field = instr.Operand as FieldReference;
						if (field == null)
							continue;
						if (!fieldWrites.TryGetValue(new FieldReferenceAndDeclaringTypeKey(field), out info))
							continue;
						bool wasNewobj;
						fieldType = getLoadedType(info.arg.DeclaringType, method, instructions, i, out wasNewobj);
						if (fieldType == null)
							continue;
						info.add(fieldType, wasNewobj);
						break;

					case Code.Call:
					case Code.Calli:
					case Code.Callvirt:
					case Code.Newobj:
						var pushedArgs = MethodStack.getPushedArgInstructions(instructions, i);
						var calledMethod = instr.Operand as MethodReference;
						if (calledMethod == null)
							continue;
						IList<TypeReference> calledMethodArgs = DotNetUtils.getArgs(calledMethod);
						calledMethodArgs = DotNetUtils.replaceGenericParameters(calledMethod.DeclaringType as GenericInstanceType, calledMethod as GenericInstanceMethod, calledMethodArgs);
						for (int j = 0; j < pushedArgs.NumValidArgs; j++) {
							var pushInstr = pushedArgs.getEnd(j);
							if (pushInstr.OpCode.Code != Code.Ldfld && pushInstr.OpCode.Code != Code.Ldsfld)
								continue;

							field = pushInstr.Operand as FieldReference;
							if (field == null)
								continue;
							if (!fieldWrites.TryGetValue(new FieldReferenceAndDeclaringTypeKey(field), out info))
								continue;
							fieldType = calledMethodArgs[calledMethodArgs.Count - 1 - j];
							if (!isValidType(info.arg.DeclaringType, fieldType))
								continue;
							info.add(fieldType);
						}
						break;

					default:
						continue;
					}
				}
			}

			bool changed = false;
			var removeThese = new List<FieldDefinition>();
			foreach (var info in fieldWrites.Values) {
				if (info.updateNewType(module)) {
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

		TypeReference getLoadedType(IGenericParameterProvider gpp, MethodDefinition method, IList<Instruction> instructions, int instrIndex, out bool wasNewobj) {
			var fieldType = MethodStack.getLoadedType(method, instructions, instrIndex, out wasNewobj);
			if (fieldType == null || !isValidType(gpp, fieldType))
				return null;
			return fieldType;
		}

		protected virtual bool isValidType(IGenericParameterProvider gpp, TypeReference type) {
			if (type == null)
				return false;
			if (type.EType == ElementType.Void)
				return false;

			while (type != null) {
				switch (MemberReferenceHelper.getMemberReferenceType(type)) {
				case CecilType.ArrayType:
				case CecilType.GenericInstanceType:
				case CecilType.PointerType:
				case CecilType.TypeDefinition:
				case CecilType.TypeReference:
					break;

				case CecilType.GenericParameter:
					var gp = (GenericParameter)type;
					var methodRef = gpp as MethodReference;
					var typeRef = gpp as TypeReference;
					if (methodRef != null) {
						if (methodRef.DeclaringType != gp.Owner && methodRef != gp.Owner)
							return false;
					}
					else if (typeRef != null) {
						if (typeRef != gp.Owner)
							return false;
					}
					else
						return false;
					break;

				case CecilType.ByReferenceType:
				case CecilType.FunctionPointerType:
				case CecilType.OptionalModifierType:
				case CecilType.PinnedType:
				case CecilType.RequiredModifierType:
				case CecilType.SentinelType:
				default:
					return false;
				}

				if (!(type is TypeSpecification))
					break;
				type = ((TypeSpecification)type).ElementType;
			}

			return type != null;
		}

		protected abstract bool isUnknownType(object o);

		static TypeReference getCommonBaseClass(ModuleDefinition module, TypeReference a, TypeReference b) {
			if (DotNetUtils.isDelegate(a) && DotNetUtils.derivesFromDelegate(DotNetUtils.getType(module, b)))
				return b;
			if (DotNetUtils.isDelegate(b) && DotNetUtils.derivesFromDelegate(DotNetUtils.getType(module, a)))
				return a;
			return null;	//TODO:
		}
	}

	class TypesRestorer : TypesRestorerBase {
		public TypesRestorer(ModuleDefinition module)
			: base(module) {
		}

		protected override bool isValidType(IGenericParameterProvider gpp, TypeReference type) {
			if (type == null)
				return false;
			if (type.IsValueType)
				return false;
			if (MemberReferenceHelper.isSystemObject(type))
				return false;
			return base.isValidType(gpp, type);
		}

		protected override bool isUnknownType(object o) {
			var arg = o as ParameterDefinition;
			if (arg != null)
				return MemberReferenceHelper.isSystemObject(arg.ParameterType);

			var field = o as FieldDefinition;
			if (field != null)
				return MemberReferenceHelper.isSystemObject(field.FieldType);

			var retType = o as MethodReturnType;
			if (retType != null)
				return MemberReferenceHelper.isSystemObject(retType.ReturnType);

			throw new ApplicationException(string.Format("Unknown type: {0}", o.GetType()));
		}
	}
}
