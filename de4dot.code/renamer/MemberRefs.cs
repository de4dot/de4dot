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

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Mono.Cecil;
using de4dot.blocks;
using de4dot.deobfuscators;

namespace de4dot.renamer {
	abstract class Ref {
		public string NewName { get; set; }
		public string OldName { get; private set; }
		public string OldFullName { get; private set; }
		public int Index { get; private set; }
		public MemberReference MemberReference { get; private set; }
		public TypeDef Owner { get; set; }
		public bool Renamed { get; set; }

		public Ref(MemberReference mr, TypeDef owner, int index) {
			MemberReference = mr;
			NewName = OldName = mr.Name;
			OldFullName = mr.FullName;
			Owner = owner;
			Index = index;
		}

		public bool gotNewName() {
			return NewName != OldName;
		}

		public abstract bool isSame(MemberReference mr);

		public bool rename(string newName) {
			if (Renamed)
				return false;
			Renamed = true;
			NewName = newName;
			return true;
		}

		static protected bool isVirtual(MethodDefinition m) {
			return m != null && m.IsVirtual;
		}

		protected static IList<GenericParamDef> createGenericParamDefList(IEnumerable<GenericParameter> parameters) {
			var list = new List<GenericParamDef>();
			if (parameters == null)
				return list;
			int i = 0;
			foreach (var param in parameters)
				list.Add(new GenericParamDef(param, i++));
			return list;
		}
	}

	class FieldDef : Ref {
		public FieldDef(FieldDefinition fieldDefinition, TypeDef owner, int index)
			: base(fieldDefinition, owner, index) {
		}

		public FieldDefinition FieldDefinition {
			get { return (FieldDefinition)MemberReference; }
		}

		public override bool isSame(MemberReference mr) {
			return MemberReferenceHelper.compareFieldReference(FieldDefinition, mr as FieldReference);
		}
	}

	class EventRef : Ref {
		public EventRef(EventReference eventReference, TypeDef owner, int index)
			: base(eventReference, owner, index) {
		}

		public EventReference EventReference {
			get { return (EventReference)MemberReference; }
		}

		public override bool isSame(MemberReference mr) {
			return MemberReferenceHelper.compareEventReference(EventReference, mr as EventReference);
		}
	}

	class EventDef : EventRef {
		public EventDef(EventDefinition eventDefinition, TypeDef owner, int index)
			: base(eventDefinition, owner, index) {
		}

		public EventDefinition EventDefinition {
			get { return (EventDefinition)MemberReference; }
		}

		public IEnumerable<MethodDefinition> methodDefinitions() {
			if (EventDefinition.AddMethod != null)
				yield return EventDefinition.AddMethod;
			if (EventDefinition.RemoveMethod != null)
				yield return EventDefinition.RemoveMethod;
			if (EventDefinition.InvokeMethod != null)
				yield return EventDefinition.InvokeMethod;
			if (EventDefinition.OtherMethods != null) {
				foreach (var m in EventDefinition.OtherMethods)
					yield return m;
			}
		}

		// Returns one of the overridden methods or null if none found
		public MethodReference getOverrideMethod() {
			foreach (var method in methodDefinitions()) {
				if (method.HasOverrides)
					return method.Overrides[0];
			}
			return null;
		}

		public bool isVirtual() {
			foreach (var method in methodDefinitions()) {
				if (isVirtual(method))
					return true;
			}
			return false;
		}
	}

	class PropertyRef : Ref {
		public PropertyRef(PropertyReference propertyReference, TypeDef owner, int index)
			: base(propertyReference, owner, index) {
		}

		public PropertyReference PropertyReference {
			get { return (PropertyReference)MemberReference; }
		}

		public override bool isSame(MemberReference mr) {
			return MemberReferenceHelper.comparePropertyReference(PropertyReference, mr as PropertyReference);
		}
	}

	class PropertyDef : PropertyRef {
		public PropertyDef(PropertyDefinition propertyDefinition, TypeDef owner, int index)
			: base(propertyDefinition, owner, index) {
		}

		public PropertyDefinition PropertyDefinition {
			get { return (PropertyDefinition)MemberReference; }
		}

		public IEnumerable<MethodDefinition> methodDefinitions() {
			if (PropertyDefinition.GetMethod != null)
				yield return PropertyDefinition.GetMethod;
			if (PropertyDefinition.SetMethod != null)
				yield return PropertyDefinition.SetMethod;
			if (PropertyDefinition.OtherMethods != null) {
				foreach (var m in PropertyDefinition.OtherMethods)
					yield return m;
			}
		}

		// Returns one of the overridden methods or null if none found
		public MethodReference getOverrideMethod() {
			foreach (var method in methodDefinitions()) {
				if (method.HasOverrides)
					return method.Overrides[0];
			}
			return null;
		}

		public bool isVirtual() {
			foreach (var method in methodDefinitions()) {
				if (isVirtual(method))
					return true;
			}
			return false;
		}
	}

	class MethodRef : Ref {
		public IList<ParamDef> paramDefs = new List<ParamDef>();

		public IList<ParamDef> ParamDefs {
			get { return paramDefs; }
		}

		public MethodRef(MethodReference methodReference, TypeDef owner, int index)
			: base(methodReference, owner, index) {
			if (methodReference.HasParameters) {
				for (int i = 0; i < methodReference.Parameters.Count; i++) {
					var param = methodReference.Parameters[i];
					paramDefs.Add(new ParamDef(param, i));
				}
			}
		}

		public MethodReference MethodReference {
			get { return (MethodReference)MemberReference; }
		}

		public override bool isSame(MemberReference mr) {
			return MemberReferenceHelper.compareMethodReference(MethodReference, mr as MethodReference);
		}
	}

	class MethodDef : MethodRef {
		IList<GenericParamDef> genericParams;

		public IList<GenericParamDef> GenericParams {
			get { return genericParams; }
		}
		public PropertyDef Property { get; set; }
		public EventDef Event { get; set; }

		public MethodDef(MethodDefinition methodDefinition, TypeDef owner, int index)
			: base(methodDefinition, owner, index) {
			genericParams = createGenericParamDefList(MethodDefinition.GenericParameters);
		}

		public MethodDefinition MethodDefinition {
			get { return (MethodDefinition)MemberReference; }
		}

		public bool isVirtual() {
			return isVirtual(MethodDefinition);
		}
	}

	class ParamDef {
		public ParameterDefinition ParameterDefinition { get; set; }
		public string OldName { get; private set; }
		public string NewName { get; set; }
		public int Index { get; private set; }
		public bool Renamed { get; set; }

		public ParamDef(ParameterDefinition parameterDefinition, int index) {
			this.ParameterDefinition = parameterDefinition;
			NewName = OldName = parameterDefinition.Name;
			Index = index;
		}

		public bool gotNewName() {
			return NewName != OldName;
		}
	}

	class GenericParamDef : Ref {
		public GenericParamDef(GenericParameter genericParameter, int index)
			: base(genericParameter, null, index) {
		}

		public GenericParameter GenericParameter {
			get { return (GenericParameter)MemberReference; }
		}

		public override bool isSame(MemberReference mr) {
			throw new NotImplementedException();
		}
	}

	class TypeInfo {
		public TypeReference typeReference;
		public TypeDef typeDef;
		public TypeInfo(TypeReference typeReference, TypeDef typeDef) {
			this.typeReference = typeReference;
			this.typeDef = typeDef;
		}
	}

	class TypeDef : Ref {
		public IDefFinder defFinder;
		public TypeInfo baseType = null;
		public IList<TypeInfo> interfaces = new List<TypeInfo>();	// directly implemented interfaces
		public IList<TypeDef> derivedTypes = new List<TypeDef>();
		public Module module;
		string newNamespace = null;

		EventDefDict events = new EventDefDict();
		FieldDefDict fields = new FieldDefDict();
		MethodDefDict methods = new MethodDefDict();
		PropertyDefDict properties = new PropertyDefDict();
		TypeDefDict types = new TypeDefDict();
		IList<GenericParamDef> genericParams;
		public TypeDefinition TypeDefinition {
			get { return (TypeDefinition)MemberReference; }
		}
		public MemberRenameState MemberRenameState { get; set; }
		public MemberRenameState InterfaceScopeState { get; set; }
		bool prepareRenameMembersCalled = false;

		public IEnumerable<TypeDef> NestedTypes {
			get { return types.getSorted(); }
		}

		public TypeDef NestingType { get; set; }

		public IList<GenericParamDef> GenericParams {
			get { return genericParams; }
		}

		public TypeDef(TypeDefinition typeDefinition, Module module, int index = 0)
			: base(typeDefinition, null, index) {
			this.module = module;
			genericParams = createGenericParamDefList(TypeDefinition.GenericParameters);
		}

		public override bool isSame(MemberReference mr) {
			return MemberReferenceHelper.compareTypes(TypeDefinition, mr as TypeReference);
		}

		public bool isInterface() {
			return TypeDefinition.IsInterface;
		}

		public IEnumerable<MethodDef> Methods {
			get { return methods.getAll(); }
		}

		// Called when all members (events, fields, props, methods) have been added
		public void membersAdded() {
			foreach (var propDef in properties.getAll()) {
				foreach (var method in propDef.methodDefinitions()) {
					var methodDef = find(method);
					if (methodDef == null)
						throw new ApplicationException("Could not find property method");
					methodDef.Property = propDef;
				}
			}

			foreach (var eventDef in events.getAll()) {
				foreach (var method in eventDef.methodDefinitions()) {
					var methodDef = find(method);
					if (methodDef == null)
						throw new ApplicationException("Could not find event method");
					methodDef.Event = eventDef;
				}
			}
		}

		public IEnumerable<TypeDef> getAllInterfaces() {
			if (isInterface())
				yield return this;
			foreach (var ifaceInfo in interfaces) {
				foreach (var iface in ifaceInfo.typeDef.getAllInterfaces())
					yield return iface;
			}
			foreach (var typeDef in derivedTypes) {
				foreach (var iface in typeDef.getAllInterfaces())
					yield return iface;
			}
		}

		public void add(EventDef e) {
			events.add(e);
		}

		public void add(FieldDef f) {
			fields.add(f);
		}

		public void add(MethodDef m) {
			methods.add(m);
		}

		public void add(PropertyDef p) {
			properties.add(p);
		}

		public void add(TypeDef t) {
			types.add(t);
		}

		public MethodDef find(MethodReference mr) {
			return methods.find(mr);
		}

		public FieldDef find(FieldReference fr) {
			return fields.find(fr);
		}

		IEnumerable<FieldDef> getInstanceFields() {
			foreach (var fieldDef in fields.getSorted()) {
				if (!fieldDef.FieldDefinition.IsStatic)
					yield return fieldDef;
			}
		}

		bool isNested() {
			return NestingType != null;
		}

		bool isGlobalType() {
			if (!isNested())
				return TypeDefinition.IsPublic;
			var mask = TypeDefinition.Attributes & TypeAttributes.VisibilityMask;
			switch (mask) {
			case TypeAttributes.NestedPrivate:
			case TypeAttributes.NestedAssembly:
			case TypeAttributes.NestedFamANDAssem:
				return false;
			case TypeAttributes.NestedPublic:
			case TypeAttributes.NestedFamily:
			case TypeAttributes.NestedFamORAssem:
				return NestingType.isGlobalType();
			default:
				return false;
			}
		}

		// Renames name, namespace, and generic parameters if needed. Does not rename members.
		public void prepareRename(TypeNameState typeNameState) {
			var typeDefinition = TypeDefinition;
			ITypeNameCreator nameCreator = isGlobalType() ?
					typeNameState.globalTypeNameCreator :
					typeNameState.internalTypeNameCreator;

			if (OldFullName != "<Module>" && !typeNameState.IsValidName(OldName)) {
				var newBaseType = baseType != null && baseType.typeDef.Renamed ? baseType.typeDef.NewName : null;
				rename(nameCreator.newName(typeDefinition, newBaseType));
			}

			if (typeDefinition.Namespace != "" && !typeNameState.isValidNamespace(typeDefinition.Namespace))
				newNamespace = typeNameState.newNamespace(typeDefinition.Namespace);

			prepareRenameGenericParams(genericParams, typeNameState.IsValidName);
		}

		public void rename() {
			var typeDefinition = TypeDefinition;

			Log.v("Type: {0} ({1:X8})", TypeDefinition.FullName, TypeDefinition.MetadataToken.ToUInt32());
			Log.indent();

			renameGenericParams(genericParams);

			if (gotNewName()) {
				var old = typeDefinition.Name;
				typeDefinition.Name = NewName;
				Log.v("Name: {0} => {1}", old, typeDefinition.Name);
			}

			if (newNamespace != null) {
				var old = typeDefinition.Namespace;
				typeDefinition.Namespace = newNamespace;
				Log.v("Namespace: {0} => {1}", old, typeDefinition.Namespace);
			}

			Log.deIndent();
		}

		static void prepareRenameGenericParams(IList<GenericParamDef> genericParams, Func<string, bool> isValidName, IList<GenericParamDef> otherGenericParams = null) {
			Dictionary<string, bool> usedNames = new Dictionary<string, bool>(StringComparer.Ordinal);
			INameCreator nameCreator = new GenericParamNameCreator();

			if (otherGenericParams != null) {
				foreach (var param in otherGenericParams)
					usedNames[param.NewName] = true;
			}

			foreach (var param in genericParams) {
				if (!isValidName(param.OldName) || usedNames.ContainsKey(param.OldName)) {
					string newName;
					do {
						newName = nameCreator.newName();
					} while (usedNames.ContainsKey(newName));
					usedNames[newName] = true;
					param.rename(newName);
				}
			}
		}

		static void renameGenericParams(IList<GenericParamDef> genericParams) {
			foreach (var param in genericParams) {
				if (!param.gotNewName())
					continue;
				param.GenericParameter.Name = param.NewName;
				Log.v("GenParam: {0} => {1}", param.OldFullName, param.GenericParameter.FullName);
			}
		}

		public void renameMembers() {
			Log.v("Type: {0}", TypeDefinition.FullName);
			Log.indent();

			renameFields();
			renameProperties();
			renameEvents();
			renameMethods();

			Log.deIndent();
		}

		void renameFields() {
			foreach (var fieldDef in fields.getSorted()) {
				if (!fieldDef.gotNewName())
					continue;
				fieldDef.FieldDefinition.Name = fieldDef.NewName;
				Log.v("Field: {0} ({1:X8}) => {2}", fieldDef.OldFullName, fieldDef.FieldDefinition.MetadataToken.ToUInt32(), fieldDef.FieldDefinition.FullName);
			}
		}

		void renameProperties() {
			foreach (var propDef in properties.getSorted()) {
				if (!propDef.gotNewName())
					continue;
				propDef.PropertyDefinition.Name = propDef.NewName;
				Log.v("Property: {0} ({1:X8}) => {2}", propDef.OldFullName, propDef.PropertyDefinition.MetadataToken.ToUInt32(), propDef.PropertyDefinition.FullName);
			}
		}

		void renameEvents() {
			foreach (var eventDef in events.getSorted()) {
				if (!eventDef.gotNewName())
					continue;
				eventDef.EventDefinition.Name = eventDef.NewName;
				Log.v("Event: {0} ({1:X8}) => {2}", eventDef.OldFullName, eventDef.EventDefinition.MetadataToken.ToUInt32(), eventDef.EventDefinition.FullName);
			}
		}

		void renameMethods() {
			foreach (var methodDef in methods.getSorted()) {
				Log.v("Method {0} ({1:X8})", methodDef.OldFullName, methodDef.MethodDefinition.MetadataToken.ToUInt32());
				Log.indent();

				renameGenericParams(methodDef.GenericParams);

				if (methodDef.gotNewName()) {
					methodDef.MethodReference.Name = methodDef.NewName;
					Log.v("Name: {0} => {1}", methodDef.OldFullName, methodDef.MethodReference.FullName);
				}

				foreach (var param in methodDef.ParamDefs) {
					if (!param.gotNewName())
						continue;
					param.ParameterDefinition.Name = param.NewName;
					Log.v("Param ({0}/{1}): {2} => {3}", param.Index + 1, methodDef.ParamDefs.Count, param.OldName, param.NewName);
				}

				Log.deIndent();
			}
		}

		public void prepareRenameMembers() {
			if (prepareRenameMembersCalled)
				return;
			prepareRenameMembersCalled = true;

			foreach (var ifaceInfo in interfaces)
				ifaceInfo.typeDef.prepareRenameMembers();
			if (baseType != null)
				baseType.typeDef.prepareRenameMembers();

			if (baseType != null)
				MemberRenameState = baseType.typeDef.MemberRenameState.clone();
			MemberRenameState.variableNameState.IsValidName = module.IsValidName;

			if (InterfaceScopeState != null)
				MemberRenameState.mergeRenamed(InterfaceScopeState);

			expandGenerics();

			prepareRenameFields();		// must be first
			prepareRenameProperties();
			prepareRenameEvents();
			prepareRenameMethods();		// must be last
		}

		// Replaces the generic params with the generic args, if any
		void expandGenerics() {
			foreach (var typeInfo in getTypeInfos()) {
				var git = typeInfo.typeReference as GenericInstanceType;
				if (git == null)
					continue;

				if (git.GenericArguments.Count != typeInfo.typeDef.TypeDefinition.GenericParameters.Count) {
					throw new ApplicationException(string.Format("# args ({0}) != # params ({1})",
							git.GenericArguments.Count,
							typeInfo.typeDef.TypeDefinition.GenericParameters.Count));
				}
				expandProperties(git);
				expandEvents(git);
				expandMethods(git);
			}
		}

		IEnumerable<TypeInfo> getTypeInfos() {
			if (baseType != null)
				yield return baseType;
			foreach (var typeInfo in interfaces)
				yield return typeInfo;
		}

		void expandProperties(GenericInstanceType git) {
			foreach (var propRef in new List<PropertyRef>(MemberRenameState.properties.Values)) {
				var newPropRef = new GenericPropertyRefExpander(propRef, git).expand();
				if (ReferenceEquals(newPropRef, propRef))
					continue;
				MemberRenameState.add(newPropRef);
			}
		}

		void expandEvents(GenericInstanceType git) {
			foreach (var eventRef in new List<EventRef>(MemberRenameState.events.Values)) {
				var newEventRef = new GenericEventRefExpander(eventRef, git).expand();
				if (ReferenceEquals(eventRef, newEventRef))
					continue;
				MemberRenameState.add(newEventRef);
			}
		}

		void expandMethods(GenericInstanceType git) {
			foreach (var methodRef in new List<MethodRef>(MemberRenameState.methods.Values)) {
				var newMethodRef = new GenericMethodRefExpander(methodRef, git).expand();
				if (ReferenceEquals(methodRef, newMethodRef))
					continue;
				MemberRenameState.add(newMethodRef);
			}
		}

		bool hasFlagsAttribute() {
			if (TypeDefinition.CustomAttributes != null) {
				foreach (var attr in TypeDefinition.CustomAttributes) {
					if (MemberReferenceHelper.verifyType(attr.AttributeType, "mscorlib", "System.FlagsAttribute"))
						return true;
				}
			}
			return false;
		}

		void prepareRenameFields() {
			var variableNameState = MemberRenameState.variableNameState;

			if (TypeDefinition.IsEnum) {
				var instanceFields = new List<FieldDef>(getInstanceFields());
				if (instanceFields.Count == 1) {
					var fieldDef = instanceFields[0];
					if (fieldDef.rename("value__")) {
						fieldDef.FieldDefinition.IsRuntimeSpecialName = true;
						fieldDef.FieldDefinition.IsSpecialName = true;
					}
				}

				int i = 0;
				string nameFormat = hasFlagsAttribute() ? "flag_{0}" : "const_{0}";
				foreach (var fieldDef in fields.getSorted()) {
					if (!fieldDef.FieldDefinition.IsStatic || !fieldDef.FieldDefinition.IsLiteral)
						continue;
					if (!variableNameState.IsValidName(fieldDef.OldName))
						fieldDef.rename(string.Format(nameFormat, i));
					i++;
				}
			}
			foreach (var fieldDef in fields.getSorted()) {
				if (fieldDef.Renamed)
					continue;
				if (!variableNameState.IsValidName(fieldDef.OldName))
					fieldDef.rename(variableNameState.getNewFieldName(fieldDef.FieldDefinition));
			}
		}

		static MethodReference getOverrideMethod(MethodDefinition meth) {
			if (meth == null || !meth.HasOverrides)
				return null;
			return meth.Overrides[0];
		}

		static string getRealName(string name) {
			int index = name.LastIndexOf('.');
			if (index < 0)
				return name;
			return name.Substring(index + 1);
		}

		static readonly Regex removeGenericsArityRegex = new Regex(@"`[0-9]+");
		static string getOverrideMethodNamePrefix(TypeReference owner) {
			var name = owner.FullName.Replace('/', '.');
			name = removeGenericsArityRegex.Replace(name, "");
			return name + ".";
		}

		static string getOverrideMethodName(TypeReference owner, string name) {
			return getOverrideMethodNamePrefix(owner) + name;
		}

		void prepareRenameProperties() {
			var variableNameState = MemberRenameState.variableNameState;

			foreach (var propDef in properties.getSorted()) {
				if (propDef.Renamed)
					continue;
				propDef.Renamed = true;

				bool isVirtual = propDef.isVirtual();
				string prefix = "";
				string baseName = propDef.OldName;

				string propName = null;
				if (isVirtual)
					getVirtualPropName(propDef, ref prefix, ref propName);
				if (propName == null && !variableNameState.IsValidName(propDef.OldName))
					propName = variableNameState.getNewPropertyName(propDef.PropertyDefinition);
				if (propName != null) {
					baseName = propName;
					propDef.NewName = prefix + baseName;
				}

				renameSpecialMethod(propDef.PropertyDefinition.GetMethod, prefix + "get_" + baseName);
				renameSpecialMethod(propDef.PropertyDefinition.SetMethod, prefix + "set_" + baseName, "value");

				if (isVirtual)
					MemberRenameState.add(propDef);
			}
		}

		void getVirtualPropName(PropertyDef propDef, ref string prefix, ref string propName) {
			PropertyRef sameDef;
			var overrideMethod = propDef.getOverrideMethod();
			if (overrideMethod != null && (sameDef = defFinder.findProp(overrideMethod)) != null) {
				prefix = getOverrideMethodNamePrefix(sameDef.Owner.TypeDefinition);
				propName = sameDef.NewName;
				return;
			}

			var method = getOverrideMethod(propDef.PropertyDefinition.GetMethod ?? propDef.PropertyDefinition.SetMethod);
			if (method != null) {
				var realName = getRealName(method.Name);
				// Only use the name if the method is not in one of the loaded files, since the
				// name shouldn't be obfuscated.
				if (Regex.IsMatch(realName, @"^[sg]et_.") && defFinder.findProp(method) == null) {
					prefix = getOverrideMethodNamePrefix(method.DeclaringType);
					propName = realName.Substring(4);
					return;
				}
			}

			sameDef = MemberRenameState.get(propDef);
			if (sameDef != null) {
				prefix = "";
				propName = sameDef.NewName;
				return;
			}
		}

		void prepareRenameEvents() {
			var variableNameState = MemberRenameState.variableNameState;

			foreach (var eventDef in events.getSorted()) {
				if (eventDef.Renamed)
					continue;
				eventDef.Renamed = true;

				bool isVirtual = eventDef.isVirtual();
				string prefix = "";
				string baseName = eventDef.OldName;

				string propName = null;
				if (isVirtual)
					getVirtualEventName(eventDef, ref prefix, ref propName);
				if (propName == null && !variableNameState.IsValidName(eventDef.OldName))
					propName = variableNameState.getNewEventName(eventDef.EventDefinition);
				if (propName != null) {
					baseName = propName;
					eventDef.NewName = prefix + baseName;
				}

				renameSpecialMethod(eventDef.EventDefinition.AddMethod, prefix + "add_" + baseName, "value");
				renameSpecialMethod(eventDef.EventDefinition.RemoveMethod, prefix + "remove_" + baseName, "value");
				renameSpecialMethod(eventDef.EventDefinition.InvokeMethod, prefix + "raise_" + baseName);

				if (isVirtual)
					MemberRenameState.add(eventDef);
			}
		}

		void getVirtualEventName(EventDef eventDef, ref string prefix, ref string propName) {
			EventRef sameDef;
			var overrideMethod = eventDef.getOverrideMethod();
			if (overrideMethod != null && (sameDef = defFinder.findEvent(overrideMethod)) != null) {
				prefix = getOverrideMethodNamePrefix(sameDef.Owner.TypeDefinition);
				propName = sameDef.NewName;
				return;
			}

			var method = getOverrideMethod(eventDef.EventDefinition.AddMethod ?? eventDef.EventDefinition.RemoveMethod);
			if (method != null) {
				var realName = getRealName(method.Name);
				// Only use the name if the method is not in one of the loaded files, since the
				// name shouldn't be obfuscated.
				if (Regex.IsMatch(realName, @"^(add|remove)_.") && defFinder.findEvent(method) == null) {
					prefix = getOverrideMethodNamePrefix(method.DeclaringType);
					propName = realName.Substring(realName.IndexOf('_') + 1);
					return;
				}
			}

			sameDef = MemberRenameState.get(eventDef);
			if (sameDef != null) {
				prefix = "";
				propName = sameDef.NewName;
				return;
			}
		}

		void renameSpecialMethod(MethodDefinition methodDefinition, string newName, string newArgName = null) {
			if (methodDefinition == null)
				return;

			var methodDef = find(methodDefinition);
			if (methodDef == null)
				throw new ApplicationException("Could not find the event/prop method");

			renameMethod(methodDef, newName);

			if (newArgName != null && methodDef.ParamDefs.Count > 0) {
				var arg = methodDef.ParamDefs[methodDef.ParamDefs.Count - 1];
				if (!MemberRenameState.variableNameState.IsValidName(arg.OldName)) {
					arg.NewName = newArgName;
					arg.Renamed = true;
				}
			}
		}

		void prepareRenameMethods() {
			foreach (var methodDef in methods.getSorted())
				renameMethod(methodDef);
		}

		void renameMethod(MethodDef methodDef, string suggestedName = null) {
			if (methodDef.Renamed)
				return;
			methodDef.Renamed = true;
			var variableNameState = MemberRenameState.variableNameState;
			bool isVirtual = methodDef.isVirtual();

			var nameCreator = getMethodNameCreator(methodDef, suggestedName);

			if (!methodDef.MethodDefinition.IsRuntimeSpecialName && !variableNameState.IsValidName(methodDef.OldName))
				methodDef.NewName = nameCreator.newName();

			if (methodDef.ParamDefs.Count > 0) {
				var newVariableNameState = variableNameState.clone();
				foreach (var paramDef in methodDef.ParamDefs) {
					if (!newVariableNameState.IsValidName(paramDef.OldName)) {
						paramDef.NewName = newVariableNameState.getNewParamName(paramDef.ParameterDefinition);
						paramDef.Renamed = true;
					}
				}
			}

			prepareRenameGenericParams(methodDef.GenericParams, variableNameState.IsValidName, methodDef.Owner == null ? null : methodDef.Owner.genericParams);

			if (isVirtual)
				MemberRenameState.add(methodDef);
		}

		string getPinvokeName(MethodDef methodDef) {
			var methodNames = new Dictionary<string, bool>(StringComparer.Ordinal);
			foreach (var method in methods.getAll())
				methodNames[method.NewName] = true;

			if (methodDef.MethodDefinition.PInvokeInfo == null)
				throw new ApplicationException(string.Format("PInvokeInfo is null: A type was probably removed but still referenced by the code."));
			var entryPoint = methodDef.MethodDefinition.PInvokeInfo.EntryPoint;
			if (Regex.IsMatch(entryPoint, @"^#\d+$"))
				entryPoint = DotNetUtils.getDllName(methodDef.MethodDefinition.PInvokeInfo.Module.Name) + "_" + entryPoint.Substring(1);
			while (true) {
				var newName = MemberRenameState.variableNameState.pinvokeNameCreator.newName(entryPoint);
				if (!methodNames.ContainsKey(newName))
					return newName;
			}
		}

		INameCreator getMethodNameCreator(MethodDef methodDef, string suggestedName) {
			var variableNameState = MemberRenameState.variableNameState;
			INameCreator nameCreator = null;
			string newName = null;

			if (methodDef.MethodDefinition.HasPInvokeInfo)
				newName = getPinvokeName(methodDef);
			else if (methodDef.MethodDefinition.IsStatic)
				nameCreator = variableNameState.staticMethodNameCreator;
			else if (methodDef.isVirtual()) {
				MethodRef otherMethodRef;
				if ((otherMethodRef = MemberRenameState.get(methodDef)) != null)
					newName = otherMethodRef.NewName;
				else if (methodDef.MethodDefinition.HasOverrides) {
					var overrideMethod = methodDef.MethodDefinition.Overrides[0];
					var otherMethodDef = defFinder.findMethod(overrideMethod);
					if (otherMethodDef != null)
						newName = getOverrideMethodName(overrideMethod.DeclaringType, otherMethodDef.NewName);
					else
						newName = getOverrideMethodName(overrideMethod.DeclaringType, overrideMethod.Name);
				}
				else
					nameCreator = variableNameState.virtualMethodNameCreator;
			}
			else
				nameCreator = variableNameState.instanceMethodNameCreator;

			if (newName == null)
				newName = suggestedName;
			if (newName != null)
				nameCreator = new OneNameCreator(newName);

			return nameCreator;
		}
	}
}
