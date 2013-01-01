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
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.code.renamer.asmmodules;
using de4dot.blocks;

namespace de4dot.code.renamer {
	[Flags]
	public enum RenamerFlags {
		RenameNamespaces = 1,
		RenameTypes = 2,
		RenameProperties = 4,
		RenameEvents = 8,
		RenameFields = 0x10,
		RenameMethods = 0x20,
		RenameMethodArgs = 0x40,
		RenameGenericParams = 0x80,
		RestoreProperties = 0x100,
		RestorePropertiesFromNames = 0x200,
		RestoreEvents = 0x400,
		RestoreEventsFromNames = 0x800,
		DontCreateNewParamDefs = 0x1000,
		DontRenameDelegateFields = 0x2000,
	}

	public class Renamer {
		public RenamerFlags RenamerFlags { get; set; }
		public bool RenameNamespaces {
			get { return (RenamerFlags & RenamerFlags.RenameNamespaces) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RenameNamespaces;
				else
					RenamerFlags &= ~RenamerFlags.RenameNamespaces;
			}
		}
		public bool RenameTypes {
			get { return (RenamerFlags & RenamerFlags.RenameTypes) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RenameTypes;
				else
					RenamerFlags &= ~RenamerFlags.RenameTypes;
			}
		}
		public bool RenameProperties {
			get { return (RenamerFlags & RenamerFlags.RenameProperties) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RenameProperties;
				else
					RenamerFlags &= ~RenamerFlags.RenameProperties;
			}
		}
		public bool RenameEvents {
			get { return (RenamerFlags & RenamerFlags.RenameEvents) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RenameEvents;
				else
					RenamerFlags &= ~RenamerFlags.RenameEvents;
			}
		}
		public bool RenameFields {
			get { return (RenamerFlags & RenamerFlags.RenameFields) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RenameFields;
				else
					RenamerFlags &= ~RenamerFlags.RenameFields;
			}
		}
		public bool RenameMethods {
			get { return (RenamerFlags & RenamerFlags.RenameMethods) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RenameMethods;
				else
					RenamerFlags &= ~RenamerFlags.RenameMethods;
			}
		}
		public bool RenameMethodArgs {
			get { return (RenamerFlags & RenamerFlags.RenameMethodArgs) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RenameMethodArgs;
				else
					RenamerFlags &= ~RenamerFlags.RenameMethodArgs;
			}
		}
		public bool RenameGenericParams {
			get { return (RenamerFlags & RenamerFlags.RenameGenericParams) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RenameGenericParams;
				else
					RenamerFlags &= ~RenamerFlags.RenameGenericParams;
			}
		}
		public bool RestoreProperties {
			get { return (RenamerFlags & RenamerFlags.RestoreProperties) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RestoreProperties;
				else
					RenamerFlags &= ~RenamerFlags.RestoreProperties;
			}
		}
		public bool RestorePropertiesFromNames {
			get { return (RenamerFlags & RenamerFlags.RestorePropertiesFromNames) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RestorePropertiesFromNames;
				else
					RenamerFlags &= ~RenamerFlags.RestorePropertiesFromNames;
			}
		}
		public bool RestoreEvents {
			get { return (RenamerFlags & RenamerFlags.RestoreEvents) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RestoreEvents;
				else
					RenamerFlags &= ~RenamerFlags.RestoreEvents;
			}
		}
		public bool RestoreEventsFromNames {
			get { return (RenamerFlags & RenamerFlags.RestoreEventsFromNames) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.RestoreEventsFromNames;
				else
					RenamerFlags &= ~RenamerFlags.RestoreEventsFromNames;
			}
		}
		public bool DontCreateNewParamDefs {
			get { return (RenamerFlags & RenamerFlags.DontCreateNewParamDefs) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.DontCreateNewParamDefs;
				else
					RenamerFlags &= ~RenamerFlags.DontCreateNewParamDefs;
			}
		}
		public bool DontRenameDelegateFields {
			get { return (RenamerFlags & RenamerFlags.DontRenameDelegateFields) != 0; }
			set {
				if (value)
					RenamerFlags |= RenamerFlags.DontRenameDelegateFields;
				else
					RenamerFlags &= ~RenamerFlags.DontRenameDelegateFields;
			}
		}

		Modules modules;
		MemberInfos memberInfos = new MemberInfos();
		DerivedFrom isDelegateClass;
		MergeStateHelper mergeStateHelper;
		bool isVerbose;

		static string[] delegateClasses = new string[] {
			"System.Delegate",
			"System.MulticastDelegate",
		};

		public Renamer(IDeobfuscatorContext deobfuscatorContext, IEnumerable<IObfuscatedFile> files, RenamerFlags flags) {
			RenamerFlags = flags;

			modules = new Modules(deobfuscatorContext);
			isDelegateClass = new DerivedFrom(delegateClasses);
			mergeStateHelper = new MergeStateHelper(memberInfos);

			foreach (var file in files)
				modules.add(new Module(file));
		}

		public void rename() {
			if (modules.Empty)
				return;
			isVerbose = !Logger.Instance.IgnoresEvent(LoggerEvent.Verbose);
			Logger.n("Renaming all obfuscated symbols");

			modules.initialize();
			renameResourceKeys();
			var groups = modules.initializeVirtualMembers();
			memberInfos.initialize(modules);
			renameTypeDefs();
			renameTypeRefs();
			modules.onTypesRenamed();
			restorePropertiesAndEvents(groups);
			prepareRenameMemberDefs(groups);
			renameMemberDefs();
			renameMemberRefs();
			removeUselessOverrides(groups);
			renameResources();
			modules.cleanUp();
		}

		void renameResourceKeys() {
			foreach (var module in modules.TheModules) {
				if (!module.ObfuscatedFile.RenameResourceKeys)
					continue;
				new ResourceKeysRenamer(module.ModuleDefMD, module.ObfuscatedFile.NameChecker).rename();
			}
		}

		void removeUselessOverrides(MethodNameGroups groups) {
			foreach (var group in groups.getAllGroups()) {
				foreach (var method in group.Methods) {
					if (!method.Owner.HasModule)
						continue;
					if (!method.isPublic())
						continue;
					var overrides = method.MethodDef.Overrides;
					for (int i = 0; i < overrides.Count; i++) {
						var overrideMethod = overrides[i].MethodDeclaration;
						if (method.MethodDef.Name != overrideMethod.Name)
							continue;
						if (isVerbose)
							Logger.v("Removed useless override from method {0} ({1:X8}), override: {2:X8}",
									Utils.removeNewlines(method.MethodDef),
									method.MethodDef.MDToken.ToInt32(),
									overrideMethod.MDToken.ToInt32());
						overrides.RemoveAt(i);
						i--;
					}
				}
			}
		}

		void renameTypeDefs() {
			if (isVerbose)
				Logger.v("Renaming obfuscated type definitions");

			foreach (var module in modules.TheModules) {
				if (module.ObfuscatedFile.RemoveNamespaceWithOneType)
					removeOneClassNamespaces(module);
			}

			var state = new TypeRenamerState();
			foreach (var type in modules.AllTypes)
				state.addTypeName(memberInfos.type(type).oldName);
			prepareRenameTypes(modules.BaseTypes, state);
			fixClsTypeNames();
			renameTypeDefs(modules.NonNestedTypes);
		}

		void removeOneClassNamespaces(Module module) {
			var nsToTypes = new Dictionary<string, List<MTypeDef>>(StringComparer.Ordinal);

			foreach (var typeDef in module.getAllTypes()) {
				List<MTypeDef> list;
				var ns = typeDef.TypeDef.Namespace.String;
				if (string.IsNullOrEmpty(ns))
					continue;
				if (module.ObfuscatedFile.NameChecker.isValidNamespaceName(ns))
					continue;
				if (!nsToTypes.TryGetValue(ns, out list))
					nsToTypes[ns] = list = new List<MTypeDef>();
				list.Add(typeDef);
			}

			var sortedNamespaces = new List<List<MTypeDef>>(nsToTypes.Values);
			sortedNamespaces.Sort((a, b) => {
				return UTF8String.CompareTo(a[0].TypeDef.Namespace, b[0].TypeDef.Namespace);
			});
			foreach (var list in sortedNamespaces) {
				const int maxClasses = 1;
				if (list.Count != maxClasses)
					continue;
				if (isVerbose)
					Logger.v("Removing namespace: {0}", Utils.removeNewlines(list[0].TypeDef.Namespace));
				foreach (var type in list)
					memberInfos.type(type).newNamespace = "";
			}
		}

		void renameTypeDefs(IEnumerable<MTypeDef> typeDefs) {
			Logger.Instance.indent();
			foreach (var typeDef in typeDefs) {
				rename(typeDef);
				renameTypeDefs(typeDef.NestedTypes);
			}
			Logger.Instance.deIndent();
		}

		void rename(MTypeDef type) {
			var typeDef = type.TypeDef;
			var info = memberInfos.type(type);

			if (isVerbose)
				Logger.v("Type: {0} ({1:X8})", Utils.removeNewlines(typeDef.FullName), typeDef.MDToken.ToUInt32());
			Logger.Instance.indent();

			renameGenericParams(type.GenericParams);

			if (RenameTypes && info.gotNewName()) {
				var old = typeDef.Name;
				typeDef.Name = info.newName;
				if (isVerbose)
					Logger.v("Name: {0} => {1}", Utils.removeNewlines(old), Utils.removeNewlines(typeDef.Name));
			}

			if (RenameNamespaces && info.newNamespace != null) {
				var old = typeDef.Namespace;
				typeDef.Namespace = info.newNamespace;
				if (isVerbose)
					Logger.v("Namespace: {0} => {1}", Utils.removeNewlines(old), Utils.removeNewlines(typeDef.Namespace));
			}

			Logger.Instance.deIndent();
		}

		void renameGenericParams(IEnumerable<MGenericParamDef> genericParams) {
			if (!RenameGenericParams)
				return;
			foreach (var param in genericParams) {
				var info = memberInfos.gparam(param);
				if (!info.gotNewName())
					continue;
				param.GenericParam.Name = info.newName;
				if (isVerbose)
					Logger.v("GenParam: {0} => {1}", Utils.removeNewlines(info.oldFullName), Utils.removeNewlines(param.GenericParam.FullName));
			}
		}

		void renameMemberDefs() {
			if (isVerbose)
				Logger.v("Renaming member definitions #2");

			var allTypes = new List<MTypeDef>(modules.AllTypes);
			allTypes.Sort((a, b) => a.Index.CompareTo(b.Index));

			Logger.Instance.indent();
			foreach (var typeDef in allTypes)
				renameMembers(typeDef);
			Logger.Instance.deIndent();
		}

		void renameMembers(MTypeDef type) {
			var info = memberInfos.type(type);

			if (isVerbose)
				Logger.v("Type: {0}", Utils.removeNewlines(info.type.TypeDef.FullName));
			Logger.Instance.indent();

			renameFields(info);
			renameProperties(info);
			renameEvents(info);
			renameMethods(info);

			Logger.Instance.deIndent();
		}

		void renameFields(TypeInfo info) {
			if (!RenameFields)
				return;
			bool isDelegateType = isDelegateClass.check(info.type);
			foreach (var fieldDef in info.type.AllFieldsSorted) {
				var fieldInfo = memberInfos.field(fieldDef);
				if (!fieldInfo.gotNewName())
					continue;
				if (isDelegateType && DontRenameDelegateFields)
					continue;
				fieldDef.FieldDef.Name = fieldInfo.newName;
				if (isVerbose)
					Logger.v("Field: {0} ({1:X8}) => {2}",
							Utils.removeNewlines(fieldInfo.oldFullName),
							fieldDef.FieldDef.MDToken.ToUInt32(),
							Utils.removeNewlines(fieldDef.FieldDef.FullName));
			}
		}

		void renameProperties(TypeInfo info) {
			if (!RenameProperties)
				return;
			foreach (var propDef in info.type.AllPropertiesSorted) {
				var propInfo = memberInfos.prop(propDef);
				if (!propInfo.gotNewName())
					continue;
				propDef.PropertyDef.Name = propInfo.newName;
				if (isVerbose)
					Logger.v("Property: {0} ({1:X8}) => {2}",
							Utils.removeNewlines(propInfo.oldFullName),
							propDef.PropertyDef.MDToken.ToUInt32(),
							Utils.removeNewlines(propDef.PropertyDef.FullName));
			}
		}

		void renameEvents(TypeInfo info) {
			if (!RenameEvents)
				return;
			foreach (var eventDef in info.type.AllEventsSorted) {
				var eventInfo = memberInfos.evt(eventDef);
				if (!eventInfo.gotNewName())
					continue;
				eventDef.EventDef.Name = eventInfo.newName;
				if (isVerbose)
					Logger.v("Event: {0} ({1:X8}) => {2}",
							Utils.removeNewlines(eventInfo.oldFullName),
							eventDef.EventDef.MDToken.ToUInt32(),
							Utils.removeNewlines(eventDef.EventDef.FullName));
			}
		}

		void renameMethods(TypeInfo info) {
			if (!RenameMethods && !RenameMethodArgs && !RenameGenericParams)
				return;
			foreach (var methodDef in info.type.AllMethodsSorted) {
				var methodInfo = memberInfos.method(methodDef);
				if (isVerbose)
					Logger.v("Method {0} ({1:X8})", Utils.removeNewlines(methodInfo.oldFullName), methodDef.MethodDef.MDToken.ToUInt32());
				Logger.Instance.indent();

				renameGenericParams(methodDef.GenericParams);

				if (RenameMethods && methodInfo.gotNewName()) {
					methodDef.MethodDef.Name = methodInfo.newName;
					if (isVerbose)
						Logger.v("Name: {0} => {1}", Utils.removeNewlines(methodInfo.oldFullName), Utils.removeNewlines(methodDef.MethodDef.FullName));
				}

				if (RenameMethodArgs) {
					foreach (var param in methodDef.AllParamDefs) {
						var paramInfo = memberInfos.param(param);
						if (!paramInfo.gotNewName())
							continue;
						if (!param.ParameterDef.HasParamDef) {
							if (DontCreateNewParamDefs)
								continue;
							param.ParameterDef.CreateParamDef();
						}
						param.ParameterDef.Name = paramInfo.newName;
						if (isVerbose) {
							if (param.IsReturnParameter)
								Logger.v("RetParam: {0} => {1}", Utils.removeNewlines(paramInfo.oldName), Utils.removeNewlines(paramInfo.newName));
							else
								Logger.v("Param ({0}/{1}): {2} => {3}", param.ParameterDef.MethodSigIndex + 1, methodDef.MethodDef.MethodSig.GetParamCount(), Utils.removeNewlines(paramInfo.oldName), Utils.removeNewlines(paramInfo.newName));
						}
					}
				}

				Logger.Instance.deIndent();
			}
		}

		void renameMemberRefs() {
			if (isVerbose)
				Logger.v("Renaming references to other definitions");
			foreach (var module in modules.TheModules) {
				if (modules.TheModules.Count > 1 && isVerbose)
					Logger.v("Renaming references to other definitions ({0})", module.Filename);
				Logger.Instance.indent();
				foreach (var refToDef in module.MethodRefsToRename)
					refToDef.reference.Name = refToDef.definition.Name;
				foreach (var refToDef in module.FieldRefsToRename)
					refToDef.reference.Name = refToDef.definition.Name;
				foreach (var info in module.CustomAttributeFieldRefs)
					info.cattr.NamedArguments[info.index].Name = info.reference.Name;
				foreach (var info in module.CustomAttributePropertyRefs)
					info.cattr.NamedArguments[info.index].Name = info.reference.Name;
				Logger.Instance.deIndent();
			}
		}

		void renameResources() {
			if (isVerbose)
				Logger.v("Renaming resources");
			foreach (var module in modules.TheModules) {
				if (modules.TheModules.Count > 1 && isVerbose)
					Logger.v("Renaming resources ({0})", module.Filename);
				Logger.Instance.indent();
				renameResources(module);
				Logger.Instance.deIndent();
			}
		}

		void renameResources(Module module) {
			var renamedTypes = new List<TypeInfo>();
			foreach (var type in module.getAllTypes()) {
				var info = memberInfos.type(type);
				if (info.oldFullName != info.type.TypeDef.FullName)
					renamedTypes.Add(info);
			}
			if (renamedTypes.Count == 0)
				return;

			new ResourceRenamer(module).rename(renamedTypes);
		}

		// Make sure the renamed types are using valid CLS names. That means renaming all
		// generic types from eg. Class1 to Class1`2. If we don't do this, some decompilers
		// (eg. ILSpy v1.0) won't produce correct output.
		void fixClsTypeNames() {
			foreach (var type in modules.NonNestedTypes)
				fixClsTypeNames(null, type);
		}

		void fixClsTypeNames(MTypeDef nesting, MTypeDef nested) {
			int nestingCount = nesting == null ? 0 : nesting.GenericParams.Count;
			int arity = nested.GenericParams.Count - nestingCount;
			var nestedInfo = memberInfos.type(nested);
			if (nestedInfo.renamed && arity > 0)
				nestedInfo.newName += "`" + arity;
			foreach (var nestedType in nested.NestedTypes)
				fixClsTypeNames(nested, nestedType);
		}

		void prepareRenameTypes(IEnumerable<MTypeDef> types, TypeRenamerState state) {
			foreach (var typeDef in types) {
				memberInfos.type(typeDef).prepareRenameTypes(state);
				prepareRenameTypes(typeDef.derivedTypes, state);
			}
		}

		void renameTypeRefs() {
			if (isVerbose)
				Logger.v("Renaming references to type definitions");
			var theModules = modules.TheModules;
			foreach (var module in theModules) {
				if (theModules.Count > 1 && isVerbose)
					Logger.v("Renaming references to type definitions ({0})", module.Filename);
				Logger.Instance.indent();
				foreach (var refToDef in module.TypeRefsToRename) {
					refToDef.reference.Name = refToDef.definition.Name;
					refToDef.reference.Namespace = refToDef.definition.Namespace;
				}
				Logger.Instance.deIndent();
			}
		}

		void restorePropertiesAndEvents(MethodNameGroups groups) {
			var allGroups = groups.getAllGroups();
			restoreVirtualProperties(allGroups);
			restorePropertiesFromNames(allGroups);
			resetVirtualPropertyNames(allGroups);
			restoreVirtualEvents(allGroups);
			restoreEventsFromNames(allGroups);
			resetVirtualEventNames(allGroups);
		}

		void resetVirtualPropertyNames(IEnumerable<MethodNameGroup> allGroups) {
			if (!this.RenameProperties)
				return;
			foreach (var group in allGroups) {
				MPropertyDef prop = null;
				foreach (var method in group.Methods) {
					if (method.Property == null)
						continue;
					if (method.Owner.HasModule)
						continue;
					prop = method.Property;
					break;
				}
				if (prop == null)
					continue;
				foreach (var method in group.Methods) {
					if (!method.Owner.HasModule)
						continue;
					if (method.Property == null)
						continue;
					memberInfos.prop(method.Property).rename(prop.PropertyDef.Name.String);
				}
			}
		}

		void resetVirtualEventNames(IEnumerable<MethodNameGroup> allGroups) {
			if (!this.RenameEvents)
				return;
			foreach (var group in allGroups) {
				MEventDef evt = null;
				foreach (var method in group.Methods) {
					if (method.Event == null)
						continue;
					if (method.Owner.HasModule)
						continue;
					evt = method.Event;
					break;
				}
				if (evt == null)
					continue;
				foreach (var method in group.Methods) {
					if (!method.Owner.HasModule)
						continue;
					if (method.Event == null)
						continue;
					memberInfos.evt(method.Event).rename(evt.EventDef.Name.String);
				}
			}
		}

		void restoreVirtualProperties(IEnumerable<MethodNameGroup> allGroups) {
			if (!RestoreProperties)
				return;
			foreach (var group in allGroups) {
				restoreVirtualProperties(group);
				restoreExplicitVirtualProperties(group);
			}
		}

		void restoreExplicitVirtualProperties(MethodNameGroup group) {
			if (group.Methods.Count != 1)
				return;
			var propMethod = group.Methods[0];
			if (propMethod.Property != null)
				return;
			if (propMethod.MethodDef.Overrides.Count == 0)
				return;

			var theProperty = getOverriddenProperty(propMethod);
			if (theProperty == null)
				return;

			createProperty(theProperty, propMethod, getOverridePrefix(group, propMethod));
		}

		void restoreVirtualProperties(MethodNameGroup group) {
			if (group.Methods.Count <= 1 || !group.hasProperty())
				return;

			MPropertyDef prop = null;
			List<MMethodDef> missingProps = null;
			foreach (var method in group.Methods) {
				if (method.Property == null) {
					if (missingProps == null)
						missingProps = new List<MMethodDef>();
					missingProps.Add(method);
				}
				else if (prop == null || !method.Owner.HasModule)
					prop = method.Property;
			}
			if (prop == null)
				return;	// Should never happen
			if (missingProps == null)
				return;

			foreach (var method in missingProps)
				createProperty(prop, method, "");
		}

		void createProperty(MPropertyDef propDef, MMethodDef methodDef, string overridePrefix) {
			if (!methodDef.Owner.HasModule)
				return;

			var newPropertyName = overridePrefix + propDef.PropertyDef.Name;
			if (!DotNetUtils.hasReturnValue(methodDef.MethodDef))
				createPropertySetter(newPropertyName, methodDef);
			else
				createPropertyGetter(newPropertyName, methodDef);
		}

		void restorePropertiesFromNames(IEnumerable<MethodNameGroup> allGroups) {
			if (!RestorePropertiesFromNames)
				return;

			foreach (var group in allGroups) {
				var groupMethod = group.Methods[0];
				var methodName = groupMethod.MethodDef.Name.String;
				bool onlyRenamableMethods = !group.hasNonRenamableMethod();

				if (Utils.StartsWith(methodName, "get_", StringComparison.Ordinal)) {
					var propName = methodName.Substring(4);
					foreach (var method in group.Methods) {
						if (onlyRenamableMethods && !memberInfos.type(method.Owner).NameChecker.isValidPropertyName(propName))
							continue;
						createPropertyGetter(propName, method);
					}
				}
				else if (Utils.StartsWith(methodName, "set_", StringComparison.Ordinal)) {
					var propName = methodName.Substring(4);
					foreach (var method in group.Methods) {
						if (onlyRenamableMethods && !memberInfos.type(method.Owner).NameChecker.isValidPropertyName(propName))
							continue;
						createPropertySetter(propName, method);
					}
				}
			}

			foreach (var type in modules.AllTypes) {
				foreach (var method in type.AllMethodsSorted) {
					if (method.isVirtual())
						continue;	// Virtual methods are in allGroups, so already fixed above
					if (method.Property != null)
						continue;
					var methodName = method.MethodDef.Name.String;
					if (Utils.StartsWith(methodName, "get_", StringComparison.Ordinal))
						createPropertyGetter(methodName.Substring(4), method);
					else if (Utils.StartsWith(methodName, "set_", StringComparison.Ordinal))
						createPropertySetter(methodName.Substring(4), method);
				}
			}
		}

		MPropertyDef createPropertyGetter(string name, MMethodDef propMethod) {
			if (string.IsNullOrEmpty(name))
				return null;
			var ownerType = propMethod.Owner;
			if (!ownerType.HasModule)
				return null;
			if (propMethod.Property != null)
				return null;

			var sig = propMethod.MethodDef.MethodSig;
			if (sig == null)
				return null;
			var propType = sig.RetType;
			var propDef = createProperty(ownerType, name, propType, propMethod.MethodDef, null);
			if (propDef == null)
				return null;
			if (propDef.GetMethod != null)
				return null;
			if (isVerbose)
				Logger.v("Restoring property getter {0} ({1:X8}), Property: {2} ({3:X8})",
						Utils.removeNewlines(propMethod),
						propMethod.MethodDef.MDToken.ToInt32(),
						Utils.removeNewlines(propDef.PropertyDef),
						propDef.PropertyDef.MDToken.ToInt32());
			propDef.PropertyDef.GetMethod = propMethod.MethodDef;
			propDef.GetMethod = propMethod;
			propMethod.Property = propDef;
			return propDef;
		}

		MPropertyDef createPropertySetter(string name, MMethodDef propMethod) {
			if (string.IsNullOrEmpty(name))
				return null;
			var ownerType = propMethod.Owner;
			if (!ownerType.HasModule)
				return null;
			if (propMethod.Property != null)
				return null;

			var sig = propMethod.MethodDef.MethodSig;
			if (sig == null || sig.Params.Count == 0)
				return null;
			var propType = sig.Params[sig.Params.Count - 1];
			var propDef = createProperty(ownerType, name, propType, null, propMethod.MethodDef);
			if (propDef == null)
				return null;
			if (propDef.SetMethod != null)
				return null;
			if (isVerbose)
				Logger.v("Restoring property setter {0} ({1:X8}), Property: {2} ({3:X8})",
						Utils.removeNewlines(propMethod),
						propMethod.MethodDef.MDToken.ToInt32(),
						Utils.removeNewlines(propDef.PropertyDef),
						propDef.PropertyDef.MDToken.ToInt32());
			propDef.PropertyDef.SetMethod = propMethod.MethodDef;
			propDef.SetMethod = propMethod;
			propMethod.Property = propDef;
			return propDef;
		}

		MPropertyDef createProperty(MTypeDef ownerType, string name, TypeSig propType, MethodDef getter, MethodDef setter) {
			if (string.IsNullOrEmpty(name) || propType.ElementType == ElementType.Void)
				return null;
			var newSig = createPropertySig(getter, propType, true) ?? createPropertySig(setter, propType, false);
			if (newSig == null)
				return null;
			var newProp = ownerType.Module.ModuleDefMD.UpdateRowId(new PropertyDefUser(name, newSig, 0));
			newProp.GetMethod = getter;
			newProp.SetMethod = setter;
			var propDef = ownerType.findAny(newProp);
			if (propDef != null)
				return propDef;

			propDef = ownerType.create(newProp);
			memberInfos.add(propDef);
			if (isVerbose)
				Logger.v("Restoring property: {0}", Utils.removeNewlines(newProp));
			return propDef;
		}

		static PropertySig createPropertySig(MethodDef method, TypeSig propType, bool isGetter) {
			if (method == null)
				return null;
			var sig = method.MethodSig;
			if (sig == null)
				return null;

			var newSig = new PropertySig(sig.HasThis, propType);
			newSig.GenParamCount = sig.GenParamCount;

			int count = sig.Params.Count;
			if (!isGetter)
				count--;
			for (int i = 0; i < count; i++)
				newSig.Params.Add(sig.Params[i]);

			return newSig;
		}

		void restoreVirtualEvents(IEnumerable<MethodNameGroup> allGroups) {
			if (!RestoreEvents)
				return;
			foreach (var group in allGroups) {
				restoreVirtualEvents(group);
				restoreExplicitVirtualEvents(group);
			}
		}

		enum EventMethodType {
			None,
			Other,
			Adder,
			Remover,
			Raiser,
		}

		void restoreExplicitVirtualEvents(MethodNameGroup group) {
			if (group.Methods.Count != 1)
				return;
			var eventMethod = group.Methods[0];
			if (eventMethod.Event != null)
				return;
			if (eventMethod.MethodDef.Overrides.Count == 0)
				return;

			MMethodDef overriddenMethod;
			var theEvent = getOverriddenEvent(eventMethod, out overriddenMethod);
			if (theEvent == null)
				return;

			createEvent(theEvent, eventMethod, getEventMethodType(overriddenMethod), getOverridePrefix(group, eventMethod));
		}

		void restoreVirtualEvents(MethodNameGroup group) {
			if (group.Methods.Count <= 1 || !group.hasEvent())
				return;

			EventMethodType methodType = EventMethodType.None;
			MEventDef evt = null;
			List<MMethodDef> missingEvents = null;
			foreach (var method in group.Methods) {
				if (method.Event == null) {
					if (missingEvents == null)
						missingEvents = new List<MMethodDef>();
					missingEvents.Add(method);
				}
				else if (evt == null || !method.Owner.HasModule) {
					evt = method.Event;
					methodType = getEventMethodType(method);
				}
			}
			if (evt == null)
				return;	// Should never happen
			if (missingEvents == null)
				return;

			foreach (var method in missingEvents)
				createEvent(evt, method, methodType, "");
		}

		void createEvent(MEventDef eventDef, MMethodDef methodDef, EventMethodType methodType, string overridePrefix) {
			if (!methodDef.Owner.HasModule)
				return;

			var newEventName = overridePrefix + eventDef.EventDef.Name;
			switch (methodType) {
			case EventMethodType.Adder:
				createEventAdder(newEventName, methodDef);
				break;
			case EventMethodType.Remover:
				createEventRemover(newEventName, methodDef);
				break;
			}
		}

		static EventMethodType getEventMethodType(MMethodDef method) {
			var evt = method.Event;
			if (evt == null)
				return EventMethodType.None;
			if (evt.AddMethod == method)
				return EventMethodType.Adder;
			if (evt.RemoveMethod == method)
				return EventMethodType.Remover;
			if (evt.RaiseMethod == method)
				return EventMethodType.Raiser;
			return EventMethodType.Other;
		}

		void restoreEventsFromNames(IEnumerable<MethodNameGroup> allGroups) {
			if (!RestoreEventsFromNames)
				return;

			foreach (var group in allGroups) {
				var groupMethod = group.Methods[0];
				var methodName = groupMethod.MethodDef.Name.String;
				bool onlyRenamableMethods = !group.hasNonRenamableMethod();

				if (Utils.StartsWith(methodName, "add_", StringComparison.Ordinal)) {
					var eventName = methodName.Substring(4);
					foreach (var method in group.Methods) {
						if (onlyRenamableMethods && !memberInfos.type(method.Owner).NameChecker.isValidEventName(eventName))
							continue;
						createEventAdder(eventName, method);
					}
				}
				else if (Utils.StartsWith(methodName, "remove_", StringComparison.Ordinal)) {
					var eventName = methodName.Substring(7);
					foreach (var method in group.Methods) {
						if (onlyRenamableMethods && !memberInfos.type(method.Owner).NameChecker.isValidEventName(eventName))
							continue;
						createEventRemover(eventName, method);
					}
				}
			}

			foreach (var type in modules.AllTypes) {
				foreach (var method in type.AllMethodsSorted) {
					if (method.isVirtual())
						continue;	// Virtual methods are in allGroups, so already fixed above
					if (method.Event != null)
						continue;
					var methodName = method.MethodDef.Name.String;
					if (Utils.StartsWith(methodName, "add_", StringComparison.Ordinal))
						createEventAdder(methodName.Substring(4), method);
					else if (Utils.StartsWith(methodName, "remove_", StringComparison.Ordinal))
						createEventRemover(methodName.Substring(7), method);
				}
			}
		}

		MEventDef createEventAdder(string name, MMethodDef eventMethod) {
			if (string.IsNullOrEmpty(name))
				return null;
			var ownerType = eventMethod.Owner;
			if (!ownerType.HasModule)
				return null;
			if (eventMethod.Event != null)
				return null;

			var method = eventMethod.MethodDef;
			var eventDef = createEvent(ownerType, name, getEventType(method));
			if (eventDef == null)
				return null;
			if (eventDef.AddMethod != null)
				return null;
			if (isVerbose)
				Logger.v("Restoring event adder {0} ({1:X8}), Event: {2} ({3:X8})",
						Utils.removeNewlines(eventMethod),
						eventMethod.MethodDef.MDToken.ToInt32(),
						Utils.removeNewlines(eventDef.EventDef),
						eventDef.EventDef.MDToken.ToInt32());
			eventDef.EventDef.AddMethod = eventMethod.MethodDef;
			eventDef.AddMethod = eventMethod;
			eventMethod.Event = eventDef;
			return eventDef;
		}

		MEventDef createEventRemover(string name, MMethodDef eventMethod) {
			if (string.IsNullOrEmpty(name))
				return null;
			var ownerType = eventMethod.Owner;
			if (!ownerType.HasModule)
				return null;
			if (eventMethod.Event != null)
				return null;

			var method = eventMethod.MethodDef;
			var eventDef = createEvent(ownerType, name, getEventType(method));
			if (eventDef == null)
				return null;
			if (eventDef.RemoveMethod != null)
				return null;
			if (isVerbose)
				Logger.v("Restoring event remover {0} ({1:X8}), Event: {2} ({3:X8})",
						Utils.removeNewlines(eventMethod),
						eventMethod.MethodDef.MDToken.ToInt32(),
						Utils.removeNewlines(eventDef.EventDef),
						eventDef.EventDef.MDToken.ToInt32());
			eventDef.EventDef.RemoveMethod = eventMethod.MethodDef;
			eventDef.RemoveMethod = eventMethod;
			eventMethod.Event = eventDef;
			return eventDef;
		}

		TypeSig getEventType(IMethod method) {
			if (DotNetUtils.hasReturnValue(method))
				return null;
			var sig = method.MethodSig;
			if (sig == null || sig.Params.Count != 1)
				return null;
			return sig.Params[0];
		}

		MEventDef createEvent(MTypeDef ownerType, string name, TypeSig eventType) {
			if (string.IsNullOrEmpty(name) || eventType == null || eventType.ElementType == ElementType.Void)
				return null;
			var newEvent = ownerType.Module.ModuleDefMD.UpdateRowId(new EventDefUser(name, eventType.ToTypeDefOrRef(), 0));
			var eventDef = ownerType.findAny(newEvent);
			if (eventDef != null)
				return eventDef;

			eventDef = ownerType.create(newEvent);
			memberInfos.add(eventDef);
			if (isVerbose)
				Logger.v("Restoring event: {0}", Utils.removeNewlines(newEvent));
			return eventDef;
		}

		void prepareRenameMemberDefs(MethodNameGroups groups) {
			if (isVerbose)
				Logger.v("Renaming member definitions #1");

			prepareRenameEntryPoints();

			var virtualMethods = new GroupHelper(memberInfos, modules.AllTypes);
			var ifaceMethods = new GroupHelper(memberInfos, modules.AllTypes);
			var propMethods = new GroupHelper(memberInfos, modules.AllTypes);
			var eventMethods = new GroupHelper(memberInfos, modules.AllTypes);
			foreach (var group in getSorted(groups)) {
				if (group.hasNonRenamableMethod())
					continue;
				else if (group.hasGetterOrSetterPropertyMethod() && getPropertyMethodType(group.Methods[0]) != PropertyMethodType.Other)
					propMethods.add(group);
				else if (group.hasAddRemoveOrRaiseEventMethod())
					eventMethods.add(group);
				else if (group.hasInterfaceMethod())
					ifaceMethods.add(group);
				else
					virtualMethods.add(group);
			}

			var prepareHelper = new PrepareHelper(memberInfos, modules.AllTypes);
			prepareHelper.prepare((info) => info.prepareRenameMembers());

			prepareHelper.prepare((info) => info.prepareRenamePropsAndEvents());
			propMethods.visitAll((group) => prepareRenameProperty(group, false));
			eventMethods.visitAll((group) => prepareRenameEvent(group, false));
			propMethods.visitAll((group) => prepareRenameProperty(group, true));
			eventMethods.visitAll((group) => prepareRenameEvent(group, true));

			foreach (var typeDef in modules.AllTypes)
				memberInfos.type(typeDef).initializeEventHandlerNames();

			prepareHelper.prepare((info) => info.prepareRenameMethods());
			ifaceMethods.visitAll((group) => prepareRenameVirtualMethods(group, "imethod_", false));
			virtualMethods.visitAll((group) => prepareRenameVirtualMethods(group, "vmethod_", false));
			ifaceMethods.visitAll((group) => prepareRenameVirtualMethods(group, "imethod_", true));
			virtualMethods.visitAll((group) => prepareRenameVirtualMethods(group, "vmethod_", true));

			restoreMethodArgs(groups);

			foreach (var typeDef in modules.AllTypes)
				memberInfos.type(typeDef).prepareRenameMethods2();
		}

		void restoreMethodArgs(MethodNameGroups groups) {
			foreach (var group in groups.getAllGroups()) {
				if (group.Methods[0].VisibleParameterCount == 0)
					continue;

				var argNames = getValidArgNames(group);

				foreach (var method in group.Methods) {
					if (!method.Owner.HasModule)
						continue;
					var nameChecker = method.Owner.Module.ObfuscatedFile.NameChecker;

					for (int i = 0; i < argNames.Length; i++) {
						var argName = argNames[i];
						if (argName == null || !nameChecker.isValidMethodArgName(argName))
							continue;
						var info = memberInfos.param(method.ParamDefs[i]);
						if (nameChecker.isValidMethodArgName(info.oldName))
							continue;
						info.newName = argName;
					}
				}
			}
		}

		string[] getValidArgNames(MethodNameGroup group) {
			var methods = new List<MMethodDef>(group.Methods);
			foreach (var method in group.Methods) {
				foreach (var ovrd in method.MethodDef.Overrides) {
					var overrideRef = ovrd.MethodDeclaration;
					var overrideDef = modules.resolveMethod(overrideRef);
					if (overrideDef == null) {
						var typeDef = modules.resolveType(overrideRef.DeclaringType) ?? modules.resolveOther(overrideRef.DeclaringType);
						if (typeDef == null)
							continue;
						overrideDef = typeDef.findMethod(overrideRef);
						if (overrideDef == null)
							continue;
					}
					if (overrideDef.VisibleParameterCount != method.VisibleParameterCount)
						continue;
					methods.Add(overrideDef);
				}
			}

			var argNames = new string[group.Methods[0].ParamDefs.Count];
			foreach (var method in methods) {
				var nameChecker = !method.Owner.HasModule ? null : method.Owner.Module.ObfuscatedFile.NameChecker;
				for (int i = 0; i < argNames.Length; i++) {
					var argName = method.ParamDefs[i].ParameterDef.Name;
					if (nameChecker == null || nameChecker.isValidMethodArgName(argName))
						argNames[i] = argName;
				}
			}
			return argNames;
		}

		class PrepareHelper {
			Dictionary<MTypeDef, bool> prepareMethodCalled = new Dictionary<MTypeDef, bool>();
			MemberInfos memberInfos;
			Action<TypeInfo> func;
			IEnumerable<MTypeDef> allTypes;

			public PrepareHelper(MemberInfos memberInfos, IEnumerable<MTypeDef> allTypes) {
				this.memberInfos = memberInfos;
				this.allTypes = allTypes;
			}

			public void prepare(Action<TypeInfo> func) {
				this.func = func;
				prepareMethodCalled.Clear();
				foreach (var typeDef in allTypes)
					prepare(typeDef);
			}

			void prepare(MTypeDef type) {
				if (prepareMethodCalled.ContainsKey(type))
					return;
				prepareMethodCalled[type] = true;

				foreach (var ifaceInfo in type.interfaces)
					prepare(ifaceInfo.typeDef);
				if (type.baseType != null)
					prepare(type.baseType.typeDef);

				TypeInfo info;
				if (memberInfos.tryGetType(type, out info))
					func(info);
			}
		}

		static List<MethodNameGroup> getSorted(MethodNameGroups groups) {
			var allGroups = new List<MethodNameGroup>(groups.getAllGroups());
			allGroups.Sort((a, b) => b.Count.CompareTo(a.Count));
			return allGroups;
		}

		class GroupHelper {
			MemberInfos memberInfos;
			Dictionary<MTypeDef, bool> visited = new Dictionary<MTypeDef, bool>();
			Dictionary<MMethodDef, MethodNameGroup> methodToGroup;
			List<MethodNameGroup> groups = new List<MethodNameGroup>();
			IEnumerable<MTypeDef> allTypes;
			Action<MethodNameGroup> func;

			public GroupHelper(MemberInfos memberInfos, IEnumerable<MTypeDef> allTypes) {
				this.memberInfos = memberInfos;
				this.allTypes = allTypes;
			}

			public void add(MethodNameGroup group) {
				groups.Add(group);
			}

			public void visitAll(Action<MethodNameGroup> func) {
				this.func = func;
				visited.Clear();

				methodToGroup = new Dictionary<MMethodDef, MethodNameGroup>();
				foreach (var group in groups) {
					foreach (var method in group.Methods)
						methodToGroup[method] = group;
				}

				foreach (var type in allTypes)
					visit(type);
			}

			void visit(MTypeDef type) {
				if (visited.ContainsKey(type))
					return;
				visited[type] = true;

				if (type.baseType != null)
					visit(type.baseType.typeDef);
				foreach (var ifaceInfo in type.interfaces)
					visit(ifaceInfo.typeDef);

				TypeInfo info;
				if (!memberInfos.tryGetType(type, out info))
					return;

				foreach (var method in type.AllMethodsSorted) {
					MethodNameGroup group;
					if (!methodToGroup.TryGetValue(method, out group))
						continue;
					foreach (var m in group.Methods)
						methodToGroup.Remove(m);
					func(group);
				}
			}
		}

		static readonly Regex removeGenericsArityRegex = new Regex(@"`[0-9]+");
		static string getOverridePrefix(MethodNameGroup group, MMethodDef method) {
			if (method == null || method.MethodDef.Overrides.Count == 0)
				return "";
			if (group.Methods.Count > 1) {
				// Don't use an override prefix if the group has an iface method.
				foreach (var m in group.Methods) {
					if (m.Owner.TypeDef.IsInterface)
						return "";
				}
			}
			var overrideMethod = method.MethodDef.Overrides[0].MethodDeclaration;
			var name = overrideMethod.DeclaringType.FullName.Replace('/', '.');
			name = removeGenericsArityRegex.Replace(name, "");
			return name + ".";
		}

		static string getRealName(string name) {
			int index = name.LastIndexOf('.');
			if (index < 0)
				return name;
			return name.Substring(index + 1);
		}

		void prepareRenameEvent(MethodNameGroup group, bool renameOverrides) {
			string methodPrefix, overridePrefix;
			var eventName = prepareRenameEvent(group, renameOverrides, out overridePrefix, out methodPrefix);
			if (eventName == null)
				return;

			var methodName = overridePrefix + methodPrefix + eventName;
			foreach (var method in group.Methods)
				memberInfos.method(method).rename(methodName);
		}

		string prepareRenameEvent(MethodNameGroup group, bool renameOverrides, out string overridePrefix, out string methodPrefix) {
			var eventMethod = getEventMethod(group);
			if (eventMethod == null)
				throw new ApplicationException("No events found");

			var eventDef = eventMethod.Event;
			if (eventMethod == eventDef.AddMethod)
				methodPrefix = "add_";
			else if (eventMethod == eventDef.RemoveMethod)
				methodPrefix = "remove_";
			else if (eventMethod == eventDef.RaiseMethod)
				methodPrefix = "raise_";
			else
				methodPrefix = "";

			overridePrefix = getOverridePrefix(group, eventMethod);
			if (renameOverrides && overridePrefix == "")
				return null;
			if (!renameOverrides && overridePrefix != "")
				return null;

			string newEventName, oldEventName;
			var eventInfo = memberInfos.evt(eventDef);

			bool mustUseOldEventName = false;
			if (overridePrefix == "")
				oldEventName = eventInfo.oldName;
			else {
				var overriddenEventDef = getOverriddenEvent(eventMethod);
				if (overriddenEventDef == null)
					oldEventName = getRealName(eventInfo.oldName);
				else {
					mustUseOldEventName = true;
					EventInfo info;
					if (memberInfos.tryGetEvent(overriddenEventDef, out info))
						oldEventName = getRealName(info.newName);
					else
						oldEventName = getRealName(overriddenEventDef.EventDef.Name.String);
				}
			}

			if (eventInfo.renamed)
				newEventName = getRealName(eventInfo.newName);
			else if (mustUseOldEventName || eventDef.Owner.Module.ObfuscatedFile.NameChecker.isValidEventName(oldEventName))
				newEventName = oldEventName;
			else {
				mergeStateHelper.merge(MergeStateFlags.Events, group);
				newEventName = getAvailableName("Event_", false, group, (group2, newName) => isEventAvailable(group2, newName));
			}

			var newEventNameWithPrefix = overridePrefix + newEventName;
			foreach (var method in group.Methods) {
				if (method.Event != null) {
					memberInfos.evt(method.Event).rename(newEventNameWithPrefix);
					var ownerInfo = memberInfos.type(method.Owner);
					ownerInfo.variableNameState.addEventName(newEventName);
					ownerInfo.variableNameState.addEventName(newEventNameWithPrefix);
				}
			}

			return newEventName;
		}

		MEventDef getOverriddenEvent(MMethodDef overrideMethod) {
			MMethodDef overriddenMethod;
			return getOverriddenEvent(overrideMethod, out overriddenMethod);
		}

		MEventDef getOverriddenEvent(MMethodDef overrideMethod, out MMethodDef overriddenMethod) {
			var theMethod = overrideMethod.MethodDef.Overrides[0].MethodDeclaration;
			overriddenMethod = modules.resolveMethod(theMethod);
			if (overriddenMethod != null)
				return overriddenMethod.Event;

			var extType = theMethod.DeclaringType;
			if (extType == null)
				return null;
			var extTypeDef = modules.resolveOther(extType);
			if (extTypeDef == null)
				return null;
			overriddenMethod = extTypeDef.findMethod(theMethod);
			if (overriddenMethod != null)
				return overriddenMethod.Event;

			return null;
		}

		MMethodDef getEventMethod(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (method.Event != null)
					return method;
			}
			return null;
		}

		void prepareRenameProperty(MethodNameGroup group, bool renameOverrides) {
			string overridePrefix;
			var propName = prepareRenameProperty(group, renameOverrides, out overridePrefix);
			if (propName == null)
				return;

			string methodPrefix;
			switch (getPropertyMethodType(group.Methods[0])) {
			case PropertyMethodType.Getter:
				methodPrefix = "get_";
				break;
			case PropertyMethodType.Setter:
				methodPrefix = "set_";
				break;
			default:
				throw new ApplicationException("Invalid property type");
			}

			var methodName = overridePrefix + methodPrefix + propName;
			foreach (var method in group.Methods)
				memberInfos.method(method).rename(methodName);
		}

		string prepareRenameProperty(MethodNameGroup group, bool renameOverrides, out string overridePrefix) {
			var propMethod = getPropertyMethod(group);
			if (propMethod == null)
				throw new ApplicationException("No properties found");

			overridePrefix = getOverridePrefix(group, propMethod);

			if (renameOverrides && overridePrefix == "")
				return null;
			if (!renameOverrides && overridePrefix != "")
				return null;

			string newPropName, oldPropName;
			var propDef = propMethod.Property;
			var propInfo = memberInfos.prop(propDef);

			bool mustUseOldPropName = false;
			if (overridePrefix == "")
				oldPropName = propInfo.oldName;
			else {
				var overriddenPropDef = getOverriddenProperty(propMethod);
				if (overriddenPropDef == null)
					oldPropName = getRealName(propInfo.oldName);
				else {
					mustUseOldPropName = true;
					PropertyInfo info;
					if (memberInfos.tryGetProperty(overriddenPropDef, out info))
						oldPropName = getRealName(info.newName);
					else
						oldPropName = getRealName(overriddenPropDef.PropertyDef.Name.String);
				}
			}

			if (propInfo.renamed)
				newPropName = getRealName(propInfo.newName);
			else if (mustUseOldPropName || propDef.Owner.Module.ObfuscatedFile.NameChecker.isValidPropertyName(oldPropName))
				newPropName = oldPropName;
			else if (isItemProperty(group))
				newPropName = "Item";
			else {
				bool trySameName = true;
				var propPrefix = getSuggestedPropertyName(group);
				if (propPrefix == null) {
					trySameName = false;
					propPrefix = getNewPropertyNamePrefix(group);
				}
				mergeStateHelper.merge(MergeStateFlags.Properties, group);
				newPropName = getAvailableName(propPrefix, trySameName, group, (group2, newName) => isPropertyAvailable(group2, newName));
			}

			var newPropNameWithPrefix = overridePrefix + newPropName;
			foreach (var method in group.Methods) {
				if (method.Property != null) {
					memberInfos.prop(method.Property).rename(newPropNameWithPrefix);
					var ownerInfo = memberInfos.type(method.Owner);
					ownerInfo.variableNameState.addPropertyName(newPropName);
					ownerInfo.variableNameState.addPropertyName(newPropNameWithPrefix);
				}
			}

			return newPropName;
		}

		bool isItemProperty(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (method.Property != null && method.Property.isItemProperty())
					return true;
			}
			return false;
		}

		MPropertyDef getOverriddenProperty(MMethodDef overrideMethod) {
			var theMethod = overrideMethod.MethodDef.Overrides[0].MethodDeclaration;
			var overriddenMethod = modules.resolveMethod(theMethod);
			if (overriddenMethod != null)
				return overriddenMethod.Property;

			var extType = theMethod.DeclaringType;
			if (extType == null)
				return null;
			var extTypeDef = modules.resolveOther(extType);
			if (extTypeDef == null)
				return null;
			var theMethodDef = extTypeDef.findMethod(theMethod);
			if (theMethodDef != null)
				return theMethodDef.Property;

			return null;
		}

		MMethodDef getPropertyMethod(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (method.Property != null)
					return method;
			}
			return null;
		}

		string getSuggestedPropertyName(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (method.Property == null)
					continue;
				var info = memberInfos.prop(method.Property);
				if (info.suggestedName != null)
					return info.suggestedName;
			}
			return null;
		}

		string getNewPropertyNamePrefix(MethodNameGroup group) {
			const string defaultVal = "Prop_";

			var propType = getPropertyType(group);
			if (propType == null)
				return defaultVal;

			var elementType = propType.ScopeType.ToTypeSig(false).RemovePinnedAndModifiers();
			if (propType is GenericInstSig || elementType is GenericSig)
				return defaultVal;

			var prefix = getPrefix(propType);

			string name = elementType.TypeName;
			int i;
			if ((i = name.IndexOf('`')) >= 0)
				name = name.Substring(0, i);
			if ((i = name.LastIndexOf('.')) >= 0)
				name = name.Substring(i + 1);
			if (name == "")
				return defaultVal;

			return prefix.ToUpperInvariant() + upperFirst(name) + "_";
		}

		static string upperFirst(string s) {
			return s.Substring(0, 1).ToUpperInvariant() + s.Substring(1);
		}

		static string getPrefix(TypeSig typeRef) {
			string prefix = "";
			typeRef = typeRef.RemovePinnedAndModifiers();
			while (typeRef is PtrSig) {
				typeRef = typeRef.Next;
				prefix += "p";
			}
			return prefix;
		}

		enum PropertyMethodType {
			Other,
			Getter,
			Setter,
		}

		static PropertyMethodType getPropertyMethodType(MMethodDef method) {
			if (DotNetUtils.hasReturnValue(method.MethodDef))
				return PropertyMethodType.Getter;
			if (method.VisibleParameterCount > 0)
				return PropertyMethodType.Setter;
			return PropertyMethodType.Other;
		}

		// Returns property type, or null if not all methods have the same type
		TypeSig getPropertyType(MethodNameGroup group) {
			var methodType = getPropertyMethodType(group.Methods[0]);
			if (methodType == PropertyMethodType.Other)
				return null;

			TypeSig type = null;
			foreach (var propMethod in group.Methods) {
				TypeSig propType;
				if (methodType == PropertyMethodType.Setter)
					propType = propMethod.ParamDefs[propMethod.ParamDefs.Count - 1].ParameterDef.Type;
				else
					propType = propMethod.MethodDef.MethodSig.GetRetType();
				if (type == null)
					type = propType;
				else if (!new SigComparer().Equals(type, propType))
					return null;
			}
			return type;
		}

		MMethodDef getOverrideMethod(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (method.MethodDef.Overrides.Count > 0)
					return method;
			}
			return null;
		}

		void prepareRenameVirtualMethods(MethodNameGroup group, string namePrefix, bool renameOverrides) {
			if (!hasInvalidMethodName(group))
				return;

			if (hasDelegateOwner(group)) {
				switch (group.Methods[0].MethodDef.Name.String) {
				case "Invoke":
				case "BeginInvoke":
				case "EndInvoke":
					return;
				}
			}

			var overrideMethod = getOverrideMethod(group);
			var overridePrefix = getOverridePrefix(group, overrideMethod);
			if (renameOverrides && overridePrefix == "")
				return;
			if (!renameOverrides && overridePrefix != "")
				return;

			string newMethodName;
			if (overridePrefix != "") {
				var overrideInfo = memberInfos.method(overrideMethod);
				var overriddenMethod = getOverriddenMethod(overrideMethod);
				if (overriddenMethod == null)
					newMethodName = getRealName(overrideMethod.MethodDef.Overrides[0].MethodDeclaration.Name.String);
				else
					newMethodName = getRealName(memberInfos.method(overriddenMethod).newName);
			}
			else {
				newMethodName = getSuggestedMethodName(group);
				if (newMethodName == null) {
					mergeStateHelper.merge(MergeStateFlags.Methods, group);
					newMethodName = getAvailableName(namePrefix, false, group, (group2, newName) => isMethodAvailable(group2, newName));
				}
			}

			var newMethodNameWithPrefix = overridePrefix + newMethodName;
			foreach (var method in group.Methods)
				memberInfos.type(method.Owner).renameMethod(method, newMethodNameWithPrefix);
		}

		[Flags]
		enum MergeStateFlags {
			None = 0,
			Methods = 0x1,
			Properties = 0x2,
			Events = 0x4,
		}

		class MergeStateHelper {
			MemberInfos memberInfos;
			MergeStateFlags flags;
			Dictionary<MTypeDef, bool> visited = new Dictionary<MTypeDef, bool>();

			public MergeStateHelper(MemberInfos memberInfos) {
				this.memberInfos = memberInfos;
			}

			public void merge(MergeStateFlags flags, MethodNameGroup group) {
				this.flags = flags;
				visited.Clear();
				foreach (var method in group.Methods)
					merge(method.Owner);
			}

			void merge(MTypeDef type) {
				if (visited.ContainsKey(type))
					return;
				visited[type] = true;

				TypeInfo info;
				if (!memberInfos.tryGetType(type, out info))
					return;

				if (type.baseType != null)
					merge(type.baseType.typeDef);
				foreach (var ifaceInfo in type.interfaces)
					merge(ifaceInfo.typeDef);

				if (type.baseType != null)
					merge(info, type.baseType.typeDef);
				foreach (var ifaceInfo in type.interfaces)
					merge(info, ifaceInfo.typeDef);
			}

			void merge(TypeInfo info, MTypeDef other) {
				TypeInfo otherInfo;
				if (!memberInfos.tryGetType(other, out otherInfo))
					return;

				if ((flags & MergeStateFlags.Methods) != MergeStateFlags.None)
					info.variableNameState.mergeMethods(otherInfo.variableNameState);
				if ((flags & MergeStateFlags.Properties) != MergeStateFlags.None)
					info.variableNameState.mergeProperties(otherInfo.variableNameState);
				if ((flags & MergeStateFlags.Events) != MergeStateFlags.None)
					info.variableNameState.mergeEvents(otherInfo.variableNameState);
			}
		}

		MMethodDef getOverriddenMethod(MMethodDef overrideMethod) {
			return modules.resolveMethod(overrideMethod.MethodDef.Overrides[0].MethodDeclaration);
		}

		string getSuggestedMethodName(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				var info = memberInfos.method(method);
				if (info.suggestedName != null)
					return info.suggestedName;
			}
			return null;
		}

		bool hasInvalidMethodName(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				var typeInfo = memberInfos.type(method.Owner);
				var methodInfo = memberInfos.method(method);
				if (!typeInfo.NameChecker.isValidMethodName(methodInfo.oldName))
					return true;
			}
			return false;
		}

		static string getAvailableName(string prefix, bool tryWithoutZero, MethodNameGroup group, Func<MethodNameGroup, string, bool> checkAvailable) {
			for (int i = 0; ; i++) {
				string newName = i == 0 && tryWithoutZero ? prefix : prefix + i;
				if (checkAvailable(group, newName))
					return newName;
			}
		}

		bool isMethodAvailable(MethodNameGroup group, string methodName) {
			foreach (var method in group.Methods) {
				if (memberInfos.type(method.Owner).variableNameState.isMethodNameUsed(methodName))
					return false;
			}
			return true;
		}

		bool isPropertyAvailable(MethodNameGroup group, string methodName) {
			foreach (var method in group.Methods) {
				if (memberInfos.type(method.Owner).variableNameState.isPropertyNameUsed(methodName))
					return false;
			}
			return true;
		}

		bool isEventAvailable(MethodNameGroup group, string methodName) {
			foreach (var method in group.Methods) {
				if (memberInfos.type(method.Owner).variableNameState.isEventNameUsed(methodName))
					return false;
			}
			return true;
		}

		bool hasDelegateOwner(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (isDelegateClass.check(method.Owner))
					return true;
			}
			return false;
		}

		void prepareRenameEntryPoints() {
			foreach (var module in modules.TheModules) {
				var entryPoint = module.ModuleDefMD.EntryPoint;
				if (entryPoint == null)
					continue;
				var methodDef = modules.resolveMethod(entryPoint);
				if (methodDef == null) {
					Logger.w(string.Format("Could not find entry point. Module: {0}, Method: {1}", module.ModuleDefMD.Location, Utils.removeNewlines(entryPoint)));
					continue;
				}
				if (!methodDef.isStatic())
					continue;
				memberInfos.method(methodDef).suggestedName = "Main";
				if (methodDef.ParamDefs.Count == 1) {
					var paramDef = methodDef.ParamDefs[0];
					var type = paramDef.ParameterDef.Type;
					if (type.FullName == "System.String[]")
						memberInfos.param(paramDef).newName = "args";
				}
			}
		}
	}
}
