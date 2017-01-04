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
using dnlib.DotNet.Resources;
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

			WarnIfXaml(files);

			modules = new Modules(deobfuscatorContext);
			isDelegateClass = new DerivedFrom(delegateClasses);
			mergeStateHelper = new MergeStateHelper(memberInfos);

			foreach (var file in files)
				modules.Add(new Module(file));
		}

		static void WarnIfXaml(IEnumerable<IObfuscatedFile> files) {
			foreach (var file in files) {
				foreach (var tmp in file.ModuleDefMD.Resources) {
					var rsrc = tmp as EmbeddedResource;
					if (rsrc == null)
						continue;
					if (UTF8String.IsNullOrEmpty(rsrc.Name))
						continue;
					if (!rsrc.Name.String.EndsWith(".g.resources"))
						continue;
					if (!HasXamlFiles(file.ModuleDefMD, rsrc))
						continue;

					Logger.w("File '{0}' contains XAML which isn't supported. Use --dont-rename.", file.Filename);
					return;
				}
			}
		}

		static bool HasXamlFiles(ModuleDef module, EmbeddedResource rsrc) {
			try {
				rsrc.Data.Position = 0;
				var rsrcSet = ResourceReader.Read(module, rsrc.Data);
				foreach (var elem in rsrcSet.ResourceElements) {
					if (elem.Name.EndsWith(".baml") || elem.Name.EndsWith(".xaml"))
						return true;
				}
			}
			catch {
			}
			return false;
		}

		public void Rename() {
			if (modules.Empty)
				return;
			isVerbose = !Logger.Instance.IgnoresEvent(LoggerEvent.Verbose);
			Logger.n("Renaming all obfuscated symbols");

			modules.Initialize();
			RenameResourceKeys();
			var groups = modules.InitializeVirtualMembers();
			memberInfos.Initialize(modules);
			RenameTypeDefs();
			RenameTypeRefs();
			modules.OnTypesRenamed();
			RestorePropertiesAndEvents(groups);
			PrepareRenameMemberDefs(groups);
			RenameMemberDefs();
			RenameMemberRefs();
			RemoveUselessOverrides(groups);
			RenameResources();
			modules.CleanUp();
		}

		void RenameResourceKeys() {
			foreach (var module in modules.TheModules) {
				if (!module.ObfuscatedFile.RenameResourceKeys)
					continue;
				new ResourceKeysRenamer(module.ModuleDefMD, module.ObfuscatedFile.NameChecker).Rename();
			}
		}

		void RemoveUselessOverrides(MethodNameGroups groups) {
			foreach (var group in groups.GetAllGroups()) {
				foreach (var method in group.Methods) {
					if (!method.Owner.HasModule)
						continue;
					if (!method.IsPublic())
						continue;
					var overrides = method.MethodDef.Overrides;
					for (int i = 0; i < overrides.Count; i++) {
						var overrideMethod = overrides[i].MethodDeclaration;
						if (method.MethodDef.Name != overrideMethod.Name)
							continue;
						if (isVerbose)
							Logger.v("Removed useless override from method {0} ({1:X8}), override: {2:X8}",
									Utils.RemoveNewlines(method.MethodDef),
									method.MethodDef.MDToken.ToInt32(),
									overrideMethod.MDToken.ToInt32());
						overrides.RemoveAt(i);
						i--;
					}
				}
			}
		}

		void RenameTypeDefs() {
			if (isVerbose)
				Logger.v("Renaming obfuscated type definitions");

			foreach (var module in modules.TheModules) {
				if (module.ObfuscatedFile.RemoveNamespaceWithOneType)
					RemoveOneClassNamespaces(module);
			}

			var state = new TypeRenamerState();
			foreach (var type in modules.AllTypes)
				state.AddTypeName(memberInfos.Type(type).oldName);
			PrepareRenameTypes(modules.BaseTypes, state);
			FixClsTypeNames();
			RenameTypeDefs(modules.NonNestedTypes);
		}

		void RemoveOneClassNamespaces(Module module) {
			var nsToTypes = new Dictionary<string, List<MTypeDef>>(StringComparer.Ordinal);

			foreach (var typeDef in module.GetAllTypes()) {
				List<MTypeDef> list;
				var ns = typeDef.TypeDef.Namespace.String;
				if (string.IsNullOrEmpty(ns))
					continue;
				if (module.ObfuscatedFile.NameChecker.IsValidNamespaceName(ns))
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
					Logger.v("Removing namespace: {0}", Utils.RemoveNewlines(list[0].TypeDef.Namespace));
				foreach (var type in list)
					memberInfos.Type(type).newNamespace = "";
			}
		}

		void RenameTypeDefs(IEnumerable<MTypeDef> typeDefs) {
			Logger.Instance.Indent();
			foreach (var typeDef in typeDefs) {
				Rename(typeDef);
				RenameTypeDefs(typeDef.NestedTypes);
			}
			Logger.Instance.DeIndent();
		}

		void Rename(MTypeDef type) {
			var typeDef = type.TypeDef;
			var info = memberInfos.Type(type);

			if (isVerbose)
				Logger.v("Type: {0} ({1:X8})", Utils.RemoveNewlines(typeDef.FullName), typeDef.MDToken.ToUInt32());
			Logger.Instance.Indent();

			renameGenericParams2(type.GenericParams);

			if (RenameTypes && info.GotNewName()) {
				var old = typeDef.Name;
				typeDef.Name = info.newName;
				if (isVerbose)
					Logger.v("Name: {0} => {1}", Utils.RemoveNewlines(old), Utils.RemoveNewlines(typeDef.Name));
			}

			if (RenameNamespaces && info.newNamespace != null) {
				var old = typeDef.Namespace;
				typeDef.Namespace = info.newNamespace;
				if (isVerbose)
					Logger.v("Namespace: {0} => {1}", Utils.RemoveNewlines(old), Utils.RemoveNewlines(typeDef.Namespace));
			}

			Logger.Instance.DeIndent();
		}

		void renameGenericParams2(IEnumerable<MGenericParamDef> genericParams) {
			if (!RenameGenericParams)
				return;
			foreach (var param in genericParams) {
				var info = memberInfos.GenericParam(param);
				if (!info.GotNewName())
					continue;
				param.GenericParam.Name = info.newName;
				if (isVerbose)
					Logger.v("GenParam: {0} => {1}", Utils.RemoveNewlines(info.oldFullName), Utils.RemoveNewlines(param.GenericParam.FullName));
			}
		}

		void RenameMemberDefs() {
			if (isVerbose)
				Logger.v("Renaming member definitions #2");

			var allTypes = new List<MTypeDef>(modules.AllTypes);
			allTypes.Sort((a, b) => a.Index.CompareTo(b.Index));

			Logger.Instance.Indent();
			foreach (var typeDef in allTypes)
				RenameMembers(typeDef);
			Logger.Instance.DeIndent();
		}

		void RenameMembers(MTypeDef type) {
			var info = memberInfos.Type(type);

			if (isVerbose)
				Logger.v("Type: {0}", Utils.RemoveNewlines(info.type.TypeDef.FullName));
			Logger.Instance.Indent();

			RenameFields2(info);
			RenameProperties2(info);
			RenameEvents2(info);
			RenameMethods2(info);

			Logger.Instance.DeIndent();
		}

		void RenameFields2(TypeInfo info) {
			if (!RenameFields)
				return;
			bool isDelegateType = isDelegateClass.Check(info.type);
			foreach (var fieldDef in info.type.AllFieldsSorted) {
				var fieldInfo = memberInfos.Field(fieldDef);
				if (!fieldInfo.GotNewName())
					continue;
				if (isDelegateType && DontRenameDelegateFields)
					continue;
				fieldDef.FieldDef.Name = fieldInfo.newName;
				if (isVerbose)
					Logger.v("Field: {0} ({1:X8}) => {2}",
							Utils.RemoveNewlines(fieldInfo.oldFullName),
							fieldDef.FieldDef.MDToken.ToUInt32(),
							Utils.RemoveNewlines(fieldDef.FieldDef.FullName));
			}
		}

		void RenameProperties2(TypeInfo info) {
			if (!RenameProperties)
				return;
			foreach (var propDef in info.type.AllPropertiesSorted) {
				var propInfo = memberInfos.Property(propDef);
				if (!propInfo.GotNewName())
					continue;
				propDef.PropertyDef.Name = propInfo.newName;
				if (isVerbose)
					Logger.v("Property: {0} ({1:X8}) => {2}",
							Utils.RemoveNewlines(propInfo.oldFullName),
							propDef.PropertyDef.MDToken.ToUInt32(),
							Utils.RemoveNewlines(propDef.PropertyDef.FullName));
			}
		}

		void RenameEvents2(TypeInfo info) {
			if (!RenameEvents)
				return;
			foreach (var eventDef in info.type.AllEventsSorted) {
				var eventInfo = memberInfos.Event(eventDef);
				if (!eventInfo.GotNewName())
					continue;
				eventDef.EventDef.Name = eventInfo.newName;
				if (isVerbose)
					Logger.v("Event: {0} ({1:X8}) => {2}",
							Utils.RemoveNewlines(eventInfo.oldFullName),
							eventDef.EventDef.MDToken.ToUInt32(),
							Utils.RemoveNewlines(eventDef.EventDef.FullName));
			}
		}

		void RenameMethods2(TypeInfo info) {
			if (!RenameMethods && !RenameMethodArgs && !RenameGenericParams)
				return;
			foreach (var methodDef in info.type.AllMethodsSorted) {
				var methodInfo = memberInfos.Method(methodDef);
				if (isVerbose)
					Logger.v("Method {0} ({1:X8})", Utils.RemoveNewlines(methodInfo.oldFullName), methodDef.MethodDef.MDToken.ToUInt32());
				Logger.Instance.Indent();

				renameGenericParams2(methodDef.GenericParams);

				if (RenameMethods && methodInfo.GotNewName()) {
					methodDef.MethodDef.Name = methodInfo.newName;
					if (isVerbose)
						Logger.v("Name: {0} => {1}", Utils.RemoveNewlines(methodInfo.oldFullName), Utils.RemoveNewlines(methodDef.MethodDef.FullName));
				}

				if (RenameMethodArgs) {
					foreach (var param in methodDef.AllParamDefs) {
						var paramInfo = memberInfos.Param(param);
						if (!paramInfo.GotNewName())
							continue;
						if (!param.ParameterDef.HasParamDef) {
							if (DontCreateNewParamDefs)
								continue;
							param.ParameterDef.CreateParamDef();
						}
						param.ParameterDef.Name = paramInfo.newName;
						if (isVerbose) {
							if (param.IsReturnParameter)
								Logger.v("RetParam: {0} => {1}", Utils.RemoveNewlines(paramInfo.oldName), Utils.RemoveNewlines(paramInfo.newName));
							else
								Logger.v("Param ({0}/{1}): {2} => {3}", param.ParameterDef.MethodSigIndex + 1, methodDef.MethodDef.MethodSig.GetParamCount(), Utils.RemoveNewlines(paramInfo.oldName), Utils.RemoveNewlines(paramInfo.newName));
						}
					}
				}

				Logger.Instance.DeIndent();
			}
		}

		void RenameMemberRefs() {
			if (isVerbose)
				Logger.v("Renaming references to other definitions");
			foreach (var module in modules.TheModules) {
				if (modules.TheModules.Count > 1 && isVerbose)
					Logger.v("Renaming references to other definitions ({0})", module.Filename);
				Logger.Instance.Indent();
				foreach (var refToDef in module.MethodRefsToRename)
					refToDef.reference.Name = refToDef.definition.Name;
				foreach (var refToDef in module.FieldRefsToRename)
					refToDef.reference.Name = refToDef.definition.Name;
				foreach (var info in module.CustomAttributeFieldRefs)
					info.cattr.NamedArguments[info.index].Name = info.reference.Name;
				foreach (var info in module.CustomAttributePropertyRefs)
					info.cattr.NamedArguments[info.index].Name = info.reference.Name;
				Logger.Instance.DeIndent();
			}
		}

		void RenameResources() {
			if (isVerbose)
				Logger.v("Renaming resources");
			foreach (var module in modules.TheModules) {
				if (modules.TheModules.Count > 1 && isVerbose)
					Logger.v("Renaming resources ({0})", module.Filename);
				Logger.Instance.Indent();
				RenameResources(module);
				Logger.Instance.DeIndent();
			}
		}

		void RenameResources(Module module) {
			var renamedTypes = new List<TypeInfo>();
			foreach (var type in module.GetAllTypes()) {
				var info = memberInfos.Type(type);
				if (info.oldFullName != info.type.TypeDef.FullName)
					renamedTypes.Add(info);
			}
			if (renamedTypes.Count == 0)
				return;

			new ResourceRenamer(module).Rename(renamedTypes);
		}

		// Make sure the renamed types are using valid CLS names. That means renaming all
		// generic types from eg. Class1 to Class1`2. If we don't do this, some decompilers
		// (eg. ILSpy v1.0) won't produce correct output.
		void FixClsTypeNames() {
			foreach (var type in modules.NonNestedTypes)
				FixClsTypeNames(null, type);
		}

		void FixClsTypeNames(MTypeDef nesting, MTypeDef nested) {
			int nestingCount = nesting == null ? 0 : nesting.GenericParams.Count;
			int arity = nested.GenericParams.Count - nestingCount;
			var nestedInfo = memberInfos.Type(nested);
			if (nestedInfo.renamed && arity > 0)
				nestedInfo.newName += "`" + arity;
			foreach (var nestedType in nested.NestedTypes)
				FixClsTypeNames(nested, nestedType);
		}

		void PrepareRenameTypes(IEnumerable<MTypeDef> types, TypeRenamerState state) {
			foreach (var typeDef in types) {
				memberInfos.Type(typeDef).PrepareRenameTypes(state);
				PrepareRenameTypes(typeDef.derivedTypes, state);
			}
		}

		void RenameTypeRefs() {
			if (isVerbose)
				Logger.v("Renaming references to type definitions");
			var theModules = modules.TheModules;
			foreach (var module in theModules) {
				if (theModules.Count > 1 && isVerbose)
					Logger.v("Renaming references to type definitions ({0})", module.Filename);
				Logger.Instance.Indent();
				foreach (var refToDef in module.TypeRefsToRename) {
					refToDef.reference.Name = refToDef.definition.Name;
					refToDef.reference.Namespace = refToDef.definition.Namespace;
				}
				Logger.Instance.DeIndent();
			}
		}

		void RestorePropertiesAndEvents(MethodNameGroups groups) {
			var allGroups = groups.GetAllGroups();
			RestoreVirtualProperties(allGroups);
			RestorePropertiesFromNames2(allGroups);
			ResetVirtualPropertyNames(allGroups);
			RestoreVirtualEvents(allGroups);
			RestoreEventsFromNames2(allGroups);
			ResetVirtualEventNames(allGroups);
		}

		void ResetVirtualPropertyNames(IEnumerable<MethodNameGroup> allGroups) {
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
					memberInfos.Property(method.Property).Rename(prop.PropertyDef.Name.String);
				}
			}
		}

		void ResetVirtualEventNames(IEnumerable<MethodNameGroup> allGroups) {
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
					memberInfos.Event(method.Event).Rename(evt.EventDef.Name.String);
				}
			}
		}

		void RestoreVirtualProperties(IEnumerable<MethodNameGroup> allGroups) {
			if (!RestoreProperties)
				return;
			foreach (var group in allGroups) {
				RestoreVirtualProperties(group);
				RestoreExplicitVirtualProperties(group);
			}
		}

		void RestoreExplicitVirtualProperties(MethodNameGroup group) {
			if (group.Methods.Count != 1)
				return;
			var propMethod = group.Methods[0];
			if (propMethod.Property != null)
				return;
			if (propMethod.MethodDef.Overrides.Count == 0)
				return;

			var theProperty = GetOverriddenProperty(propMethod);
			if (theProperty == null)
				return;

			CreateProperty(theProperty, propMethod, GetOverridePrefix(group, propMethod));
		}

		void RestoreVirtualProperties(MethodNameGroup group) {
			if (group.Methods.Count <= 1 || !group.HasProperty())
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
				CreateProperty(prop, method, "");
		}

		void CreateProperty(MPropertyDef propDef, MMethodDef methodDef, string overridePrefix) {
			if (!methodDef.Owner.HasModule)
				return;

			var newPropertyName = overridePrefix + propDef.PropertyDef.Name;
			if (!DotNetUtils.HasReturnValue(methodDef.MethodDef))
				CreatePropertySetter(newPropertyName, methodDef);
			else
				CreatePropertyGetter(newPropertyName, methodDef);
		}

		void RestorePropertiesFromNames2(IEnumerable<MethodNameGroup> allGroups) {
			if (!RestorePropertiesFromNames)
				return;

			foreach (var group in allGroups) {
				var groupMethod = group.Methods[0];
				var methodName = groupMethod.MethodDef.Name.String;
				bool onlyRenamableMethods = !group.HasNonRenamableMethod();

				if (Utils.StartsWith(methodName, "get_", StringComparison.Ordinal)) {
					var propName = methodName.Substring(4);
					foreach (var method in group.Methods) {
						if (onlyRenamableMethods && !memberInfos.Type(method.Owner).NameChecker.IsValidPropertyName(propName))
							continue;
						CreatePropertyGetter(propName, method);
					}
				}
				else if (Utils.StartsWith(methodName, "set_", StringComparison.Ordinal)) {
					var propName = methodName.Substring(4);
					foreach (var method in group.Methods) {
						if (onlyRenamableMethods && !memberInfos.Type(method.Owner).NameChecker.IsValidPropertyName(propName))
							continue;
						CreatePropertySetter(propName, method);
					}
				}
			}

			foreach (var type in modules.AllTypes) {
				foreach (var method in type.AllMethodsSorted) {
					if (method.IsVirtual())
						continue;	// Virtual methods are in allGroups, so already fixed above
					if (method.Property != null)
						continue;
					var methodName = method.MethodDef.Name.String;
					if (Utils.StartsWith(methodName, "get_", StringComparison.Ordinal))
						CreatePropertyGetter(methodName.Substring(4), method);
					else if (Utils.StartsWith(methodName, "set_", StringComparison.Ordinal))
						CreatePropertySetter(methodName.Substring(4), method);
				}
			}
		}

		MPropertyDef CreatePropertyGetter(string name, MMethodDef propMethod) {
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
			var propDef = CreateProperty(ownerType, name, propType, propMethod.MethodDef, null);
			if (propDef == null)
				return null;
			if (propDef.GetMethod != null)
				return null;
			if (isVerbose)
				Logger.v("Restoring property getter {0} ({1:X8}), Property: {2} ({3:X8})",
						Utils.RemoveNewlines(propMethod),
						propMethod.MethodDef.MDToken.ToInt32(),
						Utils.RemoveNewlines(propDef.PropertyDef),
						propDef.PropertyDef.MDToken.ToInt32());
			propDef.PropertyDef.GetMethod = propMethod.MethodDef;
			propDef.GetMethod = propMethod;
			propMethod.Property = propDef;
			return propDef;
		}

		MPropertyDef CreatePropertySetter(string name, MMethodDef propMethod) {
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
			var propDef = CreateProperty(ownerType, name, propType, null, propMethod.MethodDef);
			if (propDef == null)
				return null;
			if (propDef.SetMethod != null)
				return null;
			if (isVerbose)
				Logger.v("Restoring property setter {0} ({1:X8}), Property: {2} ({3:X8})",
						Utils.RemoveNewlines(propMethod),
						propMethod.MethodDef.MDToken.ToInt32(),
						Utils.RemoveNewlines(propDef.PropertyDef),
						propDef.PropertyDef.MDToken.ToInt32());
			propDef.PropertyDef.SetMethod = propMethod.MethodDef;
			propDef.SetMethod = propMethod;
			propMethod.Property = propDef;
			return propDef;
		}

		MPropertyDef CreateProperty(MTypeDef ownerType, string name, TypeSig propType, MethodDef getter, MethodDef setter) {
			if (string.IsNullOrEmpty(name) || propType.ElementType == ElementType.Void)
				return null;
			var newSig = CreatePropertySig(getter, propType, true) ?? CreatePropertySig(setter, propType, false);
			if (newSig == null)
				return null;
			var newProp = ownerType.Module.ModuleDefMD.UpdateRowId(new PropertyDefUser(name, newSig, 0));
			newProp.GetMethod = getter;
			newProp.SetMethod = setter;
			var propDef = ownerType.FindAny(newProp);
			if (propDef != null)
				return propDef;

			propDef = ownerType.Create(newProp);
			memberInfos.Add(propDef);
			if (isVerbose)
				Logger.v("Restoring property: {0}", Utils.RemoveNewlines(newProp));
			return propDef;
		}

		static PropertySig CreatePropertySig(MethodDef method, TypeSig propType, bool isGetter) {
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

		void RestoreVirtualEvents(IEnumerable<MethodNameGroup> allGroups) {
			if (!RestoreEvents)
				return;
			foreach (var group in allGroups) {
				RestoreVirtualEvents(group);
				RestoreExplicitVirtualEvents(group);
			}
		}

		enum EventMethodType {
			None,
			Other,
			Adder,
			Remover,
			Raiser,
		}

		void RestoreExplicitVirtualEvents(MethodNameGroup group) {
			if (group.Methods.Count != 1)
				return;
			var eventMethod = group.Methods[0];
			if (eventMethod.Event != null)
				return;
			if (eventMethod.MethodDef.Overrides.Count == 0)
				return;

			MMethodDef overriddenMethod;
			var theEvent = GetOverriddenEvent(eventMethod, out overriddenMethod);
			if (theEvent == null)
				return;

			CreateEvent(theEvent, eventMethod, GetEventMethodType(overriddenMethod), GetOverridePrefix(group, eventMethod));
		}

		void RestoreVirtualEvents(MethodNameGroup group) {
			if (group.Methods.Count <= 1 || !group.HasEvent())
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
					methodType = GetEventMethodType(method);
				}
			}
			if (evt == null)
				return;	// Should never happen
			if (missingEvents == null)
				return;

			foreach (var method in missingEvents)
				CreateEvent(evt, method, methodType, "");
		}

		void CreateEvent(MEventDef eventDef, MMethodDef methodDef, EventMethodType methodType, string overridePrefix) {
			if (!methodDef.Owner.HasModule)
				return;

			var newEventName = overridePrefix + eventDef.EventDef.Name;
			switch (methodType) {
			case EventMethodType.Adder:
				CreateEventAdder(newEventName, methodDef);
				break;
			case EventMethodType.Remover:
				CreateEventRemover(newEventName, methodDef);
				break;
			}
		}

		static EventMethodType GetEventMethodType(MMethodDef method) {
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

		void RestoreEventsFromNames2(IEnumerable<MethodNameGroup> allGroups) {
			if (!RestoreEventsFromNames)
				return;

			foreach (var group in allGroups) {
				var groupMethod = group.Methods[0];
				var methodName = groupMethod.MethodDef.Name.String;
				bool onlyRenamableMethods = !group.HasNonRenamableMethod();

				if (Utils.StartsWith(methodName, "add_", StringComparison.Ordinal)) {
					var eventName = methodName.Substring(4);
					foreach (var method in group.Methods) {
						if (onlyRenamableMethods && !memberInfos.Type(method.Owner).NameChecker.IsValidEventName(eventName))
							continue;
						CreateEventAdder(eventName, method);
					}
				}
				else if (Utils.StartsWith(methodName, "remove_", StringComparison.Ordinal)) {
					var eventName = methodName.Substring(7);
					foreach (var method in group.Methods) {
						if (onlyRenamableMethods && !memberInfos.Type(method.Owner).NameChecker.IsValidEventName(eventName))
							continue;
						CreateEventRemover(eventName, method);
					}
				}
			}

			foreach (var type in modules.AllTypes) {
				foreach (var method in type.AllMethodsSorted) {
					if (method.IsVirtual())
						continue;	// Virtual methods are in allGroups, so already fixed above
					if (method.Event != null)
						continue;
					var methodName = method.MethodDef.Name.String;
					if (Utils.StartsWith(methodName, "add_", StringComparison.Ordinal))
						CreateEventAdder(methodName.Substring(4), method);
					else if (Utils.StartsWith(methodName, "remove_", StringComparison.Ordinal))
						CreateEventRemover(methodName.Substring(7), method);
				}
			}
		}

		MEventDef CreateEventAdder(string name, MMethodDef eventMethod) {
			if (string.IsNullOrEmpty(name))
				return null;
			var ownerType = eventMethod.Owner;
			if (!ownerType.HasModule)
				return null;
			if (eventMethod.Event != null)
				return null;

			var method = eventMethod.MethodDef;
			var eventDef = CreateEvent(ownerType, name, GetEventType(method));
			if (eventDef == null)
				return null;
			if (eventDef.AddMethod != null)
				return null;
			if (isVerbose)
				Logger.v("Restoring event adder {0} ({1:X8}), Event: {2} ({3:X8})",
						Utils.RemoveNewlines(eventMethod),
						eventMethod.MethodDef.MDToken.ToInt32(),
						Utils.RemoveNewlines(eventDef.EventDef),
						eventDef.EventDef.MDToken.ToInt32());
			eventDef.EventDef.AddMethod = eventMethod.MethodDef;
			eventDef.AddMethod = eventMethod;
			eventMethod.Event = eventDef;
			return eventDef;
		}

		MEventDef CreateEventRemover(string name, MMethodDef eventMethod) {
			if (string.IsNullOrEmpty(name))
				return null;
			var ownerType = eventMethod.Owner;
			if (!ownerType.HasModule)
				return null;
			if (eventMethod.Event != null)
				return null;

			var method = eventMethod.MethodDef;
			var eventDef = CreateEvent(ownerType, name, GetEventType(method));
			if (eventDef == null)
				return null;
			if (eventDef.RemoveMethod != null)
				return null;
			if (isVerbose)
				Logger.v("Restoring event remover {0} ({1:X8}), Event: {2} ({3:X8})",
						Utils.RemoveNewlines(eventMethod),
						eventMethod.MethodDef.MDToken.ToInt32(),
						Utils.RemoveNewlines(eventDef.EventDef),
						eventDef.EventDef.MDToken.ToInt32());
			eventDef.EventDef.RemoveMethod = eventMethod.MethodDef;
			eventDef.RemoveMethod = eventMethod;
			eventMethod.Event = eventDef;
			return eventDef;
		}

		TypeSig GetEventType(IMethod method) {
			if (DotNetUtils.HasReturnValue(method))
				return null;
			var sig = method.MethodSig;
			if (sig == null || sig.Params.Count != 1)
				return null;
			return sig.Params[0];
		}

		MEventDef CreateEvent(MTypeDef ownerType, string name, TypeSig eventType) {
			if (string.IsNullOrEmpty(name) || eventType == null || eventType.ElementType == ElementType.Void)
				return null;
			var newEvent = ownerType.Module.ModuleDefMD.UpdateRowId(new EventDefUser(name, eventType.ToTypeDefOrRef(), 0));
			var eventDef = ownerType.FindAny(newEvent);
			if (eventDef != null)
				return eventDef;

			eventDef = ownerType.Create(newEvent);
			memberInfos.Add(eventDef);
			if (isVerbose)
				Logger.v("Restoring event: {0}", Utils.RemoveNewlines(newEvent));
			return eventDef;
		}

		void PrepareRenameMemberDefs(MethodNameGroups groups) {
			if (isVerbose)
				Logger.v("Renaming member definitions #1");

			PrepareRenameEntryPoints();

			var virtualMethods = new GroupHelper(memberInfos, modules.AllTypes);
			var ifaceMethods = new GroupHelper(memberInfos, modules.AllTypes);
			var propMethods = new GroupHelper(memberInfos, modules.AllTypes);
			var eventMethods = new GroupHelper(memberInfos, modules.AllTypes);
			foreach (var group in GetSorted(groups)) {
				if (group.HasNonRenamableMethod())
					continue;
				else if (group.HasGetterOrSetterPropertyMethod() && GetPropertyMethodType(group.Methods[0]) != PropertyMethodType.Other)
					propMethods.Add(group);
				else if (group.HasAddRemoveOrRaiseEventMethod())
					eventMethods.Add(group);
				else if (group.HasInterfaceMethod())
					ifaceMethods.Add(group);
				else
					virtualMethods.Add(group);
			}

			var prepareHelper = new PrepareHelper(memberInfos, modules.AllTypes);
			prepareHelper.Prepare((info) => info.PrepareRenameMembers());

			prepareHelper.Prepare((info) => info.PrepareRenamePropsAndEvents());
			propMethods.VisitAll((group) => PrepareRenameProperty(group, false));
			eventMethods.VisitAll((group) => PrepareRenameEvent(group, false));
			propMethods.VisitAll((group) => PrepareRenameProperty(group, true));
			eventMethods.VisitAll((group) => PrepareRenameEvent(group, true));

			foreach (var typeDef in modules.AllTypes)
				memberInfos.Type(typeDef).InitializeEventHandlerNames();

			prepareHelper.Prepare((info) => info.PrepareRenameMethods());
			ifaceMethods.VisitAll((group) => PrepareRenameVirtualMethods(group, "imethod_", false));
			virtualMethods.VisitAll((group) => PrepareRenameVirtualMethods(group, "vmethod_", false));
			ifaceMethods.VisitAll((group) => PrepareRenameVirtualMethods(group, "imethod_", true));
			virtualMethods.VisitAll((group) => PrepareRenameVirtualMethods(group, "vmethod_", true));

			RestoreMethodArgs(groups);

			foreach (var typeDef in modules.AllTypes)
				memberInfos.Type(typeDef).PrepareRenameMethods2();
		}

		void RestoreMethodArgs(MethodNameGroups groups) {
			foreach (var group in groups.GetAllGroups()) {
				if (group.Methods[0].VisibleParameterCount == 0)
					continue;

				var argNames = GetValidArgNames(group);

				foreach (var method in group.Methods) {
					if (!method.Owner.HasModule)
						continue;
					var nameChecker = method.Owner.Module.ObfuscatedFile.NameChecker;

					for (int i = 0; i < argNames.Length; i++) {
						var argName = argNames[i];
						if (argName == null || !nameChecker.IsValidMethodArgName(argName))
							continue;
						var info = memberInfos.Param(method.ParamDefs[i]);
						if (nameChecker.IsValidMethodArgName(info.oldName))
							continue;
						info.newName = argName;
					}
				}
			}
		}

		string[] GetValidArgNames(MethodNameGroup group) {
			var methods = new List<MMethodDef>(group.Methods);
			foreach (var method in group.Methods) {
				foreach (var ovrd in method.MethodDef.Overrides) {
					var overrideRef = ovrd.MethodDeclaration;
					var overrideDef = modules.ResolveMethod(overrideRef);
					if (overrideDef == null) {
						var typeDef = modules.ResolveType(overrideRef.DeclaringType) ?? modules.ResolveOther(overrideRef.DeclaringType);
						if (typeDef == null)
							continue;
						overrideDef = typeDef.FindMethod(overrideRef);
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
					if (nameChecker == null || nameChecker.IsValidMethodArgName(argName))
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

			public void Prepare(Action<TypeInfo> func) {
				this.func = func;
				prepareMethodCalled.Clear();
				foreach (var typeDef in allTypes)
					Prepare(typeDef);
			}

			void Prepare(MTypeDef type) {
				if (prepareMethodCalled.ContainsKey(type))
					return;
				prepareMethodCalled[type] = true;

				foreach (var ifaceInfo in type.interfaces)
					Prepare(ifaceInfo.typeDef);
				if (type.baseType != null)
					Prepare(type.baseType.typeDef);

				TypeInfo info;
				if (memberInfos.TryGetType(type, out info))
					func(info);
			}
		}

		static List<MethodNameGroup> GetSorted(MethodNameGroups groups) {
			var allGroups = new List<MethodNameGroup>(groups.GetAllGroups());
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

			public void Add(MethodNameGroup group) {
				groups.Add(group);
			}

			public void VisitAll(Action<MethodNameGroup> func) {
				this.func = func;
				visited.Clear();

				methodToGroup = new Dictionary<MMethodDef, MethodNameGroup>();
				foreach (var group in groups) {
					foreach (var method in group.Methods)
						methodToGroup[method] = group;
				}

				foreach (var type in allTypes)
					Visit(type);
			}

			void Visit(MTypeDef type) {
				if (visited.ContainsKey(type))
					return;
				visited[type] = true;

				if (type.baseType != null)
					Visit(type.baseType.typeDef);
				foreach (var ifaceInfo in type.interfaces)
					Visit(ifaceInfo.typeDef);

				TypeInfo info;
				if (!memberInfos.TryGetType(type, out info))
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
		static string GetOverridePrefix(MethodNameGroup group, MMethodDef method) {
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
			if (overrideMethod.DeclaringType == null)
				return "";
			var name = overrideMethod.DeclaringType.FullName.Replace('/', '.');
			name = removeGenericsArityRegex.Replace(name, "");
			return name + ".";
		}

		static string GetRealName(string name) {
			int index = name.LastIndexOf('.');
			if (index < 0)
				return name;
			return name.Substring(index + 1);
		}

		void PrepareRenameEvent(MethodNameGroup group, bool renameOverrides) {
			string methodPrefix, overridePrefix;
			var eventName = PrepareRenameEvent(group, renameOverrides, out overridePrefix, out methodPrefix);
			if (eventName == null)
				return;

			var methodName = overridePrefix + methodPrefix + eventName;
			foreach (var method in group.Methods)
				memberInfos.Method(method).Rename(methodName);
		}

		string PrepareRenameEvent(MethodNameGroup group, bool renameOverrides, out string overridePrefix, out string methodPrefix) {
			var eventMethod = GetEventMethod(group);
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

			overridePrefix = GetOverridePrefix(group, eventMethod);
			if (renameOverrides && overridePrefix == "")
				return null;
			if (!renameOverrides && overridePrefix != "")
				return null;

			string newEventName, oldEventName;
			var eventInfo = memberInfos.Event(eventDef);

			bool mustUseOldEventName = false;
			if (overridePrefix == "")
				oldEventName = eventInfo.oldName;
			else {
				var overriddenEventDef = GetOverriddenEvent(eventMethod);
				if (overriddenEventDef == null)
					oldEventName = GetRealName(eventInfo.oldName);
				else {
					mustUseOldEventName = true;
					EventInfo info;
					if (memberInfos.TryGetEvent(overriddenEventDef, out info))
						oldEventName = GetRealName(info.newName);
					else
						oldEventName = GetRealName(overriddenEventDef.EventDef.Name.String);
				}
			}

			if (eventInfo.renamed)
				newEventName = GetRealName(eventInfo.newName);
			else if (mustUseOldEventName || eventDef.Owner.Module.ObfuscatedFile.NameChecker.IsValidEventName(oldEventName))
				newEventName = oldEventName;
			else {
				mergeStateHelper.Merge(MergeStateFlags.Events, group);
				newEventName = GetAvailableName("Event_", false, group, (group2, newName) => IsEventAvailable(group2, newName));
			}

			var newEventNameWithPrefix = overridePrefix + newEventName;
			foreach (var method in group.Methods) {
				if (method.Event != null) {
					memberInfos.Event(method.Event).Rename(newEventNameWithPrefix);
					var ownerInfo = memberInfos.Type(method.Owner);
					ownerInfo.variableNameState.AddEventName(newEventName);
					ownerInfo.variableNameState.AddEventName(newEventNameWithPrefix);
				}
			}

			return newEventName;
		}

		MEventDef GetOverriddenEvent(MMethodDef overrideMethod) {
			MMethodDef overriddenMethod;
			return GetOverriddenEvent(overrideMethod, out overriddenMethod);
		}

		MEventDef GetOverriddenEvent(MMethodDef overrideMethod, out MMethodDef overriddenMethod) {
			var theMethod = overrideMethod.MethodDef.Overrides[0].MethodDeclaration;
			overriddenMethod = modules.ResolveMethod(theMethod);
			if (overriddenMethod != null)
				return overriddenMethod.Event;

			var extType = theMethod.DeclaringType;
			if (extType == null)
				return null;
			var extTypeDef = modules.ResolveOther(extType);
			if (extTypeDef == null)
				return null;
			overriddenMethod = extTypeDef.FindMethod(theMethod);
			if (overriddenMethod != null)
				return overriddenMethod.Event;

			return null;
		}

		MMethodDef GetEventMethod(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (method.Event != null)
					return method;
			}
			return null;
		}

		void PrepareRenameProperty(MethodNameGroup group, bool renameOverrides) {
			string overridePrefix;
			var propName = PrepareRenameProperty(group, renameOverrides, out overridePrefix);
			if (propName == null)
				return;

			string methodPrefix;
			switch (GetPropertyMethodType(group.Methods[0])) {
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
				memberInfos.Method(method).Rename(methodName);
		}

		string PrepareRenameProperty(MethodNameGroup group, bool renameOverrides, out string overridePrefix) {
			var propMethod = GetPropertyMethod(group);
			if (propMethod == null)
				throw new ApplicationException("No properties found");

			overridePrefix = GetOverridePrefix(group, propMethod);

			if (renameOverrides && overridePrefix == "")
				return null;
			if (!renameOverrides && overridePrefix != "")
				return null;

			string newPropName, oldPropName;
			var propDef = propMethod.Property;
			var propInfo = memberInfos.Property(propDef);

			bool mustUseOldPropName = false;
			if (overridePrefix == "")
				oldPropName = propInfo.oldName;
			else {
				var overriddenPropDef = GetOverriddenProperty(propMethod);
				if (overriddenPropDef == null)
					oldPropName = GetRealName(propInfo.oldName);
				else {
					mustUseOldPropName = true;
					PropertyInfo info;
					if (memberInfos.TryGetProperty(overriddenPropDef, out info))
						oldPropName = GetRealName(info.newName);
					else
						oldPropName = GetRealName(overriddenPropDef.PropertyDef.Name.String);
				}
			}

			if (propInfo.renamed)
				newPropName = GetRealName(propInfo.newName);
			else if (mustUseOldPropName || propDef.Owner.Module.ObfuscatedFile.NameChecker.IsValidPropertyName(oldPropName))
				newPropName = oldPropName;
			else if (IsItemProperty(group))
				newPropName = "Item";
			else {
				bool trySameName = true;
				var propPrefix = GetSuggestedPropertyName(group);
				if (propPrefix == null) {
					trySameName = false;
					propPrefix = GetNewPropertyNamePrefix(group);
				}
				mergeStateHelper.Merge(MergeStateFlags.Properties, group);
				newPropName = GetAvailableName(propPrefix, trySameName, group, (group2, newName) => IsPropertyAvailable(group2, newName));
			}

			var newPropNameWithPrefix = overridePrefix + newPropName;
			foreach (var method in group.Methods) {
				if (method.Property != null) {
					memberInfos.Property(method.Property).Rename(newPropNameWithPrefix);
					var ownerInfo = memberInfos.Type(method.Owner);
					ownerInfo.variableNameState.AddPropertyName(newPropName);
					ownerInfo.variableNameState.AddPropertyName(newPropNameWithPrefix);
				}
			}

			return newPropName;
		}

		bool IsItemProperty(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (method.Property != null && method.Property.IsItemProperty())
					return true;
			}
			return false;
		}

		MPropertyDef GetOverriddenProperty(MMethodDef overrideMethod) {
			var theMethod = overrideMethod.MethodDef.Overrides[0].MethodDeclaration;
			var overriddenMethod = modules.ResolveMethod(theMethod);
			if (overriddenMethod != null)
				return overriddenMethod.Property;

			var extType = theMethod.DeclaringType;
			if (extType == null)
				return null;
			var extTypeDef = modules.ResolveOther(extType);
			if (extTypeDef == null)
				return null;
			var theMethodDef = extTypeDef.FindMethod(theMethod);
			if (theMethodDef != null)
				return theMethodDef.Property;

			return null;
		}

		MMethodDef GetPropertyMethod(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (method.Property != null)
					return method;
			}
			return null;
		}

		string GetSuggestedPropertyName(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (method.Property == null)
					continue;
				var info = memberInfos.Property(method.Property);
				if (info.suggestedName != null)
					return info.suggestedName;
			}
			return null;
		}

		internal static ITypeDefOrRef GetScopeType(TypeSig typeSig) {
			if (typeSig == null)
				return null;
			var scopeType = typeSig.ScopeType;
			if (scopeType != null)
				return scopeType;

			for (int i = 0; i < 100; i++) {
				var nls = typeSig as NonLeafSig;
				if (nls == null)
					break;
				typeSig = nls.Next;
			}

			switch (typeSig.GetElementType()) {
			case ElementType.MVar:
			case ElementType.Var:
				return new TypeSpecUser(typeSig);
			default:
				return null;
			}
		}

		string GetNewPropertyNamePrefix(MethodNameGroup group) {
			const string defaultVal = "Prop_";

			var propType = GetPropertyType(group);
			if (propType == null)
				return defaultVal;

			var elementType = GetScopeType(propType).ToTypeSig(false).RemovePinnedAndModifiers();
			if (propType is GenericInstSig || elementType is GenericSig)
				return defaultVal;

			var prefix = GetPrefix(propType);

			string name = elementType.TypeName;
			int i;
			if ((i = name.IndexOf('`')) >= 0)
				name = name.Substring(0, i);
			if ((i = name.LastIndexOf('.')) >= 0)
				name = name.Substring(i + 1);
			if (name == "")
				return defaultVal;

			return prefix.ToUpperInvariant() + UpperFirst(name) + "_";
		}

		static string UpperFirst(string s) {
			return s.Substring(0, 1).ToUpperInvariant() + s.Substring(1);
		}

		static string GetPrefix(TypeSig typeRef) {
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

		static PropertyMethodType GetPropertyMethodType(MMethodDef method) {
			if (DotNetUtils.HasReturnValue(method.MethodDef))
				return PropertyMethodType.Getter;
			if (method.VisibleParameterCount > 0)
				return PropertyMethodType.Setter;
			return PropertyMethodType.Other;
		}

		// Returns property type, or null if not all methods have the same type
		TypeSig GetPropertyType(MethodNameGroup group) {
			var methodType = GetPropertyMethodType(group.Methods[0]);
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

		MMethodDef GetOverrideMethod(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (method.MethodDef.Overrides.Count > 0)
					return method;
			}
			return null;
		}

		void PrepareRenameVirtualMethods(MethodNameGroup group, string namePrefix, bool renameOverrides) {
			if (!HasInvalidMethodName(group))
				return;

			if (HasDelegateOwner(group)) {
				switch (group.Methods[0].MethodDef.Name.String) {
				case "Invoke":
				case "BeginInvoke":
				case "EndInvoke":
					return;
				}
			}

			var overrideMethod = GetOverrideMethod(group);
			var overridePrefix = GetOverridePrefix(group, overrideMethod);
			if (renameOverrides && overridePrefix == "")
				return;
			if (!renameOverrides && overridePrefix != "")
				return;

			string newMethodName;
			if (overridePrefix != "") {
				/*var overrideInfo =*/ memberInfos.Method(overrideMethod);
				var overriddenMethod = GetOverriddenMethod(overrideMethod);
				if (overriddenMethod == null)
					newMethodName = GetRealName(overrideMethod.MethodDef.Overrides[0].MethodDeclaration.Name.String);
				else
					newMethodName = GetRealName(memberInfos.Method(overriddenMethod).newName);
			}
			else {
				newMethodName = GetSuggestedMethodName(group);
				if (newMethodName == null) {
					mergeStateHelper.Merge(MergeStateFlags.Methods, group);
					newMethodName = GetAvailableName(namePrefix, false, group, (group2, newName) => IsMethodAvailable(group2, newName));
				}
			}

			var newMethodNameWithPrefix = overridePrefix + newMethodName;
			foreach (var method in group.Methods)
				memberInfos.Type(method.Owner).RenameMethod(method, newMethodNameWithPrefix);
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

			public void Merge(MergeStateFlags flags, MethodNameGroup group) {
				this.flags = flags;
				visited.Clear();
				foreach (var method in group.Methods)
					Merge(method.Owner);
			}

			void Merge(MTypeDef type) {
				if (visited.ContainsKey(type))
					return;
				visited[type] = true;

				TypeInfo info;
				if (!memberInfos.TryGetType(type, out info))
					return;

				if (type.baseType != null)
					Merge(type.baseType.typeDef);
				foreach (var ifaceInfo in type.interfaces)
					Merge(ifaceInfo.typeDef);

				if (type.baseType != null)
					Merge(info, type.baseType.typeDef);
				foreach (var ifaceInfo in type.interfaces)
					Merge(info, ifaceInfo.typeDef);
			}

			void Merge(TypeInfo info, MTypeDef other) {
				TypeInfo otherInfo;
				if (!memberInfos.TryGetType(other, out otherInfo))
					return;

				if ((flags & MergeStateFlags.Methods) != MergeStateFlags.None)
					info.variableNameState.MergeMethods(otherInfo.variableNameState);
				if ((flags & MergeStateFlags.Properties) != MergeStateFlags.None)
					info.variableNameState.MergeProperties(otherInfo.variableNameState);
				if ((flags & MergeStateFlags.Events) != MergeStateFlags.None)
					info.variableNameState.MergeEvents(otherInfo.variableNameState);
			}
		}

		MMethodDef GetOverriddenMethod(MMethodDef overrideMethod) {
			return modules.ResolveMethod(overrideMethod.MethodDef.Overrides[0].MethodDeclaration);
		}

		string GetSuggestedMethodName(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				var info = memberInfos.Method(method);
				if (info.suggestedName != null)
					return info.suggestedName;
			}
			return null;
		}

		bool HasInvalidMethodName(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				var typeInfo = memberInfos.Type(method.Owner);
				var methodInfo = memberInfos.Method(method);
				if (!typeInfo.NameChecker.IsValidMethodName(methodInfo.oldName))
					return true;
			}
			return false;
		}

		static string GetAvailableName(string prefix, bool tryWithoutZero, MethodNameGroup group, Func<MethodNameGroup, string, bool> checkAvailable) {
			for (int i = 0; ; i++) {
				string newName = i == 0 && tryWithoutZero ? prefix : prefix + i;
				if (checkAvailable(group, newName))
					return newName;
			}
		}

		bool IsMethodAvailable(MethodNameGroup group, string methodName) {
			foreach (var method in group.Methods) {
				if (memberInfos.Type(method.Owner).variableNameState.IsMethodNameUsed(methodName))
					return false;
			}
			return true;
		}

		bool IsPropertyAvailable(MethodNameGroup group, string methodName) {
			foreach (var method in group.Methods) {
				if (memberInfos.Type(method.Owner).variableNameState.IsPropertyNameUsed(methodName))
					return false;
			}
			return true;
		}

		bool IsEventAvailable(MethodNameGroup group, string methodName) {
			foreach (var method in group.Methods) {
				if (memberInfos.Type(method.Owner).variableNameState.IsEventNameUsed(methodName))
					return false;
			}
			return true;
		}

		bool HasDelegateOwner(MethodNameGroup group) {
			foreach (var method in group.Methods) {
				if (isDelegateClass.Check(method.Owner))
					return true;
			}
			return false;
		}

		void PrepareRenameEntryPoints() {
			foreach (var module in modules.TheModules) {
				var entryPoint = module.ModuleDefMD.EntryPoint;
				if (entryPoint == null)
					continue;
				var methodDef = modules.ResolveMethod(entryPoint);
				if (methodDef == null) {
					Logger.w(string.Format("Could not find entry point. Module: {0}, Method: {1}", module.ModuleDefMD.Location, Utils.RemoveNewlines(entryPoint)));
					continue;
				}
				if (!methodDef.IsStatic())
					continue;
				memberInfos.Method(methodDef).suggestedName = "Main";
				if (methodDef.ParamDefs.Count == 1) {
					var paramDef = methodDef.ParamDefs[0];
					var type = paramDef.ParameterDef.Type;
					if (type.FullName == "System.String[]")
						memberInfos.Param(paramDef).newName = "args";
				}
			}
		}
	}
}
