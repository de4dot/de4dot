/*
    Copyright (C) 2011-2015 de4dot@gmail.com

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
	public abstract class TypesRestorerBase {
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
				newArgTypes = new TypeSig[DotNetUtils.GetArgsCount(method)];
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

			public void Add(TypeSig type) {
				Add(type, false);
			}

			public void Add(TypeSig type, bool wasNewobj) {
				if (wasNewobj) {
					if (!newobjTypes)
						Clear();
					newobjTypes = true;
				}
				else if (newobjTypes)
					return;
				types[type] = true;
			}

			public void Clear() {
				types.Clear();
			}

			public bool UpdateNewType(ModuleDef module) {
				if (types.Count == 0)
					return false;

				TypeSig theNewType = null;
				foreach (var key in types.Keys) {
					if (theNewType == null) {
						theNewType = key;
						continue;
					}
					theNewType = GetCommonBaseClass(module, theNewType, key);
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

		UpdatedMethod GetUpdatedMethod(MethodDef method) {
			int token = method.MDToken.ToInt32();
			UpdatedMethod updatedMethod;
			if (updatedMethods.TryGetValue(token, out updatedMethod))
				return updatedMethod;
			return updatedMethods[token] = new UpdatedMethod(method);
		}

		UpdatedField GetUpdatedField(FieldDef field) {
			int token = field.MDToken.ToInt32();
			UpdatedField updatedField;
			if (updatedFields.TryGetValue(token, out updatedField))
				return updatedField;
			return updatedFields[token] = new UpdatedField(field);
		}

		public void Deobfuscate() {
			allMethods = new List<MethodDef>();

			AddAllMethods();
			AddAllFields();

			DeobfuscateLoop();

			RestoreFieldTypes();
			RestoreMethodTypes();
		}

		void AddAllMethods() {
			foreach (var type in module.GetTypes())
				AddMethods(type.Methods);
		}

		void AddMethods(IEnumerable<MethodDef> methods) {
			allMethods.AddRange(methods);
		}

		void AddMethod(MethodDef method) {
			allMethods.Add(method);
		}

		void AddAllFields() {
			foreach (var type in module.GetTypes()) {
				foreach (var field in type.Fields) {
					if (!IsUnknownType(field))
						continue;

					fieldWrites[field] = new TypeInfo<FieldDef>(field);
				}
			}
		}

		void DeobfuscateLoop() {
			for (int i = 0; i < 10; i++) {
				bool modified = false;
				modified |= DeobfuscateFields();
				modified |= DeobfuscateMethods();
				if (!modified)
					break;
			}
		}

		void RestoreFieldTypes() {
			var fields = new List<UpdatedField>(updatedFields.Values);
			if (fields.Count == 0)
				return;

			Logger.v("Changing field types to real type");
			fields.Sort((a, b) => a.token.CompareTo(b.token));
			Logger.Instance.Indent();
			foreach (var updatedField in fields)
				Logger.v("Field {0:X8}: type {1} ({2:X8})", updatedField.token, Utils.RemoveNewlines(updatedField.newFieldType.FullName), updatedField.newFieldType.MDToken.ToInt32());
			Logger.Instance.DeIndent();
		}

		void RestoreMethodTypes() {
			var methods = new List<UpdatedMethod>(updatedMethods.Values);
			if (methods.Count == 0)
				return;

			Logger.v("Changing method args and return types to real type");
			methods.Sort((a, b) => a.token.CompareTo(b.token));
			Logger.Instance.Indent();
			foreach (var updatedMethod in methods) {
				Logger.v("Method {0:X8}", updatedMethod.token);
				Logger.Instance.Indent();
				if (updatedMethod.newReturnType != null) {
					Logger.v("ret: {0} ({1:X8})",
							Utils.RemoveNewlines(updatedMethod.newReturnType.FullName),
							updatedMethod.newReturnType.MDToken.ToInt32());
				}
				for (int i = 0; i < updatedMethod.newArgTypes.Length; i++) {
					var updatedArg = updatedMethod.newArgTypes[i];
					if (updatedArg == null)
						continue;
					Logger.v("arg {0}: {1} ({2:X8})",
							i,
							Utils.RemoveNewlines(updatedArg.FullName),
							updatedArg.MDToken.ToInt32());
				}
				Logger.Instance.DeIndent();
			}
			Logger.Instance.DeIndent();
		}

		bool DeobfuscateMethods() {
			bool modified = false;
			foreach (var method in allMethods) {
				methodReturnInfo = new TypeInfo<Parameter>(method.Parameters.ReturnParameter);
				DeobfuscateMethod(method);

				if (methodReturnInfo.UpdateNewType(module)) {
					GetUpdatedMethod(method).newReturnType = methodReturnInfo.newType;
					method.MethodSig.RetType = methodReturnInfo.newType;
					modified = true;
				}

				foreach (var info in argInfos.Values) {
					if (info.UpdateNewType(module)) {
						GetUpdatedMethod(method).newArgTypes[info.arg.Index] = info.newType;
						info.arg.Type = info.newType;
						modified = true;
					}
				}
			}
			return modified;
		}

		static int SortTypeInfos(TypeInfo<Parameter> a, TypeInfo<Parameter> b) {
			if (a.arg.Method.MDToken.ToInt32() < b.arg.Method.MDToken.ToInt32()) return -1;
			if (a.arg.Method.MDToken.ToInt32() > b.arg.Method.MDToken.ToInt32()) return 1;

			return a.arg.Index.CompareTo(b.arg.Index);
		}

		void DeobfuscateMethod(MethodDef method) {
			if (!method.IsStatic || method.Body == null)
				return;

			bool fixReturnType = IsUnknownType(method.MethodSig.GetRetType());

			argInfos.Clear();
			foreach (var arg in method.Parameters) {
				if (arg.IsHiddenThisParameter)
					continue;
				if (!IsUnknownType(arg))
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
					var type = GetLoadedType(method, method, instructions, i, out wasNewobj);
					if (type == null)
						break;
					methodReturnInfo.Add(type);
					break;

				case Code.Call:
				case Code.Calli:
				case Code.Callvirt:
				case Code.Newobj:
					pushedArgs = MethodStack.GetPushedArgInstructions(instructions, i);
					var calledMethod = instr.Operand as IMethod;
					if (calledMethod == null)
						break;
					var calledMethodParams = DotNetUtils.GetArgs(calledMethod);
					for (int j = 0; j < pushedArgs.NumValidArgs; j++) {
						int calledMethodParamIndex = calledMethodParams.Count - j - 1;
						var ldInstr = pushedArgs.GetEnd(j);
						switch (ldInstr.OpCode.Code) {
						case Code.Ldarg:
						case Code.Ldarg_S:
						case Code.Ldarg_0:
						case Code.Ldarg_1:
						case Code.Ldarg_2:
						case Code.Ldarg_3:
							AddMethodArgType(method, GetParameter(methodParams, ldInstr), DotNetUtils.GetArg(calledMethodParams, calledMethodParamIndex));
							break;

						default:
							break;
						}
					}
					break;

				case Code.Castclass:
					pushedArgs = MethodStack.GetPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs < 1)
						break;
					AddMethodArgType(method, GetParameter(methodParams, pushedArgs.GetEnd(0)), instr.Operand as ITypeDefOrRef);
					break;

				case Code.Stloc:
				case Code.Stloc_S:
				case Code.Stloc_0:
				case Code.Stloc_1:
				case Code.Stloc_2:
				case Code.Stloc_3:
					pushedArgs = MethodStack.GetPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs < 1)
						break;
					AddMethodArgType(method, GetParameter(methodParams, pushedArgs.GetEnd(0)), instr.GetLocal(method.Body.Variables));
					break;

				case Code.Stsfld:
					pushedArgs = MethodStack.GetPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs < 1)
						break;
					AddMethodArgType(method, GetParameter(methodParams, pushedArgs.GetEnd(0)), instr.Operand as IField);
					break;

				case Code.Stfld:
					pushedArgs = MethodStack.GetPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs >= 1) {
						var field = instr.Operand as IField;
						AddMethodArgType(method, GetParameter(methodParams, pushedArgs.GetEnd(0)), field);
						if (pushedArgs.NumValidArgs >= 2 && field != null)
							AddMethodArgType(method, GetParameter(methodParams, pushedArgs.GetEnd(1)), field.DeclaringType);
					}
					break;

				case Code.Ldfld:
				case Code.Ldflda:
					pushedArgs = MethodStack.GetPushedArgInstructions(instructions, i);
					if (pushedArgs.NumValidArgs < 1)
						break;
					AddMethodArgType(method, GetParameter(methodParams, pushedArgs.GetEnd(0)), instr.Operand as IField);
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

		static Parameter GetParameter(IList<Parameter> parameters, Instruction instr) {
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

		bool AddMethodArgType(IGenericParameterProvider gpp, Parameter methodParam, IField field) {
			if (field == null)
				return false;
			return AddMethodArgType(gpp, methodParam, field.FieldSig.GetFieldType());
		}

		bool AddMethodArgType(IGenericParameterProvider gpp, Parameter methodParam, Local otherLocal) {
			if (otherLocal == null)
				return false;
			return AddMethodArgType(gpp, methodParam, otherLocal.Type);
		}

		bool AddMethodArgType(IGenericParameterProvider gpp, Parameter methodParam, Parameter otherParam) {
			if (otherParam == null)
				return false;
			return AddMethodArgType(gpp, methodParam, otherParam.Type);
		}

		bool AddMethodArgType(IGenericParameterProvider gpp, Parameter methodParam, ITypeDefOrRef type) {
			return AddMethodArgType(gpp, methodParam, type.ToTypeSig());
		}

		bool AddMethodArgType(IGenericParameterProvider gpp, Parameter methodParam, TypeSig type) {
			if (methodParam == null || type == null)
				return false;

			if (!IsValidType(gpp, type))
				return false;

			TypeInfo<Parameter> info;
			if (!argInfos.TryGetValue(methodParam, out info))
				return false;
			if (info.Types.ContainsKey(type))
				return false;

			info.Add(type);
			return true;
		}

		bool DeobfuscateFields() {
			foreach (var info in fieldWrites.Values)
				info.Clear();

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
						fieldType = GetLoadedType(info.arg.DeclaringType, method, instructions, i, out wasNewobj);
						if (fieldType == null)
							continue;
						info.Add(fieldType, wasNewobj);
						break;

					case Code.Call:
					case Code.Calli:
					case Code.Callvirt:
					case Code.Newobj:
						var pushedArgs = MethodStack.GetPushedArgInstructions(instructions, i);
						var calledMethod = instr.Operand as IMethod;
						if (calledMethod == null)
							continue;
						var calledMethodDefOrRef = calledMethod as IMethodDefOrRef;
						var calledMethodSpec = calledMethod as MethodSpec;
						if (calledMethodSpec != null)
							calledMethodDefOrRef = calledMethodSpec.Method;
						if (calledMethodDefOrRef == null)
							continue;

						IList<TypeSig> calledMethodArgs = DotNetUtils.GetArgs(calledMethodDefOrRef);
						calledMethodArgs = DotNetUtils.ReplaceGenericParameters(calledMethodDefOrRef.DeclaringType.TryGetGenericInstSig(), calledMethodSpec, calledMethodArgs);
						for (int j = 0; j < pushedArgs.NumValidArgs; j++) {
							var pushInstr = pushedArgs.GetEnd(j);
							if (pushInstr.OpCode.Code != Code.Ldfld && pushInstr.OpCode.Code != Code.Ldsfld)
								continue;

							field = pushInstr.Operand as IField;
							if (field == null)
								continue;
							if (!fieldWrites.TryGetValue(field, out info))
								continue;
							fieldType = calledMethodArgs[calledMethodArgs.Count - 1 - j];
							if (!IsValidType(info.arg.DeclaringType, fieldType))
								continue;
							info.Add(fieldType);
						}
						break;

					default:
						continue;
					}
				}
			}

			bool modified = false;
			var removeThese = new List<FieldDef>();
			foreach (var info in fieldWrites.Values) {
				if (info.UpdateNewType(module)) {
					removeThese.Add(info.arg);
					GetUpdatedField(info.arg).newFieldType = info.newType;
					info.arg.FieldSig.Type = info.newType;
					modified = true;
				}
			}
			foreach (var field in removeThese)
				fieldWrites.Remove(field);
			return modified;
		}

		TypeSig GetLoadedType(IGenericParameterProvider gpp, MethodDef method, IList<Instruction> instructions, int instrIndex, out bool wasNewobj) {
			var fieldType = MethodStack.GetLoadedType(method, instructions, instrIndex, out wasNewobj);
			if (fieldType == null || !IsValidType(gpp, fieldType))
				return null;
			return fieldType;
		}

		protected virtual bool IsValidType(IGenericParameterProvider gpp, TypeSig type) {
			if (type == null)
				return false;
			if (type.ElementType == ElementType.Void)
				return false;

			while (type != null) {
				switch (type.ElementType) {
				case ElementType.GenericInst:
					foreach (var ga in ((GenericInstSig)type).GenericArguments) {
						if (!IsValidType(gpp, ga))
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

		protected abstract bool IsUnknownType(object o);

		static TypeSig GetCommonBaseClass(ModuleDef module, TypeSig a, TypeSig b) {
			if (DotNetUtils.IsDelegate(a) && DotNetUtils.DerivesFromDelegate(module.Find(b.ToTypeDefOrRef())))
				return b;
			if (DotNetUtils.IsDelegate(b) && DotNetUtils.DerivesFromDelegate(module.Find(a.ToTypeDefOrRef())))
				return a;
			return null;	//TODO:
		}
	}

	public class TypesRestorer : TypesRestorerBase {
		public TypesRestorer(ModuleDef module)
			: base(module) {
		}

		protected override bool IsValidType(IGenericParameterProvider gpp, TypeSig type) {
			if (type == null)
				return false;
			if (type.IsValueType)
				return false;
			if (type.ElementType == ElementType.Object)
				return false;
			return base.IsValidType(gpp, type);
		}

		protected override bool IsUnknownType(object o) {
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
