/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators {
	// Restore the type of all fields / parameters that have had their type turned into object.
	// This thing requires a lot more code than I have time to do now (similar to symbol renaming)
	// so it will be a basic implementation only.
	abstract class TypesRestorerBase {
		ModuleDef module;
		List<MethodDef> allMethods;
		Dictionary<Parameter, TypeInfo<Parameter>> argInfos = new Dictionary<Parameter, TypeInfo<Parameter>>();
		TypeInfo<Parameter> methodReturnInfo;
		Dictionary<IField, TypeInfo<FieldDef>> fieldWrites = new Dictionary<IField, TypeInfo<FieldDef>>(FieldEqualityComparer.CompareDeclaringTypes);
		Dictionary<int, UpdatedMethod> updatedMethods = new Dictionary<int, UpdatedMethod>();
		Dictionary<int, UpdatedField> updatedFields = new Dictionary<int, UpdatedField>();

		class UpdatedMethod {
			public int token;
			public TypeSig[] newArgTypes;
			public TypeSig newReturnType;

			public UpdatedMethod(MethodDef method) {
				token = method.MDToken.ToInt32();
				newArgTypes = new TypeSig[DotNetUtils.getArgsCount(method)];
			}
		}

		class UpdatedField {
			public int token;
			public TypeSig newFieldType;

			public UpdatedField(FieldDef field) {
				token = field.MDToken.ToInt32();
			}
		}

		class TypeInfo<T> {
			Dictionary<TypeSig, bool> types = new Dictionary<TypeSig, bool>(TypeEqualityComparer.Instance);
			public TypeSig newType = null;
			public T arg;
			bool newobjTypes;

			public Dictionary<TypeSig, bool> Types {
				get { return types; }
			}

			public TypeInfo(T arg) {
				this.arg = arg;
			}

			public void add(TypeSig type) {
				add(type, false);
			}

			public void add(TypeSig type, bool wasNewobj) {
				if (wasNewobj) {
					if (!newobjTypes)
						clear();
					newobjTypes = true;
				}
				else if (newobjTypes)
					return;
				types[type] = true;
			}

			public void clear() {
				types.Clear();
			}

			public bool updateNewType(ModuleDef module) {
				if (types.Count == 0)
					return false;

				TypeSig theNewType = null;
				foreach (var key in types.Keys) {
					if (theNewType == null) {
						theNewType = key;
						continue;
					}
					theNewType = getCommonBaseClass(module, theNewType, key);
					if (theNewType == null)
						break;
				}
				if (theNewType == null)
					return false;
				if (new SigComparer().Equals(theNewType, newType))
					return false;

				newType = theNewType;
				return true;
			}
		}

		public TypesRestorerBase(ModuleDef module) {
			this.module = module;
		}

		UpdatedMethod getUpdatedMethod(MethodDef method) {
			int token = method.MDToken.ToInt32();
			UpdatedMethod updatedMethod;
			if (updatedMethods.TryGetValue(token, out updatedMethod))
				return updatedMethod;
			return updatedMethods[token] = new UpdatedMethod(method);
		}

		UpdatedField getUpdatedField(FieldDef field) {
			int token = field.MDToken.ToInt32();
			UpdatedField updatedField;
			if (updatedFields.TryGetValue(token, out updatedField))
				return updatedField;
			return updatedFields[token] = new UpdatedField(field);
		}

		public void deobfuscate() {
			allMethods = new List<MethodDef>();

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

		void addMethods(IEnumerable<MethodDef> methods) {
			allMethods.AddRange(methods);
		}

		void addMethod(MethodDef method) {
			allMethods.Add(method);
		}

		void addAllFields() {
			foreach (var type in module.GetTypes()) {
				foreach (var field in type.Fields) {
					if (!isUnknownType(field))
						continue;

					fieldWrites[field] = new TypeInfo<FieldDef>(field);
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

			Logger.v("Changing field types to real type");
			fields.Sort((a, b) => a.token.CompareTo(b.token));
			Logger.Instance.indent();
			foreach (var updatedField in fields)
				Logger.v("Field {0:X8}: type {1} ({2:X8})", updatedField.token, Utils.removeNewlines(updatedField.newFieldType.FullName), updatedField.newFieldType.MDToken.ToInt32());
			Logger.Instance.deIndent();
		}

		void restoreMethodTypes() {
			var methods = new List<UpdatedMethod>(updatedMethods.Values);
			if (methods.Count == 0)
				return;

			Logger.v("Changing method args and return types to real type");
			methods.Sort((a, b) => a.token.CompareTo(b.token));
			Logger.Instance.indent();
			foreach (var updatedMethod in methods) {
				Logger.v("Method {0:X8}", updatedMethod.token);
				Logger.Instance.indent();
				if (updatedMethod.newReturnType != null) {
					Logger.v("ret: {0} ({1:X8})",
							Utils.removeNewlines(updatedMethod.newReturnType.FullName),
							updatedMethod.newReturnType.MDToken.ToInt32());
				}
				for (int i = 0; i < updatedMethod.newArgTypes.Length; i++) {
					var updatedArg = updatedMethod.newArgTypes[i];
					if (updatedArg == null)
						continue;
					Logger.v("arg {0}: {1} ({2:X8})",
							i,
							Utils.removeNewlines(updatedArg.FullName),
							updatedArg.MDToken.ToInt32());
				}
				Logger.Instance.deIndent();
			}
			Logger.Instance.deIndent();
		}

		bool deobfuscateMethods() {
			bool changed = false;
			foreach (var method in allMethods) {
				methodReturnInfo = new TypeInfo<Parameter>(method.Parameters.ReturnParameter);
				deobfuscateMethod(method);

				if (methodReturnInfo.updateNewType(module)) {
					getUpdatedMethod(method).newReturnType = methodReturnInfo.newType;
					method.MethodSig.RetType = methodReturnInfo.newType;
					changed = true;
				}

				foreach (var info in argInfos.Values) {
					if (info.updateNewType(module)) {
						getUpdatedMethod(method).newArgTypes[info.arg.Index] = info.newType;
						info.arg.Type = info.newType;
						changed = true;
					}
				}
			}
			return changed;
		}

		static int sortTypeInfos(TypeInfo<Parameter> a, TypeInfo<Parameter> b) {
			if (a.arg.Method.MDToken.ToInt32() < b.arg.Method.MDToken.ToInt32()) return -1;
			if (a.arg.Method.MDToken.ToInt32() > b.arg.Method.MDToken.ToInt32()) return 1;

			return a.arg.Index.CompareTo(b.arg.Index);
		}

		void deobfuscateMethod(MethodDef method) {
			if (!method.IsStatic || method.Body == null)
				return;

			bool fixReturnType = isUnknownType(method.MethodSig.GetRetType());

			argInfos.Clear();
			foreach (var arg in method.Parameters) {
				if (arg.IsHiddenThisParameter)
					continue;
				if (!isUnknownType(arg))
					continue;
				argInfos[arg] = new TypeInfo<Parameter>(arg);
			}
			if (argInfos.Count == 0 && !fixReturnType)
				return;

			var methodParams = method.Parameters;
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
					var calledMethod = instr.Operand as IMethod;
					if (calledMethod == null)
						break;
					var calledMethodParams = DotNetUtils.getArgs(calledMethod);
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
							addMethodArgType(method, getParameter(methodParams, ldInstr), DotNetUtils.getArg(calledMethodParams, calledMethodParamIndex));
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
					addMethodArgType(method, getParameter(methodParams, pushedArgs.getEnd(0)), instr.Operand as ITypeDefOrRef);
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
					addMethodArgType(method, getParameter(methodParams, pushedArgs.getEnd(0)), instr.GetLocal(method.Body.Variables));
					break;

				case Code.Stsfld:
					pushedArgs = MethodStack.getPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs < 1)
						break;
					addMethodArgType(method, getParameter(methodParams, pushedArgs.getEnd(0)), instr.Operand as IField);
					break;

				case Code.Stfld:
					pushedArgs = MethodStack.getPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs >= 1) {
						var field = instr.Operand as IField;
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
					addMethodArgType(method, getParameter(methodParams, pushedArgs.getEnd(0)), instr.Operand as IField);
					break;

				//TODO: For better results, these should be checked:
				case Code.Starg:
				case Code.Starg_S:

				case Code.Ldelema:
				case Code.Ldelem:
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

				case Code.Stelem:
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

		static Parameter getParameter(IList<Parameter> parameters, Instruction instr) {
			switch (instr.OpCode.Code) {
			case Code.Ldarg:
			case Code.Ldarg_S:
			case Code.Ldarg_0:
			case Code.Ldarg_1:
			case Code.Ldarg_2:
			case Code.Ldarg_3:
				return instr.GetParameter(parameters);

			default:
				return null;
			}
		}

		bool addMethodArgType(IGenericParameterProvider gpp, Parameter methodParam, IField field) {
			if (field == null)
				return false;
			return addMethodArgType(gpp, methodParam, field.FieldSig.GetFieldType());
		}

		bool addMethodArgType(IGenericParameterProvider gpp, Parameter methodParam, Local otherLocal) {
			if (otherLocal == null)
				return false;
			return addMethodArgType(gpp, methodParam, otherLocal.Type);
		}

		bool addMethodArgType(IGenericParameterProvider gpp, Parameter methodParam, Parameter otherParam) {
			if (otherParam == null)
				return false;
			return addMethodArgType(gpp, methodParam, otherParam.Type);
		}

		bool addMethodArgType(IGenericParameterProvider gpp, Parameter methodParam, ITypeDefOrRef type) {
			return addMethodArgType(gpp, methodParam, type.ToTypeSig());
		}

		bool addMethodArgType(IGenericParameterProvider gpp, Parameter methodParam, TypeSig type) {
			if (methodParam == null || type == null)
				return false;

			if (!isValidType(gpp, type))
				return false;

			TypeInfo<Parameter> info;
			if (!argInfos.TryGetValue(methodParam, out info))
				return false;
			if (info.Types.ContainsKey(type))
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
					TypeSig fieldType = null;
					TypeInfo<FieldDef> info = null;
					IField field;
					switch (instr.OpCode.Code) {
					case Code.Stfld:
					case Code.Stsfld:
						field = instr.Operand as IField;
						if (field == null)
							continue;
						if (!fieldWrites.TryGetValue(field, out info))
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
						var calledMethod = instr.Operand as IMethod;
						if (calledMethod == null)
							continue;
						var calledMethodDefOrRef = calledMethod as IMethodDefOrRef;
						var calledMethodSpec = calledMethod as MethodSpec;
						if (calledMethodSpec != null)
							calledMethodDefOrRef = calledMethodSpec.Method;
						if (calledMethodDefOrRef == null)
							continue;

						IList<TypeSig> calledMethodArgs = DotNetUtils.getArgs(calledMethodDefOrRef);
						calledMethodArgs = DotNetUtils.replaceGenericParameters(calledMethodDefOrRef.DeclaringType.TryGetGenericInstSig(), calledMethodSpec, calledMethodArgs);
						for (int j = 0; j < pushedArgs.NumValidArgs; j++) {
							var pushInstr = pushedArgs.getEnd(j);
							if (pushInstr.OpCode.Code != Code.Ldfld && pushInstr.OpCode.Code != Code.Ldsfld)
								continue;

							field = pushInstr.Operand as IField;
							if (field == null)
								continue;
							if (!fieldWrites.TryGetValue(field, out info))
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
			var removeThese = new List<FieldDef>();
			foreach (var info in fieldWrites.Values) {
				if (info.updateNewType(module)) {
					removeThese.Add(info.arg);
					getUpdatedField(info.arg).newFieldType = info.newType;
					info.arg.FieldSig.Type = info.newType;
					changed = true;
				}
			}
			foreach (var field in removeThese)
				fieldWrites.Remove(field);
			return changed;
		}

		TypeSig getLoadedType(IGenericParameterProvider gpp, MethodDef method, IList<Instruction> instructions, int instrIndex, out bool wasNewobj) {
			var fieldType = MethodStack.getLoadedType(method, instructions, instrIndex, out wasNewobj);
			if (fieldType == null || !isValidType(gpp, fieldType))
				return null;
			return fieldType;
		}

		protected virtual bool isValidType(IGenericParameterProvider gpp, TypeSig type) {
			if (type == null)
				return false;
			if (type.ElementType == ElementType.Void)
				return false;

			while (type != null) {
				switch (type.ElementType) {
				case ElementType.GenericInst:
					foreach (var ga in ((GenericInstSig)type).GenericArguments) {
						if (!isValidType(gpp, ga))
							return false;
					}
					break;

				case ElementType.SZArray:
				case ElementType.Array:
				case ElementType.Ptr:
				case ElementType.Class:
				case ElementType.ValueType:
				case ElementType.Void:
				case ElementType.Boolean:
				case ElementType.Char:
				case ElementType.I1:
				case ElementType.U1:
				case ElementType.I2:
				case ElementType.U2:
				case ElementType.I4:
				case ElementType.U4:
				case ElementType.I8:
				case ElementType.U8:
				case ElementType.R4:
				case ElementType.R8:
				case ElementType.TypedByRef:
				case ElementType.I:
				case ElementType.U:
				case ElementType.String:
				case ElementType.Object:
					break;

				case ElementType.Var:
				case ElementType.MVar:
					// TODO: Return false for now. We don't know whether the Var is a Var in
					// this type or from some other type.
					return false;

				case ElementType.ByRef:
				case ElementType.FnPtr:
				case ElementType.CModOpt:
				case ElementType.CModReqd:
				case ElementType.Pinned:
				case ElementType.Sentinel:
				case ElementType.ValueArray:
				case ElementType.R:
				case ElementType.End:
				case ElementType.Internal:
				case ElementType.Module:
				default:
					return false;
				}

				if (type.Next == null)
					break;
				type = type.Next;
			}

			return type != null;
		}

		protected abstract bool isUnknownType(object o);

		static TypeSig getCommonBaseClass(ModuleDef module, TypeSig a, TypeSig b) {
			if (DotNetUtils.isDelegate(a) && DotNetUtils.derivesFromDelegate(module.Find(b.ToTypeDefOrRef())))
				return b;
			if (DotNetUtils.isDelegate(b) && DotNetUtils.derivesFromDelegate(module.Find(a.ToTypeDefOrRef())))
				return a;
			return null;	//TODO:
		}
	}

	class TypesRestorer : TypesRestorerBase {
		public TypesRestorer(ModuleDef module)
			: base(module) {
		}

		protected override bool isValidType(IGenericParameterProvider gpp, TypeSig type) {
			if (type == null)
				return false;
			if (type.IsValueType)
				return false;
			if (type.ElementType == ElementType.Object)
				return false;
			return base.isValidType(gpp, type);
		}

		protected override bool isUnknownType(object o) {
			var arg = o as Parameter;
			if (arg != null)
				return arg.Type.GetElementType() == ElementType.Object;

			var field = o as FieldDef;
			if (field != null)
				return field.FieldSig.GetFieldType().GetElementType() == ElementType.Object;

			var sig = o as TypeSig;
			if (sig != null)
				return sig.ElementType == ElementType.Object;

			throw new ApplicationException(string.Format("Unknown type: {0}", o.GetType()));
		}
	}
}
