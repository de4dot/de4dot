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
using de4dot.blocks;

namespace de4dot.code.renamer.asmmodules {
	class TypeInfo {
		public TypeReference typeReference;
		public TypeDef typeDef;
		public TypeInfo(TypeReference typeReference, TypeDef typeDef) {
			this.typeReference = typeReference;
			this.typeDef = typeDef;
		}

		public TypeInfo(TypeInfo other, GenericInstanceType git) {
			this.typeReference = TypeReferenceInstance.make(other.typeReference, git);
			this.typeDef = other.typeDef;
		}

		public override int GetHashCode() {
			return typeDef.GetHashCode() +
					MemberReferenceHelper.typeHashCode(typeReference);
		}

		public override bool Equals(object obj) {
			var other = obj as TypeInfo;
			if (other == null)
				return false;
			return typeDef == other.typeDef &&
				MemberReferenceHelper.compareTypes(typeReference, other.typeReference);
		}

		public override string ToString() {
			return typeReference.ToString();
		}
	}

	class MethodDefKey {
		public readonly MethodDef methodDef;

		public MethodDefKey(MethodDef methodDef) {
			this.methodDef = methodDef;
		}

		public override int GetHashCode() {
			return MemberReferenceHelper.methodReferenceAndDeclaringTypeHashCode(methodDef.MethodDefinition);
		}

		public override bool Equals(object obj) {
			var other = obj as MethodDefKey;
			if (other == null)
				return false;
			return MemberReferenceHelper.compareMethodReferenceAndDeclaringType(methodDef.MethodDefinition, other.methodDef.MethodDefinition);
		}
	}

	class MethodInst {
		public MethodDef origMethodDef;
		public MethodReference methodReference;

		public MethodInst(MethodDef origMethodDef, MethodReference methodReference) {
			this.origMethodDef = origMethodDef;
			this.methodReference = methodReference;
		}

		public override string ToString() {
			return methodReference.ToString();
		}
	}

	class MethodInstances {
		Dictionary<MethodReferenceKey, List<MethodInst>> methodInstances = new Dictionary<MethodReferenceKey, List<MethodInst>>();

		public void initializeFrom(MethodInstances other, GenericInstanceType git) {
			foreach (var list in other.methodInstances.Values) {
				foreach (var methodInst in list) {
					MethodReference newMethod = MethodReferenceInstance.make(methodInst.methodReference, git);
					add(new MethodInst(methodInst.origMethodDef, newMethod));
				}
			}
		}

		public void add(MethodInst methodInst) {
			List<MethodInst> list;
			var key = new MethodReferenceKey(methodInst.methodReference);
			if (methodInst.origMethodDef.isNewSlot() || !methodInstances.TryGetValue(key, out list))
				methodInstances[key] = list = new List<MethodInst>();
			list.Add(methodInst);
		}

		public List<MethodInst> lookup(MethodReference methodReference) {
			List<MethodInst> list;
			methodInstances.TryGetValue(new MethodReferenceKey(methodReference), out list);
			return list;
		}

		public IEnumerable<List<MethodInst>> getMethods() {
			return methodInstances.Values;
		}
	}

	// Keeps track of which methods of an interface that have been implemented
	class InterfaceMethodInfo {
		TypeInfo iface;
		Dictionary<MethodDefKey, MethodDef> ifaceMethodToClassMethod = new Dictionary<MethodDefKey, MethodDef>();

		public TypeInfo IFace {
			get { return iface; }
		}

		public Dictionary<MethodDefKey, MethodDef> IfaceMethodToClassMethod {
			get { return ifaceMethodToClassMethod; }
		}

		public InterfaceMethodInfo(TypeInfo iface) {
			this.iface = iface;
			foreach (var methodDef in iface.typeDef.AllMethods)
				ifaceMethodToClassMethod[new MethodDefKey(methodDef)] = null;
		}

		public InterfaceMethodInfo(TypeInfo iface, InterfaceMethodInfo other) {
			this.iface = iface;
			foreach (var key in other.ifaceMethodToClassMethod.Keys)
				ifaceMethodToClassMethod[key] = other.ifaceMethodToClassMethod[key];
		}

		public void merge(InterfaceMethodInfo other) {
			foreach (var key in other.ifaceMethodToClassMethod.Keys) {
				if (other.ifaceMethodToClassMethod[key] == null)
					continue;
				if (ifaceMethodToClassMethod[key] != null)
					throw new ApplicationException("Interface method already initialized");
				ifaceMethodToClassMethod[key] = other.ifaceMethodToClassMethod[key];
			}
		}

		// Returns the previous method, or null if none
		public MethodDef addMethod(MethodDef ifaceMethod, MethodDef classMethod) {
			var ifaceKey = new MethodDefKey(ifaceMethod);
			if (!ifaceMethodToClassMethod.ContainsKey(ifaceKey))
				throw new ApplicationException("Could not find interface method");

			MethodDef oldMethod;
			ifaceMethodToClassMethod.TryGetValue(ifaceKey, out oldMethod);
			ifaceMethodToClassMethod[ifaceKey] = classMethod;
			return oldMethod;
		}

		public void addMethodIfEmpty(MethodDef ifaceMethod, MethodDef classMethod) {
			if (ifaceMethodToClassMethod[new MethodDefKey(ifaceMethod)] == null)
				addMethod(ifaceMethod, classMethod);
		}

		public override string ToString() {
			return iface.ToString();
		}
	}

	class InterfaceMethodInfos {
		Dictionary<TypeReferenceKey, InterfaceMethodInfo> interfaceMethods = new Dictionary<TypeReferenceKey, InterfaceMethodInfo>();

		public IEnumerable<InterfaceMethodInfo> AllInfos {
			get { return interfaceMethods.Values; }
		}

		public void initializeFrom(InterfaceMethodInfos other, GenericInstanceType git) {
			foreach (var pair in other.interfaceMethods) {
				var oldTypeInfo = pair.Value.IFace;
				var newTypeInfo = new TypeInfo(oldTypeInfo, git);
				var oldKey = new TypeReferenceKey(oldTypeInfo.typeReference);
				var newKey = new TypeReferenceKey(newTypeInfo.typeReference);

				InterfaceMethodInfo newMethodsInfo = new InterfaceMethodInfo(newTypeInfo, other.interfaceMethods[oldKey]);
				if (interfaceMethods.ContainsKey(newKey))
					newMethodsInfo.merge(interfaceMethods[newKey]);
				interfaceMethods[newKey] = newMethodsInfo;
			}
		}

		public void addInterface(TypeInfo iface) {
			var key = new TypeReferenceKey(iface.typeReference);
			if (!interfaceMethods.ContainsKey(key))
				interfaceMethods[key] = new InterfaceMethodInfo(iface);
		}

		// Returns the previous classMethod, or null if none
		public MethodDef addMethod(TypeInfo iface, MethodDef ifaceMethod, MethodDef classMethod) {
			return addMethod(iface.typeReference, ifaceMethod, classMethod);
		}

		// Returns the previous classMethod, or null if none
		public MethodDef addMethod(TypeReference iface, MethodDef ifaceMethod, MethodDef classMethod) {
			InterfaceMethodInfo info;
			var key = new TypeReferenceKey(iface);
			if (!interfaceMethods.TryGetValue(key, out info))
				throw new ApplicationException("Could not find interface");
			return info.addMethod(ifaceMethod, classMethod);
		}

		public void addMethodIfEmpty(TypeInfo iface, MethodDef ifaceMethod, MethodDef classMethod) {
			InterfaceMethodInfo info;
			var key = new TypeReferenceKey(iface.typeReference);
			if (!interfaceMethods.TryGetValue(key, out info))
				throw new ApplicationException("Could not find interface");
			info.addMethodIfEmpty(ifaceMethod, classMethod);
		}
	}

	class TypeDef : Ref {
		EventDefDict events = new EventDefDict();
		FieldDefDict fields = new FieldDefDict();
		MethodDefDict methods = new MethodDefDict();
		PropertyDefDict properties = new PropertyDefDict();
		TypeDefDict types = new TypeDefDict();
		List<GenericParamDef> genericParams;
		internal TypeInfo baseType = null;
		internal IList<TypeInfo> interfaces = new List<TypeInfo>();	// directly implemented interfaces
		internal IList<TypeDef> derivedTypes = new List<TypeDef>();
		Module module;

		bool initializeVirtualMembersCalled = false;
		MethodInstances virtualMethodInstances = new MethodInstances();
		Dictionary<TypeInfo, bool> allImplementedInterfaces = new Dictionary<TypeInfo, bool>();
		InterfaceMethodInfos interfaceMethodInfos = new InterfaceMethodInfos();

		public Module Module {
			get { return module; }
		}

		// Returns false if this is a type from a dependency (non-renamble) assembly (eg. mscorlib)
		public bool HasModule {
			get { return module != null; }
		}

		public IList<GenericParamDef> GenericParams {
			get { return genericParams; }
		}

		public IEnumerable<TypeDef> NestedTypes {
			get { return types.getSorted(); }
		}

		public TypeDef NestingType { get; set; }

		public TypeDefinition TypeDefinition {
			get { return (TypeDefinition)memberReference; }
		}

		public IEnumerable<EventDef> AllEvents {
			get { return events.getValues(); }
		}

		public IEnumerable<FieldDef> AllFields {
			get { return fields.getValues(); }
		}

		public IEnumerable<MethodDef> AllMethods {
			get { return methods.getValues(); }
		}

		public IEnumerable<PropertyDef> AllProperties {
			get { return properties.getValues(); }
		}

		public IEnumerable<EventDef> AllEventsSorted {
			get { return events.getSorted(); }
		}

		public IEnumerable<FieldDef> AllFieldsSorted {
			get { return fields.getSorted(); }
		}

		public IEnumerable<MethodDef> AllMethodsSorted {
			get { return methods.getSorted(); }
		}

		public IEnumerable<PropertyDef> AllPropertiesSorted {
			get { return properties.getSorted(); }
		}

		public TypeDef(TypeDefinition typeDefinition, Module module, int index)
			: base(typeDefinition, null, index) {
			this.module = module;
			genericParams = GenericParamDef.createGenericParamDefList(TypeDefinition.GenericParameters);
		}

		public void addInterface(TypeDef ifaceDef, TypeReference iface) {
			if (ifaceDef == null || iface == null)
				return;
			interfaces.Add(new TypeInfo(iface, ifaceDef));
		}

		public void addBaseType(TypeDef baseDef, TypeReference baseRef) {
			if (baseDef == null || baseRef == null)
				return;
			baseType = new TypeInfo(baseRef, baseDef);
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

		public MethodDef findAny(MethodReference mr) {
			return methods.findAny(mr);
		}

		public FieldDef find(FieldReference fr) {
			return fields.find(fr);
		}

		public FieldDef findAny(FieldReference fr) {
			return fields.findAny(fr);
		}

		public PropertyDef find(PropertyReference pr) {
			return properties.find(pr);
		}

		public PropertyDef findAny(PropertyReference pr) {
			return properties.findAny(pr);
		}

		public EventDef find(EventReference er) {
			return events.find(er);
		}

		public EventDef findAny(EventReference er) {
			return events.findAny(er);
		}

		public PropertyDef create(PropertyDefinition newProp) {
			if (findAny(newProp) != null)
				throw new ApplicationException("Can't add a property when it's already been added");

			var propDef = new PropertyDef(newProp, this, properties.Count);
			add(propDef);
			TypeDefinition.Properties.Add(newProp);
			return propDef;
		}

		public EventDef create(EventDefinition newEvent) {
			if (findAny(newEvent) != null)
				throw new ApplicationException("Can't add an event when it's already been added");

			var eventDef = new EventDef(newEvent, this, events.Count);
			add(eventDef);
			TypeDefinition.Events.Add(newEvent);
			return eventDef;
		}

		public void addMembers() {
			var type = TypeDefinition;

			for (int i = 0; i < type.Events.Count; i++)
				add(new EventDef(type.Events[i], this, i));
			for (int i = 0; i < type.Fields.Count; i++)
				add(new FieldDef(type.Fields[i], this, i));
			for (int i = 0; i < type.Methods.Count; i++)
				add(new MethodDef(type.Methods[i], this, i));
			for (int i = 0; i < type.Properties.Count; i++)
				add(new PropertyDef(type.Properties[i], this, i));

			foreach (var propDef in properties.getValues()) {
				foreach (var method in propDef.methodDefinitions()) {
					var methodDef = find(method);
					if (methodDef == null)
						throw new ApplicationException("Could not find property method");
					methodDef.Property = propDef;
					if (method == propDef.PropertyDefinition.GetMethod)
						propDef.GetMethod = methodDef;
					if (method == propDef.PropertyDefinition.SetMethod)
						propDef.SetMethod = methodDef;
				}
			}

			foreach (var eventDef in events.getValues()) {
				foreach (var method in eventDef.methodDefinitions()) {
					var methodDef = find(method);
					if (methodDef == null)
						throw new ApplicationException("Could not find event method");
					methodDef.Event = eventDef;
					if (method == eventDef.EventDefinition.AddMethod)
						eventDef.AddMethod = methodDef;
					if (method == eventDef.EventDefinition.RemoveMethod)
						eventDef.RemoveMethod = methodDef;
					if (method == eventDef.EventDefinition.InvokeMethod)
						eventDef.RaiseMethod = methodDef;
				}
			}
		}

		public void onTypesRenamed() {
			events.onTypesRenamed();
			properties.onTypesRenamed();
			fields.onTypesRenamed();
			methods.onTypesRenamed();
			types.onTypesRenamed();
		}

		public bool isNested() {
			return NestingType != null;
		}

		public bool isGlobalType() {
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

		public void initializeVirtualMembers(MethodNameGroups groups, IResolver resolver) {
			if (initializeVirtualMembersCalled)
				return;
			initializeVirtualMembersCalled = true;

			foreach (var iface in interfaces)
				iface.typeDef.initializeVirtualMembers(groups, resolver);
			if (baseType != null)
				baseType.typeDef.initializeVirtualMembers(groups, resolver);

			foreach (var methodDef in methods.getValues()) {
				if (methodDef.isVirtual())
					groups.add(methodDef);
			}

			instantiateVirtualMembers(groups);
			initializeInterfaceMethods(groups);
		}

		void initializeAllInterfaces() {
			if (baseType != null)
				initializeInterfaces(baseType);

			foreach (var iface in interfaces) {
				allImplementedInterfaces[iface] = true;
				interfaceMethodInfos.addInterface(iface);
				initializeInterfaces(iface);
			}
		}

		void initializeInterfaces(TypeInfo typeInfo) {
			var git = typeInfo.typeReference as GenericInstanceType;
			interfaceMethodInfos.initializeFrom(typeInfo.typeDef.interfaceMethodInfos, git);
			foreach (var info in typeInfo.typeDef.allImplementedInterfaces.Keys) {
				var newTypeInfo = new TypeInfo(info, git);
				allImplementedInterfaces[newTypeInfo] = true;
			}
		}

		void initializeInterfaceMethods(MethodNameGroups groups) {
			initializeAllInterfaces();

			if (TypeDefinition.IsInterface)
				return;

			//--- Partition II 12.2 Implementing virtual methods on interfaces:
			//--- The VES shall use the following algorithm to determine the appropriate
			//--- implementation of an interface's virtual abstract methods:
			//---
			//--- * If the base class implements the interface, start with the same virtual methods
			//---	that it provides; otherwise, create an interface that has empty slots for all
			//---	virtual functions.
			// Done. See initializeAllInterfaces().

			var methodsDict = new Dictionary<MethodReferenceKey, MethodDef>();

			//--- * If this class explicitly specifies that it implements the interface (i.e., the
			//---	interfaces that appear in this class‘ InterfaceImpl table, §22.23)
			//---	* If the class defines any public virtual newslot methods whose name and
			//---	  signature match a virtual method on the interface, then use these new virtual
			//---	  methods to implement the corresponding interface method.
			if (interfaces.Count > 0) {
				methodsDict.Clear();
				foreach (var method in methods.getValues()) {
					if (!method.isPublic() || !method.isVirtual() || !method.isNewSlot())
						continue;
					methodsDict[new MethodReferenceKey(method.MethodDefinition)] = method;
				}

				foreach (var ifaceInfo in interfaces) {
					foreach (var methodsList in ifaceInfo.typeDef.virtualMethodInstances.getMethods()) {
						if (methodsList.Count != 1)	// Never happens
							throw new ApplicationException("Interface with more than one method in the list");
						var methodInst = methodsList[0];
						var ifaceMethod = methodInst.origMethodDef;
						if (!ifaceMethod.isVirtual())
							continue;
						var ifaceMethodReference = MethodReferenceInstance.make(methodInst.methodReference, ifaceInfo.typeReference as GenericInstanceType);
						MethodDef classMethod;
						var key = new MethodReferenceKey(ifaceMethodReference);
						if (!methodsDict.TryGetValue(key, out classMethod))
							continue;
						interfaceMethodInfos.addMethod(ifaceInfo, ifaceMethod, classMethod);
					}
				}
			}

			//--- * If there are any virtual methods in the interface that still have empty slots,
			//---	see if there are any public virtual methods, but not public virtual newslot
			//---	methods, available on this class (directly or inherited) having the same name
			//---	and signature, then use these to implement the corresponding methods on the
			//---	interface.
			methodsDict.Clear();
			foreach (var methodInstList in virtualMethodInstances.getMethods()) {
				// This class' method is at the end
				for (int i = methodInstList.Count - 1; i >= 0; i--) {
					var classMethod = methodInstList[i];
					// These methods are guaranteed to be virtual.
					// We should allow newslot methods, despite what the official doc says.
					if (!classMethod.origMethodDef.isPublic())
						continue;
					methodsDict[new MethodReferenceKey(classMethod.methodReference)] = classMethod.origMethodDef;
					break;
				}
			}
			foreach (var ifaceInfo in allImplementedInterfaces.Keys) {
				foreach (var methodsList in ifaceInfo.typeDef.virtualMethodInstances.getMethods()) {
					if (methodsList.Count != 1)	// Never happens
						throw new ApplicationException("Interface with more than one method in the list");
					var ifaceMethod = methodsList[0].origMethodDef;
					if (!ifaceMethod.isVirtual())
						continue;
					var ifaceMethodRef = MethodReferenceInstance.make(ifaceMethod.MethodDefinition, ifaceInfo.typeReference as GenericInstanceType);
					MethodDef classMethod;
					var key = new MethodReferenceKey(ifaceMethodRef);
					if (!methodsDict.TryGetValue(key, out classMethod))
						continue;
					interfaceMethodInfos.addMethodIfEmpty(ifaceInfo, ifaceMethod, classMethod);
				}
			}

			//--- * Apply all MethodImpls that are specified for this class, thereby placing
			//---	explicitly specified virtual methods into the interface in preference to those
			//---	inherited or chosen by name matching.
			methodsDict.Clear();
			var ifaceMethodsDict = new Dictionary<MethodReferenceAndDeclaringTypeKey, MethodDef>();
			foreach (var ifaceInfo in allImplementedInterfaces.Keys) {
				var git = ifaceInfo.typeReference as GenericInstanceType;
				foreach (var ifaceMethod in ifaceInfo.typeDef.methods.getValues()) {
					MethodReference ifaceMethodReference = ifaceMethod.MethodDefinition;
					if (git != null)
						ifaceMethodReference = simpleClone(ifaceMethod.MethodDefinition, git);
					ifaceMethodsDict[new MethodReferenceAndDeclaringTypeKey(ifaceMethodReference)] = ifaceMethod;
				}
			}
			foreach (var classMethod in methods.getValues()) {
				if (!classMethod.isVirtual())
					continue;
				foreach (var overrideMethod in classMethod.MethodDefinition.Overrides) {
					MethodDef ifaceMethod;
					var key = new MethodReferenceAndDeclaringTypeKey(overrideMethod);
					if (!ifaceMethodsDict.TryGetValue(key, out ifaceMethod)) {
						// We couldn't find the interface method (eg. interface not resolved) or
						// it overrides a base class method, and not an interface method.
						continue;
					}

					interfaceMethodInfos.addMethod(overrideMethod.DeclaringType, ifaceMethod, classMethod);
				}
			}

			//--- * If the current class is not abstract and there are any interface methods that
			//---	still have empty slots, then the program is invalid.
			// Check it anyway. C# requires a method, even if it's abstract. I don't think anyone
			// writes pure CIL assemblies.
			foreach (var info in interfaceMethodInfos.AllInfos) {
				foreach (var pair in info.IfaceMethodToClassMethod) {
					if (pair.Value != null)
						continue;
					if (!resolvedAllInterfaces() || !resolvedBaseClasses())
						continue;
					// Ignore if COM class
					if (!TypeDefinition.IsImport &&
						!hasAttribute("System.Runtime.InteropServices.ComImportAttribute") &&
						!hasAttribute("System.Runtime.InteropServices.TypeLibTypeAttribute")) {
						Log.w("Could not find interface method {0} ({1:X8}). Type: {2} ({3:X8})",
								Utils.removeNewlines(pair.Key.methodDef.MethodDefinition),
								pair.Key.methodDef.MethodDefinition.MetadataToken.ToInt32(),
								Utils.removeNewlines(TypeDefinition),
								TypeDefinition.MetadataToken.ToInt32());
					}
				}
			}

			foreach (var info in interfaceMethodInfos.AllInfos) {
				foreach (var pair in info.IfaceMethodToClassMethod) {
					if (pair.Value == null)
						continue;
					if (pair.Key.methodDef.MethodDefinition.Name != pair.Value.MethodDefinition.Name)
						continue;
					groups.same(pair.Key.methodDef, pair.Value);
				}
			}
		}

		bool hasAttribute(string name) {
			foreach (var attr in TypeDefinition.CustomAttributes) {
				if (attr.AttributeType.FullName == name)
					return true;
			}
			return false;
		}

		// Returns true if all interfaces have been resolved
		bool? resolvedAllInterfacesResult;
		bool resolvedAllInterfaces() {
			if (!resolvedAllInterfacesResult.HasValue) {
				resolvedAllInterfacesResult = true;	// If we find a circular reference
				resolvedAllInterfacesResult = resolvedAllInterfacesInternal();
			}
			return resolvedAllInterfacesResult.Value;
		}
		bool resolvedAllInterfacesInternal() {
			if (TypeDefinition.Interfaces.Count != interfaces.Count)
				return false;
			foreach (var ifaceInfo in interfaces) {
				if (!ifaceInfo.typeDef.resolvedAllInterfaces())
					return false;
			}
			return true;
		}

		// Returns true if all base classes have been resolved
		bool? resolvedBaseClassesResult;
		bool resolvedBaseClasses() {
			if (!resolvedBaseClassesResult.HasValue) {
				resolvedBaseClassesResult = true;	// If we find a circular reference
				resolvedBaseClassesResult = resolvedBaseClassesInternal();
			}
			return resolvedBaseClassesResult.Value;
		}
		bool resolvedBaseClassesInternal() {
			if (TypeDefinition.BaseType == null)
				return true;
			if (baseType == null)
				return false;
			return baseType.typeDef.resolvedBaseClasses();
		}

		MethodReference simpleClone(MethodReference methodReference, TypeReference declaringType) {
			var m = new MethodReference(methodReference.Name, methodReference.MethodReturnType.ReturnType, declaringType);
			m.MethodReturnType.ReturnType = methodReference.MethodReturnType.ReturnType;
			m.HasThis = methodReference.HasThis;
			m.ExplicitThis = methodReference.ExplicitThis;
			m.CallingConvention = methodReference.CallingConvention;
			foreach (var p in methodReference.Parameters)
				m.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
			foreach (var gp in methodReference.GenericParameters)
				m.GenericParameters.Add(new GenericParameter(declaringType));
			return m;
		}

		void instantiateVirtualMembers(MethodNameGroups groups) {
			if (!TypeDefinition.IsInterface) {
				if (baseType != null)
					virtualMethodInstances.initializeFrom(baseType.typeDef.virtualMethodInstances, baseType.typeReference as GenericInstanceType);

				// Figure out which methods we override in the base class
				foreach (var methodDef in methods.getValues()) {
					if (!methodDef.isVirtual() || methodDef.isNewSlot())
						continue;
					var methodInstList = virtualMethodInstances.lookup(methodDef.MethodDefinition);
					if (methodInstList == null)
						continue;
					foreach (var methodInst in methodInstList)
						groups.same(methodDef, methodInst.origMethodDef);
				}
			}

			foreach (var methodDef in methods.getValues()) {
				if (!methodDef.isVirtual())
					continue;
				virtualMethodInstances.add(new MethodInst(methodDef, methodDef.MethodDefinition));
			}
		}
	}
}
