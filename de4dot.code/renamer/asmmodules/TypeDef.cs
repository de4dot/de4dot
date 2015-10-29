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
using de4dot.blocks;

namespace de4dot.code.renamer.asmmodules {
	public class TypeInfo {
		public ITypeDefOrRef typeRef;
		public MTypeDef typeDef;
		public TypeInfo(ITypeDefOrRef typeRef, MTypeDef typeDef) {
			this.typeRef = typeRef;
			this.typeDef = typeDef;
		}

		public TypeInfo(TypeInfo other, GenericInstSig git) {
			this.typeRef = GenericArgsSubstitutor.Create(other.typeRef, git);
			this.typeDef = other.typeDef;
		}

		public override int GetHashCode() {
			return typeDef.GetHashCode() +
					new SigComparer().GetHashCode(typeRef);
		}

		public override bool Equals(object obj) {
			var other = obj as TypeInfo;
			if (other == null)
				return false;
			return typeDef == other.typeDef &&
				new SigComparer().Equals(typeRef, other.typeRef);
		}

		public override string ToString() {
			return typeRef.ToString();
		}
	}

	public class MethodDefKey {
		public readonly MMethodDef methodDef;

		public MethodDefKey(MMethodDef methodDef) {
			this.methodDef = methodDef;
		}

		public override int GetHashCode() {
			return MethodEqualityComparer.CompareDeclaringTypes.GetHashCode(methodDef.MethodDef);
		}

		public override bool Equals(object obj) {
			var other = obj as MethodDefKey;
			if (other == null)
				return false;
			return MethodEqualityComparer.CompareDeclaringTypes.Equals(methodDef.MethodDef, other.methodDef.MethodDef);
		}
	}

	public class MethodInst {
		public MMethodDef origMethodDef;
		public IMethodDefOrRef methodRef;

		public MethodInst(MMethodDef origMethodDef, IMethodDefOrRef methodRef) {
			this.origMethodDef = origMethodDef;
			this.methodRef = methodRef;
		}

		public override string ToString() {
			return methodRef.ToString();
		}
	}

	public class MethodInstances {
		Dictionary<IMethodDefOrRef, List<MethodInst>> methodInstances = new Dictionary<IMethodDefOrRef, List<MethodInst>>(MethodEqualityComparer.DontCompareDeclaringTypes);

		public void InitializeFrom(MethodInstances other, GenericInstSig git) {
			foreach (var list in other.methodInstances.Values) {
				foreach (var methodInst in list) {
					var newMethod = GenericArgsSubstitutor.Create(methodInst.methodRef, git);
					Add(new MethodInst(methodInst.origMethodDef, newMethod));
				}
			}
		}

		public void Add(MethodInst methodInst) {
			List<MethodInst> list;
			var key = methodInst.methodRef;
			if (methodInst.origMethodDef.IsNewSlot() || !methodInstances.TryGetValue(key, out list))
				methodInstances[key] = list = new List<MethodInst>();
			list.Add(methodInst);
		}

		public List<MethodInst> Lookup(IMethodDefOrRef methodRef) {
			List<MethodInst> list;
			methodInstances.TryGetValue(methodRef, out list);
			return list;
		}

		public IEnumerable<List<MethodInst>> GetMethods() {
			return methodInstances.Values;
		}
	}

	// Keeps track of which methods of an interface that have been implemented
	public class InterfaceMethodInfo {
		TypeInfo iface;
		Dictionary<MethodDefKey, MMethodDef> ifaceMethodToClassMethod = new Dictionary<MethodDefKey, MMethodDef>();

		public TypeInfo IFace {
			get { return iface; }
		}

		public Dictionary<MethodDefKey, MMethodDef> IfaceMethodToClassMethod {
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

		public void Merge(InterfaceMethodInfo other) {
			foreach (var key in other.ifaceMethodToClassMethod.Keys) {
				if (other.ifaceMethodToClassMethod[key] == null)
					continue;
				if (ifaceMethodToClassMethod[key] != null)
					throw new ApplicationException("Interface method already initialized");
				ifaceMethodToClassMethod[key] = other.ifaceMethodToClassMethod[key];
			}
		}

		// Returns the previous method, or null if none
		public MMethodDef AddMethod(MMethodDef ifaceMethod, MMethodDef classMethod) {
			var ifaceKey = new MethodDefKey(ifaceMethod);
			if (!ifaceMethodToClassMethod.ContainsKey(ifaceKey))
				throw new ApplicationException("Could not find interface method");

			MMethodDef oldMethod;
			ifaceMethodToClassMethod.TryGetValue(ifaceKey, out oldMethod);
			ifaceMethodToClassMethod[ifaceKey] = classMethod;
			return oldMethod;
		}

		public void AddMethodIfEmpty(MMethodDef ifaceMethod, MMethodDef classMethod) {
			if (ifaceMethodToClassMethod[new MethodDefKey(ifaceMethod)] == null)
				AddMethod(ifaceMethod, classMethod);
		}

		public override string ToString() {
			return iface.ToString();
		}
	}

	public class InterfaceMethodInfos {
		Dictionary<ITypeDefOrRef, InterfaceMethodInfo> interfaceMethods = new Dictionary<ITypeDefOrRef, InterfaceMethodInfo>(TypeEqualityComparer.Instance);

		public IEnumerable<InterfaceMethodInfo> AllInfos {
			get { return interfaceMethods.Values; }
		}

		public void InitializeFrom(InterfaceMethodInfos other, GenericInstSig git) {
			foreach (var pair in other.interfaceMethods) {
				var oldTypeInfo = pair.Value.IFace;
				var newTypeInfo = new TypeInfo(oldTypeInfo, git);
				var oldKey = oldTypeInfo.typeRef;
				var newKey = newTypeInfo.typeRef;

				InterfaceMethodInfo newMethodsInfo = new InterfaceMethodInfo(newTypeInfo, other.interfaceMethods[oldKey]);
				if (interfaceMethods.ContainsKey(newKey))
					newMethodsInfo.Merge(interfaceMethods[newKey]);
				interfaceMethods[newKey] = newMethodsInfo;
			}
		}

		public void AddInterface(TypeInfo iface) {
			var key = iface.typeRef;
			if (!interfaceMethods.ContainsKey(key))
				interfaceMethods[key] = new InterfaceMethodInfo(iface);
		}

		// Returns the previous classMethod, or null if none
		public MMethodDef AddMethod(TypeInfo iface, MMethodDef ifaceMethod, MMethodDef classMethod) {
			return AddMethod(iface.typeRef, ifaceMethod, classMethod);
		}

		// Returns the previous classMethod, or null if none
		public MMethodDef AddMethod(ITypeDefOrRef iface, MMethodDef ifaceMethod, MMethodDef classMethod) {
			InterfaceMethodInfo info;
			if (!interfaceMethods.TryGetValue(iface, out info))
				throw new ApplicationException("Could not find interface");
			return info.AddMethod(ifaceMethod, classMethod);
		}

		public void AddMethodIfEmpty(TypeInfo iface, MMethodDef ifaceMethod, MMethodDef classMethod) {
			InterfaceMethodInfo info;
			if (!interfaceMethods.TryGetValue(iface.typeRef, out info))
				throw new ApplicationException("Could not find interface");
			info.AddMethodIfEmpty(ifaceMethod, classMethod);
		}
	}

	public class MTypeDef : Ref {
		EventDefDict events = new EventDefDict();
		FieldDefDict fields = new FieldDefDict();
		MethodDefDict methods = new MethodDefDict();
		PropertyDefDict properties = new PropertyDefDict();
		TypeDefDict types = new TypeDefDict();
		List<MGenericParamDef> genericParams;
		internal TypeInfo baseType = null;
		internal IList<TypeInfo> interfaces = new List<TypeInfo>();	// directly implemented interfaces
		internal IList<MTypeDef> derivedTypes = new List<MTypeDef>();
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

		public IList<MGenericParamDef> GenericParams {
			get { return genericParams; }
		}

		public IEnumerable<MTypeDef> NestedTypes {
			get { return types.GetSorted(); }
		}

		public MTypeDef NestingType { get; set; }

		public TypeDef TypeDef {
			get { return (TypeDef)memberRef; }
		}

		public IEnumerable<MEventDef> AllEvents {
			get { return events.GetValues(); }
		}

		public IEnumerable<MFieldDef> AllFields {
			get { return fields.GetValues(); }
		}

		public IEnumerable<MMethodDef> AllMethods {
			get { return methods.GetValues(); }
		}

		public IEnumerable<MPropertyDef> AllProperties {
			get { return properties.GetValues(); }
		}

		public IEnumerable<MEventDef> AllEventsSorted {
			get { return events.GetSorted(); }
		}

		public IEnumerable<MFieldDef> AllFieldsSorted {
			get { return fields.GetSorted(); }
		}

		public IEnumerable<MMethodDef> AllMethodsSorted {
			get { return methods.GetSorted(); }
		}

		public IEnumerable<MPropertyDef> AllPropertiesSorted {
			get { return properties.GetSorted(); }
		}

		public MTypeDef(TypeDef typeDef, Module module, int index)
			: base(typeDef, null, index) {
			this.module = module;
			genericParams = MGenericParamDef.CreateGenericParamDefList(TypeDef.GenericParameters);
		}

		public void AddInterface(MTypeDef ifaceDef, ITypeDefOrRef iface) {
			if (ifaceDef == null || iface == null)
				return;
			interfaces.Add(new TypeInfo(iface, ifaceDef));
		}

		public void AddBaseType(MTypeDef baseDef, ITypeDefOrRef baseRef) {
			if (baseDef == null || baseRef == null)
				return;
			baseType = new TypeInfo(baseRef, baseDef);
		}

		public void Add(MEventDef e) {
			events.Add(e);
		}

		public void Add(MFieldDef f) {
			fields.Add(f);
		}

		public void Add(MMethodDef m) {
			methods.Add(m);
		}

		public void Add(MPropertyDef p) {
			properties.Add(p);
		}

		public void Add(MTypeDef t) {
			types.Add(t);
		}

		public MMethodDef FindMethod(MemberRef mr) {
			return methods.Find(mr);
		}

		public MMethodDef FindMethod(IMethodDefOrRef md) {
			return methods.Find(md);
		}

		public MMethodDef FindMethod(MethodDef md) {
			return methods.Find(md);
		}

		public MMethodDef FindAnyMethod(MemberRef mr) {
			return methods.FindAny(mr);
		}

		public MFieldDef FindField(MemberRef fr) {
			return fields.Find(fr);
		}

		public MFieldDef FindAnyField(MemberRef fr) {
			return fields.FindAny(fr);
		}

		public MPropertyDef Find(PropertyDef pr) {
			return properties.Find(pr);
		}

		public MPropertyDef FindAny(PropertyDef pr) {
			return properties.FindAny(pr);
		}

		public MEventDef Find(EventDef er) {
			return events.Find(er);
		}

		public MEventDef FindAny(EventDef er) {
			return events.FindAny(er);
		}

		public MPropertyDef Create(PropertyDef newProp) {
			if (FindAny(newProp) != null)
				throw new ApplicationException("Can't add a property when it's already been added");

			var propDef = new MPropertyDef(newProp, this, properties.Count);
			Add(propDef);
			TypeDef.Properties.Add(newProp);
			return propDef;
		}

		public MEventDef Create(EventDef newEvent) {
			if (FindAny(newEvent) != null)
				throw new ApplicationException("Can't add an event when it's already been added");

			var eventDef = new MEventDef(newEvent, this, events.Count);
			Add(eventDef);
			TypeDef.Events.Add(newEvent);
			return eventDef;
		}

		public void AddMembers() {
			var type = TypeDef;

			for (int i = 0; i < type.Events.Count; i++)
				Add(new MEventDef(type.Events[i], this, i));
			for (int i = 0; i < type.Fields.Count; i++)
				Add(new MFieldDef(type.Fields[i], this, i));
			for (int i = 0; i < type.Methods.Count; i++)
				Add(new MMethodDef(type.Methods[i], this, i));
			for (int i = 0; i < type.Properties.Count; i++)
				Add(new MPropertyDef(type.Properties[i], this, i));

			foreach (var propDef in properties.GetValues()) {
				foreach (var method in propDef.MethodDefs()) {
					var methodDef = FindMethod(method);
					if (methodDef == null)
						throw new ApplicationException("Could not find property method");
					methodDef.Property = propDef;
					if (method == propDef.PropertyDef.GetMethod)
						propDef.GetMethod = methodDef;
					if (method == propDef.PropertyDef.SetMethod)
						propDef.SetMethod = methodDef;
				}
			}

			foreach (var eventDef in events.GetValues()) {
				foreach (var method in eventDef.MethodDefs()) {
					var methodDef = FindMethod(method);
					if (methodDef == null)
						throw new ApplicationException("Could not find event method");
					methodDef.Event = eventDef;
					if (method == eventDef.EventDef.AddMethod)
						eventDef.AddMethod = methodDef;
					if (method == eventDef.EventDef.RemoveMethod)
						eventDef.RemoveMethod = methodDef;
					if (method == eventDef.EventDef.InvokeMethod)
						eventDef.RaiseMethod = methodDef;
				}
			}
		}

		public void OnTypesRenamed() {
			events.OnTypesRenamed();
			properties.OnTypesRenamed();
			fields.OnTypesRenamed();
			methods.OnTypesRenamed();
			types.OnTypesRenamed();
		}

		public bool IsNested() {
			return NestingType != null;
		}

		public bool IsGlobalType() {
			if (!IsNested())
				return TypeDef.IsPublic;
			switch (TypeDef.Visibility) {
			case TypeAttributes.NestedPrivate:
			case TypeAttributes.NestedAssembly:
			case TypeAttributes.NestedFamANDAssem:
				return false;
			case TypeAttributes.NestedPublic:
			case TypeAttributes.NestedFamily:
			case TypeAttributes.NestedFamORAssem:
				return NestingType.IsGlobalType();
			default:
				return false;
			}
		}

		public void InitializeVirtualMembers(MethodNameGroups groups, IResolver resolver) {
			if (initializeVirtualMembersCalled)
				return;
			initializeVirtualMembersCalled = true;

			foreach (var iface in interfaces)
				iface.typeDef.InitializeVirtualMembers(groups, resolver);
			if (baseType != null)
				baseType.typeDef.InitializeVirtualMembers(groups, resolver);

			foreach (var methodDef in methods.GetValues()) {
				if (methodDef.IsVirtual())
					groups.Add(methodDef);
			}

			InstantiateVirtualMembers(groups);
			InitializeInterfaceMethods(groups);
		}

		void InitializeAllInterfaces() {
			if (baseType != null)
				InitializeInterfaces(baseType);

			foreach (var iface in interfaces) {
				allImplementedInterfaces[iface] = true;
				interfaceMethodInfos.AddInterface(iface);
				InitializeInterfaces(iface);
			}
		}

		void InitializeInterfaces(TypeInfo typeInfo) {
			var git = typeInfo.typeRef.TryGetGenericInstSig();
			interfaceMethodInfos.InitializeFrom(typeInfo.typeDef.interfaceMethodInfos, git);
			foreach (var info in typeInfo.typeDef.allImplementedInterfaces.Keys) {
				var newTypeInfo = new TypeInfo(info, git);
				allImplementedInterfaces[newTypeInfo] = true;
			}
		}

		void InitializeInterfaceMethods(MethodNameGroups groups) {
			InitializeAllInterfaces();

			if (TypeDef.IsInterface)
				return;

			//--- Partition II 12.2 Implementing virtual methods on interfaces:
			//--- The VES shall use the following algorithm to determine the appropriate
			//--- implementation of an interface's virtual abstract methods:
			//---
			//--- * If the base class implements the interface, start with the same virtual methods
			//---	that it provides; otherwise, create an interface that has empty slots for all
			//---	virtual functions.
			// Done. See initializeAllInterfaces().

			var methodsDict = new Dictionary<IMethodDefOrRef, MMethodDef>(MethodEqualityComparer.DontCompareDeclaringTypes);

			//--- * If this class explicitly specifies that it implements the interface (i.e., the
			//---	interfaces that appear in this class‘ InterfaceImpl table, §22.23)
			//---	* If the class defines any public virtual newslot methods whose name and
			//---	  signature match a virtual method on the interface, then use these new virtual
			//---	  methods to implement the corresponding interface method.
			if (interfaces.Count > 0) {
				methodsDict.Clear();
				foreach (var method in methods.GetValues()) {
					if (!method.IsPublic() || !method.IsVirtual() || !method.IsNewSlot())
						continue;
					methodsDict[method.MethodDef] = method;
				}

				foreach (var ifaceInfo in interfaces) {
					foreach (var methodsList in ifaceInfo.typeDef.virtualMethodInstances.GetMethods()) {
						if (methodsList.Count < 1)
							continue;
						var methodInst = methodsList[0];
						var ifaceMethod = methodInst.origMethodDef;
						if (!ifaceMethod.IsVirtual())
							continue;
						var ifaceMethodRef = GenericArgsSubstitutor.Create(methodInst.methodRef, ifaceInfo.typeRef.TryGetGenericInstSig());
						MMethodDef classMethod;
						if (!methodsDict.TryGetValue(ifaceMethodRef, out classMethod))
							continue;
						interfaceMethodInfos.AddMethod(ifaceInfo, ifaceMethod, classMethod);
					}
				}
			}

			//--- * If there are any virtual methods in the interface that still have empty slots,
			//---	see if there are any public virtual methods, but not public virtual newslot
			//---	methods, available on this class (directly or inherited) having the same name
			//---	and signature, then use these to implement the corresponding methods on the
			//---	interface.
			methodsDict.Clear();
			foreach (var methodInstList in virtualMethodInstances.GetMethods()) {
				// This class' method is at the end
				for (int i = methodInstList.Count - 1; i >= 0; i--) {
					var classMethod = methodInstList[i];
					// These methods are guaranteed to be virtual.
					// We should allow newslot methods, despite what the official doc says.
					if (!classMethod.origMethodDef.IsPublic())
						continue;
					methodsDict[classMethod.methodRef] = classMethod.origMethodDef;
					break;
				}
			}
			foreach (var ifaceInfo in allImplementedInterfaces.Keys) {
				foreach (var methodsList in ifaceInfo.typeDef.virtualMethodInstances.GetMethods()) {
					if (methodsList.Count < 1)
						continue;
					var ifaceMethod = methodsList[0].origMethodDef;
					if (!ifaceMethod.IsVirtual())
						continue;
					var ifaceMethodRef = GenericArgsSubstitutor.Create(ifaceMethod.MethodDef, ifaceInfo.typeRef.TryGetGenericInstSig());
					MMethodDef classMethod;
					if (!methodsDict.TryGetValue(ifaceMethodRef, out classMethod))
						continue;
					interfaceMethodInfos.AddMethodIfEmpty(ifaceInfo, ifaceMethod, classMethod);
				}
			}

			//--- * Apply all MethodImpls that are specified for this class, thereby placing
			//---	explicitly specified virtual methods into the interface in preference to those
			//---	inherited or chosen by name matching.
			methodsDict.Clear();
			var ifaceMethodsDict = new Dictionary<IMethodDefOrRef, MMethodDef>(MethodEqualityComparer.CompareDeclaringTypes);
			foreach (var ifaceInfo in allImplementedInterfaces.Keys) {
				var git = ifaceInfo.typeRef.TryGetGenericInstSig();
				foreach (var ifaceMethod in ifaceInfo.typeDef.methods.GetValues()) {
					IMethodDefOrRef ifaceMethodRef = ifaceMethod.MethodDef;
					if (git != null)
						ifaceMethodRef = SimpleClone(ifaceMethod.MethodDef, ifaceInfo.typeRef);
					ifaceMethodsDict[ifaceMethodRef] = ifaceMethod;
				}
			}
			foreach (var classMethod in methods.GetValues()) {
				if (!classMethod.IsVirtual())
					continue;
				foreach (var overrideMethod in classMethod.MethodDef.Overrides) {
					MMethodDef ifaceMethod;
					if (!ifaceMethodsDict.TryGetValue(overrideMethod.MethodDeclaration, out ifaceMethod)) {
						// We couldn't find the interface method (eg. interface not resolved) or
						// it overrides a base class method, and not an interface method.
						continue;
					}

					interfaceMethodInfos.AddMethod(overrideMethod.MethodDeclaration.DeclaringType, ifaceMethod, classMethod);
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
					if (!ResolvedAllInterfaces() || !ResolvedBaseClasses())
						continue;
					// Ignore if COM class
					if (!TypeDef.IsImport &&
						!HasAttribute("System.Runtime.InteropServices.ComImportAttribute") &&
						!HasAttribute("System.Runtime.InteropServices.TypeLibTypeAttribute")) {
						Logger.w("Could not find interface method {0} ({1:X8}). Type: {2} ({3:X8})",
								Utils.RemoveNewlines(pair.Key.methodDef.MethodDef),
								pair.Key.methodDef.MethodDef.MDToken.ToInt32(),
								Utils.RemoveNewlines(TypeDef),
								TypeDef.MDToken.ToInt32());
					}
				}
			}

			foreach (var info in interfaceMethodInfos.AllInfos) {
				foreach (var pair in info.IfaceMethodToClassMethod) {
					if (pair.Value == null)
						continue;
					if (pair.Key.methodDef.MethodDef.Name != pair.Value.MethodDef.Name)
						continue;
					groups.Same(pair.Key.methodDef, pair.Value);
				}
			}
		}

		bool HasAttribute(string name) {
			foreach (var attr in TypeDef.CustomAttributes) {
				if (attr.TypeFullName == name)
					return true;
			}
			return false;
		}

		// Returns true if all interfaces have been resolved
		bool? resolvedAllInterfacesResult;
		bool ResolvedAllInterfaces() {
			if (!resolvedAllInterfacesResult.HasValue) {
				resolvedAllInterfacesResult = true;	// If we find a circular reference
				resolvedAllInterfacesResult = ResolvedAllInterfacesInternal();
			}
			return resolvedAllInterfacesResult.Value;
		}
		bool ResolvedAllInterfacesInternal() {
			if (TypeDef.Interfaces.Count != interfaces.Count)
				return false;
			foreach (var ifaceInfo in interfaces) {
				if (!ifaceInfo.typeDef.ResolvedAllInterfaces())
					return false;
			}
			return true;
		}

		// Returns true if all base classes have been resolved
		bool? resolvedBaseClassesResult;
		bool ResolvedBaseClasses() {
			if (!resolvedBaseClassesResult.HasValue) {
				resolvedBaseClassesResult = true;	// If we find a circular reference
				resolvedBaseClassesResult = ResolvedBaseClassesInternal();
			}
			return resolvedBaseClassesResult.Value;
		}
		bool ResolvedBaseClassesInternal() {
			if (TypeDef.BaseType == null)
				return true;
			if (baseType == null)
				return false;
			return baseType.typeDef.ResolvedBaseClasses();
		}

		MemberRef SimpleClone(MethodDef methodRef, ITypeDefOrRef declaringType) {
			if (module == null)
				return new MemberRefUser(null, methodRef.Name, methodRef.MethodSig, declaringType);
			var mr = new MemberRefUser(module.ModuleDefMD, methodRef.Name, methodRef.MethodSig, declaringType);
			return module.ModuleDefMD.UpdateRowId(mr);
		}

		void InstantiateVirtualMembers(MethodNameGroups groups) {
			if (!TypeDef.IsInterface) {
				if (baseType != null)
					virtualMethodInstances.InitializeFrom(baseType.typeDef.virtualMethodInstances, baseType.typeRef.TryGetGenericInstSig());

				// Figure out which methods we override in the base class
				foreach (var methodDef in methods.GetValues()) {
					if (!methodDef.IsVirtual() || methodDef.IsNewSlot())
						continue;
					var methodInstList = virtualMethodInstances.Lookup(methodDef.MethodDef);
					if (methodInstList == null)
						continue;
					foreach (var methodInst in methodInstList)
						groups.Same(methodDef, methodInst.origMethodDef);
				}
			}

			foreach (var methodDef in methods.GetValues()) {
				if (!methodDef.IsVirtual())
					continue;
				virtualMethodInstances.Add(new MethodInst(methodDef, methodDef.MethodDef));
			}
		}
	}
}
