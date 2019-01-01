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
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.code.renamer.asmmodules;
using de4dot.blocks;

namespace de4dot.code.renamer {
	public class TypeInfo : MemberInfo {
		public string oldNamespace;
		public string newNamespace;
		public VariableNameState variableNameState = VariableNameState.Create();
		public MTypeDef type;
		MemberInfos memberInfos;

		public INameChecker NameChecker => type.Module.ObfuscatedFile.NameChecker;

		public TypeInfo(MTypeDef typeDef, MemberInfos memberInfos)
			: base(typeDef) {
			type = typeDef;
			this.memberInfos = memberInfos;
			oldNamespace = typeDef.TypeDef.Namespace.String;
		}

		bool IsWinFormsClass() => memberInfos.IsWinFormsClass(type);
		public PropertyInfo Property(MPropertyDef prop) => memberInfos.Property(prop);
		public EventInfo Event(MEventDef evt) => memberInfos.Event(evt);
		public FieldInfo Field(MFieldDef field) => memberInfos.Field(field);
		public MethodInfo Method(MMethodDef method) => memberInfos.Method(method);
		public GenericParamInfo GenericParam(MGenericParamDef gparam) => memberInfos.GenericParam(gparam);
		public ParamInfo Param(MParamDef param) => memberInfos.Param(param);

		TypeInfo GetBase() {
			if (type.baseType == null)
				return null;

			memberInfos.TryGetType(type.baseType.typeDef, out var baseInfo);
			return baseInfo;
		}

		bool IsModuleType() => type.TypeDef.IsGlobalModuleType;

		public void PrepareRenameTypes(TypeRenamerState state) {
			var checker = NameChecker;

			if (newNamespace == null && oldNamespace != "") {
				if (type.TypeDef.IsNested)
					newNamespace = "";
				else if (!checker.IsValidNamespaceName(oldNamespace))
					newNamespace = state.CreateNamespace(type.TypeDef, oldNamespace);
			}

			string origClassName = null;
			if (IsWinFormsClass())
				origClassName = FindWindowsFormsClassName(type);
			if (IsModuleType()) {
				if (oldNamespace != "")
					newNamespace = "";
				Rename("<Module>");
			}
			else if (!checker.IsValidTypeName(oldName)) {
				if (origClassName != null && checker.IsValidTypeName(origClassName))
					Rename(state.GetTypeName(oldName, origClassName));
				else {
					var nameCreator = type.IsGlobalType() ?
											state.globalTypeNameCreator :
											state.internalTypeNameCreator;
					string newBaseType = null;
					var baseInfo = GetBase();
					if (baseInfo != null && baseInfo.renamed)
						newBaseType = baseInfo.newName;
					Rename(nameCreator.Create(type.TypeDef, newBaseType));
				}
			}

			PrepareRenameGenericParams(type.GenericParams, checker);
		}

		public void MergeState() {
			foreach (var ifaceInfo in type.interfaces)
				MergeState(ifaceInfo.typeDef);
			if (type.baseType != null)
				MergeState(type.baseType.typeDef);
		}

		void MergeState(MTypeDef other) {
			if (other == null)
				return;
			if (!memberInfos.TryGetType(other, out var otherInfo))
				return;
			variableNameState.Merge(otherInfo.variableNameState);
		}

		public void PrepareRenameMembers() {
			MergeState();

			foreach (var fieldDef in type.AllFields)
				variableNameState.AddFieldName(Field(fieldDef).oldName);
			foreach (var eventDef in type.AllEvents)
				variableNameState.AddEventName(Event(eventDef).oldName);
			foreach (var propDef in type.AllProperties)
				variableNameState.AddPropertyName(Property(propDef).oldName);
			foreach (var methodDef in type.AllMethods)
				variableNameState.AddMethodName(Method(methodDef).oldName);

			if (IsWinFormsClass())
				InitializeWindowsFormsFieldsAndProps();

			PrepareRenameFields();
		}

		public void PrepareRenamePropsAndEvents() {
			MergeState();
			PrepareRenameProperties();
			PrepareRenameEvents();
		}

		void PrepareRenameFields() {
			var checker = NameChecker;

			if (type.TypeDef.IsEnum) {
				var instanceFields = GetInstanceFields();
				if (instanceFields.Count == 1)
					Field(instanceFields[0]).Rename("value__");

				int i = 0;
				string nameFormat = HasFlagsAttribute() ? "flag_{0}" : "const_{0}";
				foreach (var fieldDef in type.AllFieldsSorted) {
					var fieldInfo = Field(fieldDef);
					if (fieldInfo.renamed)
						continue;
					if (!fieldDef.FieldDef.IsStatic || !fieldDef.FieldDef.IsLiteral)
						continue;
					if (!checker.IsValidFieldName(fieldInfo.oldName))
						fieldInfo.Rename(string.Format(nameFormat, i));
					i++;
				}
			}
			foreach (var fieldDef in type.AllFieldsSorted) {
				var fieldInfo = Field(fieldDef);
				if (fieldInfo.renamed)
					continue;
				if (!checker.IsValidFieldName(fieldInfo.oldName))
					fieldInfo.Rename(fieldInfo.suggestedName ?? variableNameState.GetNewFieldName(fieldDef.FieldDef));
			}
		}

		List<MFieldDef> GetInstanceFields() {
			var fields = new List<MFieldDef>();
			foreach (var fieldDef in type.AllFields) {
				if (!fieldDef.FieldDef.IsStatic)
					fields.Add(fieldDef);
			}
			return fields;
		}

		bool HasFlagsAttribute() {
			foreach (var attr in type.TypeDef.CustomAttributes) {
				if (attr.AttributeType.FullName == "System.FlagsAttribute")
					return true;
			}
			return false;
		}

		void PrepareRenameProperties() {
			foreach (var propDef in type.AllPropertiesSorted) {
				if (propDef.IsVirtual())
					continue;
				PrepareRenameProperty(propDef);
			}
		}

		void PrepareRenameProperty(MPropertyDef propDef) {
			if (propDef.IsVirtual())
				throw new ApplicationException("Can't rename virtual props here");
			var propInfo = Property(propDef);
			if (propInfo.renamed)
				return;

			string propName = propInfo.oldName;
			if (!NameChecker.IsValidPropertyName(propName))
				propName = propInfo.suggestedName;
			if (!NameChecker.IsValidPropertyName(propName)) {
				if (propDef.IsItemProperty())
					propName = "Item";
				else
					propName = variableNameState.GetNewPropertyName(propDef.PropertyDef);
			}
			variableNameState.AddPropertyName(propName);
			propInfo.Rename(propName);

			RenameSpecialMethod(propDef.GetMethod, "get_" + propName);
			RenameSpecialMethod(propDef.SetMethod, "set_" + propName);
		}

		void PrepareRenameEvents() {
			foreach (var eventDef in type.AllEventsSorted) {
				if (eventDef.IsVirtual())
					continue;
				PrepareRenameEvent(eventDef);
			}
		}

		void PrepareRenameEvent(MEventDef eventDef) {
			if (eventDef.IsVirtual())
				throw new ApplicationException("Can't rename virtual events here");
			var eventInfo = Event(eventDef);
			if (eventInfo.renamed)
				return;

			string eventName = eventInfo.oldName;
			if (!NameChecker.IsValidEventName(eventName))
				eventName = eventInfo.suggestedName;
			if (!NameChecker.IsValidEventName(eventName))
				eventName = variableNameState.GetNewEventName(eventDef.EventDef);
			variableNameState.AddEventName(eventName);
			eventInfo.Rename(eventName);

			RenameSpecialMethod(eventDef.AddMethod, "add_" + eventName);
			RenameSpecialMethod(eventDef.RemoveMethod, "remove_" + eventName);
			RenameSpecialMethod(eventDef.RaiseMethod, "raise_" + eventName);
		}

		void RenameSpecialMethod(MMethodDef methodDef, string newName) {
			if (methodDef == null)
				return;
			if (methodDef.IsVirtual())
				return;
			RenameMethod(methodDef, newName);
		}

		public void PrepareRenameMethods() {
			MergeState();
			foreach (var methodDef in type.AllMethodsSorted) {
				if (methodDef.IsVirtual())
					continue;
				RenameMethod(methodDef);
			}
		}

		public void PrepareRenameMethods2() {
			var checker = NameChecker;
			foreach (var methodDef in type.AllMethodsSorted) {
				PrepareRenameMethodArgs(methodDef);
				PrepareRenameGenericParams(methodDef.GenericParams, checker, methodDef.Owner?.GenericParams);
			}
		}

		void PrepareRenameMethodArgs(MMethodDef methodDef) {
			VariableNameState newVariableNameState = null;
			ParamInfo info;
			if (methodDef.VisibleParameterCount > 0) {
				if (IsEventHandler(methodDef)) {
					info = Param(methodDef.ParamDefs[methodDef.VisibleParameterBaseIndex]);
					if (!info.GotNewName())
						info.newName = "sender";

					info = Param(methodDef.ParamDefs[methodDef.VisibleParameterBaseIndex + 1]);
					if (!info.GotNewName())
						info.newName = "e";
				}
				else {
					newVariableNameState = variableNameState.CloneParamsOnly();
					var checker = NameChecker;
					foreach (var paramDef in methodDef.ParamDefs) {
						if (paramDef.IsHiddenThisParameter)
							continue;
						info = Param(paramDef);
						if (info.GotNewName())
							continue;
						if (!checker.IsValidMethodArgName(info.oldName))
							info.newName = newVariableNameState.GetNewParamName(info.oldName, paramDef.ParameterDef);
					}
				}
			}

			info = Param(methodDef.ReturnParamDef);
			if (!info.GotNewName()) {
				if (!NameChecker.IsValidMethodReturnArgName(info.oldName)) {
					if (newVariableNameState == null)
						newVariableNameState = variableNameState.CloneParamsOnly();
					info.newName = newVariableNameState.GetNewParamName(info.oldName, methodDef.ReturnParamDef.ParameterDef);
				}
			}

			if ((methodDef.Property != null && methodDef == methodDef.Property.SetMethod) ||
				(methodDef.Event != null && (methodDef == methodDef.Event.AddMethod || methodDef == methodDef.Event.RemoveMethod))) {
				if (methodDef.VisibleParameterCount > 0) {
					var paramDef = methodDef.ParamDefs[methodDef.ParamDefs.Count - 1];
					Param(paramDef).newName = "value";
				}
			}
		}

		bool CanRenameMethod(MMethodDef methodDef) {
			var methodInfo = Method(methodDef);
			if (methodDef.IsStatic()) {
				if (methodInfo.oldName == ".cctor")
					return false;
			}
			else if (methodDef.IsVirtual()) {
				if (DotNetUtils.DerivesFromDelegate(type.TypeDef)) {
					switch (methodInfo.oldName) {
					case "BeginInvoke":
					case "EndInvoke":
					case "Invoke":
						return false;
					}
				}
			}
			else {
				if (methodInfo.oldName == ".ctor")
					return false;
			}
			return true;
		}

		public void RenameMethod(MMethodDef methodDef, string methodName) {
			if (!CanRenameMethod(methodDef))
				return;
			var methodInfo = Method(methodDef);
			variableNameState.AddMethodName(methodName);
			methodInfo.Rename(methodName);
		}

		void RenameMethod(MMethodDef methodDef) {
			if (methodDef.IsVirtual())
				throw new ApplicationException("Can't rename virtual methods here");
			if (!CanRenameMethod(methodDef))
				return;

			var info = Method(methodDef);
			if (info.renamed)
				return;
			info.renamed = true;
			var checker = NameChecker;

			// PInvoke methods' EntryPoint is always valid. It has to, so always rename.
			bool isValidName = NameChecker.IsValidMethodName(info.oldName);
			bool isExternPInvoke = methodDef.MethodDef.ImplMap != null && methodDef.MethodDef.RVA == 0;
			if (!isValidName || isExternPInvoke) {
				INameCreator nameCreator = null;
				string newName = info.suggestedName;
				string newName2;
				if (methodDef.MethodDef.ImplMap != null && !string.IsNullOrEmpty(newName2 = GetPinvokeName(methodDef)))
					newName = newName2;
				else if (methodDef.IsStatic())
					nameCreator = variableNameState.staticMethodNameCreator;
				else
					nameCreator = variableNameState.instanceMethodNameCreator;
				if (!string.IsNullOrEmpty(newName))
					nameCreator = new NameCreator2(newName);
				RenameMethod(methodDef, variableNameState.GetNewMethodName(info.oldName, nameCreator));
			}
		}

		string GetPinvokeName(MMethodDef methodDef) {
			var entryPoint = methodDef.MethodDef.ImplMap.Name.String;
			if (Regex.IsMatch(entryPoint, @"^#\d+$"))
				entryPoint = DotNetUtils.GetDllName(methodDef.MethodDef.ImplMap.Module.Name.String) + "_" + entryPoint.Substring(1);
			return entryPoint;
		}

		static bool IsEventHandler(MMethodDef methodDef) {
			var sig = methodDef.MethodDef.MethodSig;
			if (sig == null || sig.Params.Count != 2)
				return false;
			if (sig.RetType.ElementType != ElementType.Void)
				return false;
			if (sig.Params[0].ElementType != ElementType.Object)
				return false;
			if (!sig.Params[1].FullName.Contains("EventArgs"))
				return false;
			return true;
		}

		void PrepareRenameGenericParams(IEnumerable<MGenericParamDef> genericParams, INameChecker checker) =>
			PrepareRenameGenericParams(genericParams, checker, null);

		void PrepareRenameGenericParams(IEnumerable<MGenericParamDef> genericParams, INameChecker checker, IEnumerable<MGenericParamDef> otherGenericParams) {
			var usedNames = new Dictionary<string, bool>(StringComparer.Ordinal);
			var nameCreator = new GenericParamNameCreator();

			if (otherGenericParams != null) {
				foreach (var param in otherGenericParams) {
					var gpInfo = memberInfos.GenericParam(param);
					usedNames[gpInfo.newName] = true;
				}
			}

			foreach (var param in genericParams) {
				var gpInfo = memberInfos.GenericParam(param);
				if (!checker.IsValidGenericParamName(gpInfo.oldName) || usedNames.ContainsKey(gpInfo.oldName)) {
					string newName;
					do {
						newName = nameCreator.Create();
					} while (usedNames.ContainsKey(newName));
					usedNames[newName] = true;
					gpInfo.Rename(newName);
				}
			}
		}

		void InitializeWindowsFormsFieldsAndProps() {
			var checker = NameChecker;

			var ourFields = new FieldDefAndDeclaringTypeDict<MFieldDef>();
			foreach (var fieldDef in type.AllFields)
				ourFields.Add(fieldDef.FieldDef, fieldDef);
			var ourMethods = new MethodDefAndDeclaringTypeDict<MMethodDef>();
			foreach (var methodDef in type.AllMethods)
				ourMethods.Add(methodDef.MethodDef, methodDef);

			foreach (var methodDef in type.AllMethods) {
				if (methodDef.MethodDef.Body == null)
					continue;
				if (methodDef.MethodDef.IsStatic || methodDef.MethodDef.IsVirtual)
					continue;
				var instructions = methodDef.MethodDef.Body.Instructions;
				for (int i = 2; i < instructions.Count; i++) {
					var call = instructions[i];
					if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
						continue;
					if (!IsWindowsFormsSetNameMethod(call.Operand as IMethod))
						continue;

					var ldstr = instructions[i - 1];
					if (ldstr.OpCode.Code != Code.Ldstr)
						continue;
					var fieldName = ldstr.Operand as string;
					if (fieldName == null || !checker.IsValidFieldName(fieldName))
						continue;

					var instr = instructions[i - 2];
					IField fieldRef = null;
					if (instr.OpCode.Code == Code.Call || instr.OpCode.Code == Code.Callvirt) {
						var calledMethod = instr.Operand as IMethod;
						if (calledMethod == null)
							continue;
						var calledMethodDef = ourMethods.Find(calledMethod);
						if (calledMethodDef == null)
							continue;
						fieldRef = GetFieldRef(calledMethodDef.MethodDef);

						var propDef = calledMethodDef.Property;
						if (propDef == null)
							continue;

						memberInfos.Property(propDef).suggestedName = fieldName;
						fieldName = "_" + fieldName;
					}
					else if (instr.OpCode.Code == Code.Ldfld) {
						fieldRef = instr.Operand as IField;
					}

					if (fieldRef == null)
						continue;
					var fieldDef = ourFields.Find(fieldRef);
					if (fieldDef == null)
						continue;
					var fieldInfo = memberInfos.Field(fieldDef);

					if (fieldInfo.renamed)
						continue;

					fieldInfo.suggestedName = variableNameState.GetNewFieldName(fieldInfo.oldName, new NameCreator2(fieldName));
				}
			}
		}

		static IField GetFieldRef(MethodDef method) {
			if (method == null || method.Body == null)
				return null;
			var instructions = method.Body.Instructions;
			int index = 0;
			var ldarg0 = DotNetUtils.GetInstruction(instructions, ref index);
			if (ldarg0 == null || ldarg0.GetParameterIndex() != 0)
				return null;
			var ldfld = DotNetUtils.GetInstruction(instructions, ref index);
			if (ldfld == null || ldfld.OpCode.Code != Code.Ldfld)
				return null;
			var ret = DotNetUtils.GetInstruction(instructions, ref index);
			if (ret == null)
				return null;
			if (ret.IsStloc()) {
				var local = ret.GetLocal(method.Body.Variables);
				ret = DotNetUtils.GetInstruction(instructions, ref index);
				if (ret == null || !ret.IsLdloc())
					return null;
				if (ret.GetLocal(method.Body.Variables) != local)
					return null;
				ret = DotNetUtils.GetInstruction(instructions, ref index);
			}
			if (ret == null || ret.OpCode.Code != Code.Ret)
				return null;
			return ldfld.Operand as IField;
		}

		public void InitializeEventHandlerNames() {
			var ourFields = new FieldDefAndDeclaringTypeDict<MFieldDef>();
			foreach (var fieldDef in type.AllFields)
				ourFields.Add(fieldDef.FieldDef, fieldDef);
			var ourMethods = new MethodDefAndDeclaringTypeDict<MMethodDef>();
			foreach (var methodDef in type.AllMethods)
				ourMethods.Add(methodDef.MethodDef, methodDef);

			InitVbEventHandlers(ourFields, ourMethods);
			InitFieldEventHandlers(ourFields, ourMethods);
			InitTypeEventHandlers(ourFields, ourMethods);
		}

		// VB initializes the handlers in the property setter, where it first removes the handler
		// from the previous control, and then adds the handler to the new control.
		void InitVbEventHandlers(FieldDefAndDeclaringTypeDict<MFieldDef> ourFields, MethodDefAndDeclaringTypeDict<MMethodDef> ourMethods) {
			var checker = NameChecker;

			foreach (var propDef in type.AllProperties) {
				var setterDef = propDef.SetMethod;
				if (setterDef == null)
					continue;

				var handler = GetVbHandler(setterDef.MethodDef, out string eventName);
				if (handler == null)
					continue;
				var handlerDef = ourMethods.Find(handler);
				if (handlerDef == null)
					continue;

				if (!checker.IsValidEventName(eventName))
					continue;

				memberInfos.Method(handlerDef).suggestedName = $"{memberInfos.Property(propDef).newName}_{eventName}";
			}
		}

		static IMethod GetVbHandler(MethodDef method, out string eventName) {
			eventName = null;
			if (method.Body == null)
				return null;
			var sig = method.MethodSig;
			if (sig == null)
				return null;
			if (sig.RetType.ElementType != ElementType.Void)
				return null;
			if (sig.Params.Count != 1)
				return null;
			if (method.Body.Variables.Count != 1)
				return null;
			if (!IsEventHandlerType(method.Body.Variables[0].Type))
				return null;

			var instructions = method.Body.Instructions;
			int index = 0;

			int newobjIndex = FindInstruction(instructions, index, Code.Newobj);
			if (newobjIndex == -1 || FindInstruction(instructions, newobjIndex + 1, Code.Newobj) != -1)
				return null;
			if (!IsEventHandlerCtor(instructions[newobjIndex].Operand as IMethod))
				return null;
			if (newobjIndex < 1)
				return null;
			var ldvirtftn = instructions[newobjIndex - 1];
			if (ldvirtftn.OpCode.Code != Code.Ldvirtftn && ldvirtftn.OpCode.Code != Code.Ldftn)
				return null;
			var handlerMethod = ldvirtftn.Operand as IMethod;
			if (handlerMethod == null)
				return null;
			if (!new SigComparer().Equals(method.DeclaringType, handlerMethod.DeclaringType))
				return null;
			index = newobjIndex;

			if (!FindEventCall(instructions, ref index, out var removeField, out var removeMethod))
				return null;
			if (!FindEventCall(instructions, ref index, out var addField, out var addMethod))
				return null;

			if (FindInstruction(instructions, index, Code.Callvirt) != -1)
				return null;
			if (!new SigComparer().Equals(addField, removeField))
				return null;
			if (!new SigComparer().Equals(method.DeclaringType, addField.DeclaringType))
				return null;
			if (!new SigComparer().Equals(addMethod.DeclaringType, removeMethod.DeclaringType))
				return null;
			if (!Utils.StartsWith(addMethod.Name.String, "add_", StringComparison.Ordinal))
				return null;
			if (!Utils.StartsWith(removeMethod.Name.String, "remove_", StringComparison.Ordinal))
				return null;
			eventName = addMethod.Name.String.Substring(4);
			if (eventName != removeMethod.Name.String.Substring(7))
				return null;
			if (eventName == "")
				return null;

			return handlerMethod;
		}

		static bool FindEventCall(IList<Instruction> instructions, ref int index, out IField field, out IMethod calledMethod) {
			field = null;
			calledMethod = null;

			int callvirt = FindInstruction(instructions, index, Code.Callvirt);
			if (callvirt < 2)
				return false;
			index = callvirt + 1;

			var ldloc = instructions[callvirt - 1];
			if (ldloc.OpCode.Code != Code.Ldloc_0)
				return false;

			var ldfld = instructions[callvirt - 2];
			if (ldfld.OpCode.Code != Code.Ldfld)
				return false;

			field = ldfld.Operand as IField;
			calledMethod = instructions[callvirt].Operand as IMethod;
			return field != null && calledMethod != null;
		}

		static int FindInstruction(IList<Instruction> instructions, int index, Code code) {
			for (int i = index; i < instructions.Count; i++) {
				if (instructions[i].OpCode.Code == code)
					return i;
			}
			return -1;
		}

		void InitFieldEventHandlers(FieldDefAndDeclaringTypeDict<MFieldDef> ourFields, MethodDefAndDeclaringTypeDict<MMethodDef> ourMethods) {
			var checker = NameChecker;

			foreach (var methodDef in type.AllMethods) {
				if (methodDef.MethodDef.Body == null)
					continue;
				if (methodDef.MethodDef.IsStatic)
					continue;
				var instructions = methodDef.MethodDef.Body.Instructions;
				for (int i = 0; i < instructions.Count - 6; i++) {
					// We're looking for this code pattern:
					//	ldarg.0
					//	ldfld field
					//	ldarg.0
					//	ldftn method / ldarg.0 + ldvirtftn
					//	newobj event_handler_ctor
					//	callvirt add_SomeEvent

					if (instructions[i].GetParameterIndex() != 0)
						continue;
					int index = i + 1;

					var ldfld = instructions[index++];
					if (ldfld.OpCode.Code != Code.Ldfld)
						continue;
					var fieldRef = ldfld.Operand as IField;
					if (fieldRef == null)
						continue;
					var fieldDef = ourFields.Find(fieldRef);
					if (fieldDef == null)
						continue;

					if (instructions[index++].GetParameterIndex() != 0)
						continue;

					IMethod methodRef;
					var instr = instructions[index + 1];
					if (instr.OpCode.Code == Code.Ldvirtftn) {
						if (!IsThisOrDup(instructions[index++]))
							continue;
						var ldvirtftn = instructions[index++];
						methodRef = ldvirtftn.Operand as IMethod;
					}
					else {
						var ldftn = instructions[index++];
						if (ldftn.OpCode.Code != Code.Ldftn)
							continue;
						methodRef = ldftn.Operand as IMethod;
					}
					if (methodRef == null)
						continue;
					var handlerMethod = ourMethods.Find(methodRef);
					if (handlerMethod == null)
						continue;

					var newobj = instructions[index++];
					if (newobj.OpCode.Code != Code.Newobj)
						continue;
					if (!IsEventHandlerCtor(newobj.Operand as IMethod))
						continue;

					var call = instructions[index++];
					if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
						continue;
					var addHandler = call.Operand as IMethod;
					if (addHandler == null)
						continue;
					if (!Utils.StartsWith(addHandler.Name.String, "add_", StringComparison.Ordinal))
						continue;

					var eventName = addHandler.Name.String.Substring(4);
					if (!checker.IsValidEventName(eventName))
						continue;

					memberInfos.Method(handlerMethod).suggestedName = $"{memberInfos.Field(fieldDef).newName}_{eventName}";
				}
			}
		}

		void InitTypeEventHandlers(FieldDefAndDeclaringTypeDict<MFieldDef> ourFields, MethodDefAndDeclaringTypeDict<MMethodDef> ourMethods) {
			var checker = NameChecker;

			foreach (var methodDef in type.AllMethods) {
				if (methodDef.MethodDef.Body == null)
					continue;
				if (methodDef.MethodDef.IsStatic)
					continue;
				var method = methodDef.MethodDef;
				var instructions = method.Body.Instructions;
				for (int i = 0; i < instructions.Count - 5; i++) {
					// ldarg.0
					// ldarg.0 / dup
					// ldarg.0 / dup
					// ldvirtftn handler
					// newobj event handler ctor
					// call add_Xyz

					if (instructions[i].GetParameterIndex() != 0)
						continue;
					int index = i + 1;

					if (!IsThisOrDup(instructions[index++]))
						continue;
					IMethod handler;
					if (instructions[index].OpCode.Code == Code.Ldftn) {
						handler = instructions[index++].Operand as IMethod;
					}
					else {
						if (!IsThisOrDup(instructions[index++]))
							continue;
						var instr = instructions[index++];
						if (instr.OpCode.Code != Code.Ldvirtftn)
							continue;
						handler = instr.Operand as IMethod;
					}
					if (handler == null)
						continue;
					var handlerDef = ourMethods.Find(handler);
					if (handlerDef == null)
						continue;

					var newobj = instructions[index++];
					if (newobj.OpCode.Code != Code.Newobj)
						continue;
					if (!IsEventHandlerCtor(newobj.Operand as IMethod))
						continue;

					var call = instructions[index++];
					if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
						continue;
					var addMethod = call.Operand as IMethod;
					if (addMethod == null)
						continue;
					if (!Utils.StartsWith(addMethod.Name.String, "add_", StringComparison.Ordinal))
						continue;

					var eventName = addMethod.Name.String.Substring(4);
					if (!checker.IsValidEventName(eventName))
						continue;

					memberInfos.Method(handlerDef).suggestedName = $"{newName}_{eventName}";
				}
			}
		}

		static bool IsThisOrDup(Instruction instr) => instr.GetParameterIndex() == 0 || instr.OpCode.Code == Code.Dup;

		static bool IsEventHandlerCtor(IMethod method) {
			if (method == null)
				return false;
			if (method.Name != ".ctor")
				return false;
			if (!DotNetUtils.IsMethod(method, "System.Void", "(System.Object,System.IntPtr)"))
				return false;
			if (!IsEventHandlerType(method.DeclaringType))
				return false;
			return true;
		}

		static bool IsEventHandlerType(IType type) => type.FullName.EndsWith("EventHandler", StringComparison.Ordinal);

		string FindWindowsFormsClassName(MTypeDef type) {
			foreach (var methodDef in type.AllMethods) {
				if (methodDef.MethodDef.Body == null)
					continue;
				if (methodDef.MethodDef.IsStatic || methodDef.MethodDef.IsVirtual)
					continue;
				var instructions = methodDef.MethodDef.Body.Instructions;
				for (int i = 2; i < instructions.Count; i++) {
					var call = instructions[i];
					if (call.OpCode.Code != Code.Call && call.OpCode.Code != Code.Callvirt)
						continue;
					if (!IsWindowsFormsSetNameMethod(call.Operand as IMethod))
						continue;

					var ldstr = instructions[i - 1];
					if (ldstr.OpCode.Code != Code.Ldstr)
						continue;
					var className = ldstr.Operand as string;
					if (className == null)
						continue;

					if (instructions[i - 2].GetParameterIndex() != 0)
						continue;

					FindInitializeComponentMethod(type, methodDef);
					return className;
				}
			}
			return null;
		}

		void FindInitializeComponentMethod(MTypeDef type, MMethodDef possibleInitMethod) {
			foreach (var methodDef in type.AllMethods) {
				if (methodDef.MethodDef.Name != ".ctor")
					continue;
				if (methodDef.MethodDef.Body == null)
					continue;
				foreach (var instr in methodDef.MethodDef.Body.Instructions) {
					if (instr.OpCode.Code != Code.Call && instr.OpCode.Code != Code.Callvirt)
						continue;
					if (!MethodEqualityComparer.CompareDeclaringTypes.Equals(possibleInitMethod.MethodDef, instr.Operand as IMethod))
						continue;

					memberInfos.Method(possibleInitMethod).suggestedName = "InitializeComponent";
					return;
				}
			}
		}

		static bool IsWindowsFormsSetNameMethod(IMethod method) {
			if (method == null)
				return false;
			if (method.Name.String != "set_Name")
				return false;
			var sig = method.MethodSig;
			if (sig == null)
				return false;
			if (sig.RetType.ElementType != ElementType.Void)
				return false;
			if (sig.Params.Count != 1)
				return false;
			if (sig.Params[0].ElementType != ElementType.String)
				return false;
			return true;
		}
	}
}
