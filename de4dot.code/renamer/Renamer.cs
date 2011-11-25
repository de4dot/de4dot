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
using Mono.Cecil.Cil;
using de4dot.renamer.asmmodules;
using de4dot.blocks;

namespace de4dot.renamer {
	class Renamer {
		public bool RenameNamespaces { get; set; }
		public bool RenameTypes { get; set; }
		public bool RenameProperties { get; set; }
		public bool RenameEvents { get; set; }
		public bool RenameFields { get; set; }
		public bool RenameMethods { get; set; }
		public bool RenameMethodArgs { get; set; }
		public bool RenameGenericParams { get; set; }
		public bool RestoreProperties { get; set; }
		public bool RestorePropertiesFromNames { get; set; }
		public bool RestoreEvents { get; set; }
		public bool RestoreEventsFromNames { get; set; }

		Modules modules = new Modules();
		MemberInfos memberInfos = new MemberInfos();
		DerivedFrom isDelegateClass;
		MergeStateHelper mergeStateHelper;

		static string[] delegateClasses = new string[] {
			"System.Delegate",
			"System.MulticastDelegate",
		};

		public Renamer(IEnumerable<IObfuscatedFile> files) {
			RenameNamespaces = true;
			RenameTypes = true;
			RenameProperties = true;
			RenameEvents = true;
			RenameFields = true;
			RenameMethods = true;
			RenameMethodArgs = true;
			RenameGenericParams = true;
			RestoreProperties = true;
			RestorePropertiesFromNames = true;
			RestoreEvents = true;
			RestoreEventsFromNames = true;

			isDelegateClass = new DerivedFrom(delegateClasses);
			mergeStateHelper = new MergeStateHelper(memberInfos);

			foreach (var file in files)
				modules.add(new Module(file));
		}

		public void rename() {
			if (modules.Empty)
				return;
			Log.n("Renaming all obfuscated symbols");

			modules.initialize();
			var scopes = modules.initializeVirtualMembers();
			memberInfos.initialize(modules);
			renameTypeDefinitions();
			renameTypeReferences();
			modules.onTypesRenamed();
			restorePropertiesAndEvents(scopes);
			prepareRenameMemberDefinitions(scopes);
			renameMemberDefinitions();
			renameMemberReferences();
			renameResources();
			modules.cleanUp();
		}

		void renameTypeDefinitions() {
			Log.v("Renaming obfuscated type definitions");

			foreach (var module in modules.TheModules) {
				if (module.ObfuscatedFile.RemoveNamespaceWithOneType)
					removeOneClassNamespaces(module);
			}

			var state = new TypeRenamerState();
			foreach (var type in modules.AllTypes)
				state.addTypeName(memberInfos.type(type).oldName);
			prepareRenameTypes(modules.BaseTypes, state);
			fixClsTypeNames();
			renameTypeDefinitions(modules.NonNestedTypes);
		}

		void removeOneClassNamespaces(Module module) {
			var nsToTypes = new Dictionary<string, List<TypeDef>>(StringComparer.Ordinal);

			foreach (var typeDef in module.getAllTypes()) {
				List<TypeDef> list;
				var ns = typeDef.TypeDefinition.Namespace;
				if (string.IsNullOrEmpty(ns))
					continue;
				if (module.ObfuscatedFile.NameChecker.isValidNamespaceName(ns))
					continue;
				if (!nsToTypes.TryGetValue(ns, out list))
					nsToTypes[ns] = list = new List<TypeDef>();
				list.Add(typeDef);
			}

			var sortedNamespaces = new List<List<TypeDef>>(nsToTypes.Values);
			sortedNamespaces.Sort((a, b) => {
				return string.CompareOrdinal(a[0].TypeDefinition.Namespace, b[0].TypeDefinition.Namespace);
			});
			foreach (var list in sortedNamespaces) {
				const int maxClasses = 1;
				if (list.Count != maxClasses)
					continue;
				var ns = list[0].TypeDefinition.Namespace;
				Log.v("Removing namespace: {0}", ns);
				foreach (var type in list)
					memberInfos.type(type).newNamespace = "";
			}
		}

		void renameTypeDefinitions(IEnumerable<TypeDef> typeDefs) {
			Log.indent();
			foreach (var typeDef in typeDefs) {
				rename(typeDef);
				renameTypeDefinitions(typeDef.NestedTypes);
			}
			Log.deIndent();
		}

		void rename(TypeDef type) {
			var typeDefinition = type.TypeDefinition;
			var info = memberInfos.type(type);

			Log.v("Type: {0} ({1:X8})", typeDefinition.FullName, typeDefinition.MetadataToken.ToUInt32());
			Log.indent();

			renameGenericParams(type.GenericParams);

			if (RenameTypes && info.gotNewName()) {
				var old = typeDefinition.Name;
				typeDefinition.Name = info.newName;
				Log.v("Name: {0} => {1}", old, typeDefinition.Name);
			}

			if (RenameNamespaces && info.newNamespace != null) {
				var old = typeDefinition.Namespace;
				typeDefinition.Namespace = info.newNamespace;
				Log.v("Namespace: {0} => {1}", old, typeDefinition.Namespace);
			}

			Log.deIndent();
		}

		void renameGenericParams(IEnumerable<GenericParamDef> genericParams) {
			if (!RenameGenericParams)
				return;
			foreach (var param in genericParams) {
				var info = memberInfos.gparam(param);
				if (!info.gotNewName())
					continue;
				param.GenericParameter.Name = info.newName;
				Log.v("GenParam: {0} => {1}", info.oldFullName, param.GenericParameter.FullName);
			}
		}

		void renameMemberDefinitions() {
			Log.v("Renaming member definitions #2");

			var allTypes = new List<TypeDef>(modules.AllTypes);
			allTypes.Sort((a, b) => Utils.compareInt32(a.Index, b.Index));

			Log.indent();
			foreach (var typeDef in allTypes)
				renameMembers(typeDef);
			Log.deIndent();
		}

		void renameMembers(TypeDef type) {
			var info = memberInfos.type(type);

			Log.v("Type: {0}", info.type.TypeDefinition.FullName);
			Log.indent();

			renameFields(info);
			renameProperties(info);
			renameEvents(info);
			renameMethods(info);

			Log.deIndent();
		}

		void renameFields(TypeInfo info) {
			if (!RenameFields)
				return;
			foreach (var fieldDef in info.type.AllFieldsSorted) {
				var fieldInfo = memberInfos.field(fieldDef);
				if (!fieldInfo.gotNewName())
					continue;
				fieldDef.FieldDefinition.Name = fieldInfo.newName;
				Log.v("Field: {0} ({1:X8}) => {2}", fieldInfo.oldFullName, fieldDef.FieldDefinition.MetadataToken.ToUInt32(), fieldDef.FieldDefinition.FullName);
			}
		}

		void renameProperties(TypeInfo info) {
			if (!RenameProperties)
				return;
			foreach (var propDef in info.type.AllPropertiesSorted) {
				var propInfo = memberInfos.prop(propDef);
				if (!propInfo.gotNewName())
					continue;
				propDef.PropertyDefinition.Name = propInfo.newName;
				Log.v("Property: {0} ({1:X8}) => {2}", propInfo.oldFullName, propDef.PropertyDefinition.MetadataToken.ToUInt32(), propDef.PropertyDefinition.FullName);
			}
		}

		void renameEvents(TypeInfo info) {
			if (!RenameEvents)
				return;
			foreach (var eventDef in info.type.AllEventsSorted) {
				var eventInfo = memberInfos.evt(eventDef);
				if (!eventInfo.gotNewName())
					continue;
				eventDef.EventDefinition.Name = eventInfo.newName;
				Log.v("Event: {0} ({1:X8}) => {2}", eventInfo.oldFullName, eventDef.EventDefinition.MetadataToken.ToUInt32(), eventDef.EventDefinition.FullName);
			}
		}

		void renameMethods(TypeInfo info) {
			if (!RenameMethods && !RenameMethodArgs && !RenameGenericParams)
				return;
			foreach (var methodDef in info.type.AllMethodsSorted) {
				var methodInfo = memberInfos.method(methodDef);
				Log.v("Method {0} ({1:X8})", methodInfo.oldFullName, methodDef.MethodDefinition.MetadataToken.ToUInt32());
				Log.indent();

				renameGenericParams(methodDef.GenericParams);

				if (RenameMethods && methodInfo.gotNewName()) {
					methodDef.MethodDefinition.Name = methodInfo.newName;
					Log.v("Name: {0} => {1}", methodInfo.oldFullName, methodDef.MethodDefinition.FullName);
				}

				if (RenameMethodArgs) {
					foreach (var param in methodDef.ParamDefs) {
						var paramInfo = memberInfos.param(param);
						if (!paramInfo.gotNewName())
							continue;
						param.ParameterDefinition.Name = paramInfo.newName;
						Log.v("Param ({0}/{1}): {2} => {3}", param.Index + 1, methodDef.ParamDefs.Count, paramInfo.oldName, paramInfo.newName);
					}
				}

				Log.deIndent();
			}
		}

		void renameMemberReferences() {
			Log.v("Renaming references to other definitions");
			foreach (var module in modules.TheModules) {
				if (modules.TheModules.Count > 1)
					Log.v("Renaming references to other definitions ({0})", module.Filename);
				Log.indent();
				foreach (var refToDef in module.MethodRefsToRename)
					refToDef.reference.Name = refToDef.definition.Name;
				foreach (var refToDef in module.FieldRefsToRename)
					refToDef.reference.Name = refToDef.definition.Name;
				Log.deIndent();
			}
		}

		void renameResources() {
			Log.v("Renaming resources");
			foreach (var module in modules.TheModules) {
				if (modules.TheModules.Count > 1)
					Log.v("Renaming resources ({0})", module.Filename);
				Log.indent();
				renameResources(module);
				Log.deIndent();
			}
		}

		void renameResources(Module module) {
			var renamedTypes = new List<TypeInfo>();
			foreach (var type in module.getAllTypes()) {
				var info = memberInfos.type(type);
				if (info.oldFullName != info.type.TypeDefinition.FullName)
					renamedTypes.Add(info);
			}
			if (renamedTypes.Count == 0)
				return;

			// Rename the longest names first. Otherwise eg. b.g.resources could be renamed
			// Class0.g.resources instead of Class1.resources when b.g was renamed Class1.
			renamedTypes.Sort((a, b) => Utils.compareInt32(b.oldFullName.Length, a.oldFullName.Length));

			renameResourceNamesInCode(module, renamedTypes);
			renameResources(module, renamedTypes);
		}

		void renameResourceNamesInCode(Module module, IEnumerable<TypeInfo> renamedTypes) {
			// This is needed to speed up this method
			var oldToNewTypeName = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var info in renamedTypes)
				oldToNewTypeName[info.oldFullName] = info.type.TypeDefinition.FullName;

			List<string> validResourceNames = new List<string>();
			if (module.ModuleDefinition.Resources != null) {
				foreach (var resource in module.ModuleDefinition.Resources) {
					var name = resource.Name;
					if (name.EndsWith(".resources", StringComparison.Ordinal))
						validResourceNames.Add(name);
				}
			}

			foreach (var method in module.getAllMethods()) {
				if (!method.HasBody)
					continue;
				foreach (var instr in method.Body.Instructions) {
					if (instr.OpCode != OpCodes.Ldstr)
						continue;
					var s = (string)instr.Operand;
					if (string.IsNullOrEmpty(s))
						continue;	// Ignore emtpy strings since we'll get lots of false warnings

					string newName = null;
					string oldName = null;
					if (oldToNewTypeName.ContainsKey(s)) {
						oldName = s;
						newName = oldToNewTypeName[s];
					}
					else if (s.EndsWith(".resources", StringComparison.Ordinal)) {
						// This should rarely, if ever, execute...
						foreach (var info in renamedTypes) {	// Slow loop
							var newName2 = renameResourceString(s, info.oldFullName, info.type.TypeDefinition.FullName);
							if (newName2 != s) {
								newName = newName2;
								oldName = info.oldFullName;
								break;
							}
						}
					}
					if (newName == null || string.IsNullOrEmpty(oldName))
						continue;

					bool isValid = false;
					foreach (var validName in validResourceNames) {
						if (Utils.StartsWith(validName, oldName, StringComparison.Ordinal)) {
							isValid = true;
							break;
						}
					}
					if (!isValid)
						continue;

					if (s == "" || !module.ObfuscatedFile.RenameResourcesInCode)
						Log.v("Possible resource name in code: '{0}' => '{1}' in method {2}", s, newName, method);
					else {
						instr.Operand = newName;
						Log.v("Renamed resource string in code: '{0}' => '{1}' ({2})", s, newName, method);
						break;
					}
				}
			}
		}

		void renameResources(Module module, IEnumerable<TypeInfo> renamedTypes) {
			if (module.ModuleDefinition.Resources == null)
				return;
			foreach (var resource in module.ModuleDefinition.Resources) {
				var s = resource.Name;
				foreach (var info in renamedTypes) {
					var newName = renameResourceString(s, info.oldFullName, info.type.TypeDefinition.FullName);
					if (newName != s) {
						resource.Name = newName;
						Log.v("Renamed resource in resources: {0} => {1}", s, newName);
						break;
					}
				}
			}
		}

		static string renameResourceString(string s, string oldTypeName, string newTypeName) {
			if (!Utils.StartsWith(s, oldTypeName, StringComparison.Ordinal))
				return s;
			if (s.Length == oldTypeName.Length)
				return newTypeName;
			// s.Length > oldTypeName.Length
			if (s[oldTypeName.Length] != '.')
				return s;
			if (!s.EndsWith(".resources", StringComparison.Ordinal))
				return s;
			return newTypeName + s.Substring(oldTypeName.Length);
		}

		// Make sure the renamed types are using valid CLS names. That means renaming all
		// generic types from eg. Class1 to Class1`2. If we don't do this, some decompilers
		// (eg. ILSpy v1.0) won't produce correct output.
		void fixClsTypeNames() {
			foreach (var type in modules.NonNestedTypes)
				fixClsTypeNames(null, type);
		}

		void fixClsTypeNames(TypeDef nesting, TypeDef nested) {
			int nestingCount = nesting == null ? 0 : nesting.GenericParams.Count;
			int arity = nested.GenericParams.Count - nestingCount;
			var nestedInfo = memberInfos.type(nested);
			if (nestedInfo.renamed && arity > 0)
				nestedInfo.newName += "`" + arity;
			foreach (var nestedType in nested.NestedTypes)
				fixClsTypeNames(nested, nestedType);
		}

		void prepareRenameTypes(IEnumerable<TypeDef> types, TypeRenamerState state) {
			foreach (var typeDef in types) {
				memberInfos.type(typeDef).prepareRenameTypes(state);
				prepareRenameTypes(typeDef.derivedTypes, state);
			}
		}

		void renameTypeReferences() {
			Log.v("Renaming references to type definitions");
			var theModules = modules.TheModules;
			foreach (var module in theModules) {
				if (theModules.Count > 1)
					Log.v("Renaming references to type definitions ({0})", module.Filename);
				Log.indent();
				foreach (var refToDef in module.TypeRefsToRename) {
					refToDef.reference.Name = refToDef.definition.Name;
					refToDef.reference.Namespace = refToDef.definition.Namespace;
				}
				Log.deIndent();
			}
		}

		void restorePropertiesAndEvents(MethodNameScopes scopes) {
			var allScopes = scopes.getAllScopes();
			restoreVirtualProperties(allScopes);
			restorePropertiesFromNames(allScopes);
			restoreVirtualEvents(allScopes);
			restoreEventsFromNames(allScopes);
		}

		void restoreVirtualProperties(IEnumerable<MethodNameScope> allScopes) {
			if (!RestoreProperties)
				return;
			foreach (var scope in allScopes)
				restoreVirtualProperties(scope);
		}

		void restoreVirtualProperties(MethodNameScope scope) {
			if (scope.Methods.Count <= 1 || !scope.hasProperty())
				return;

			PropertyDef prop = null;
			List<MethodDef> missingProps = null;
			foreach (var method in scope.Methods) {
				if (method.Property == null) {
					if (missingProps == null)
						missingProps = new List<MethodDef>();
					missingProps.Add(method);
				}
				else if (prop == null)
					prop = method.Property;
			}
			if (prop == null)
				return;	// Should never happen
			if (missingProps == null)
				return;

			foreach (var method in missingProps) {
				if (!method.Owner.HasModule)
					continue;

				if (method.MethodDefinition.MethodReturnType.ReturnType.FullName == "System.Void")
					createPropertySetter(prop.PropertyDefinition.Name, method);
				else
					createPropertyGetter(prop.PropertyDefinition.Name, method);
			}
		}

		void restorePropertiesFromNames(IEnumerable<MethodNameScope> allScopes) {
			if (!RestorePropertiesFromNames)
				return;

			foreach (var scope in allScopes) {
				var scopeMethod = scope.Methods[0];
				var methodName = scopeMethod.MethodDefinition.Name;
				bool onlyRenamableMethods = !scope.hasNonRenamableMethod();

				if (Utils.StartsWith(methodName, "get_", StringComparison.Ordinal)) {
					var propName = methodName.Substring(4);
					foreach (var method in scope.Methods) {
						if (onlyRenamableMethods && !memberInfos.type(method.Owner).NameChecker.isValidPropertyName(propName))
							continue;
						createPropertyGetter(propName, method);
					}
				}
				else if (Utils.StartsWith(methodName, "set_", StringComparison.Ordinal)) {
					var propName = methodName.Substring(4);
					foreach (var method in scope.Methods) {
						if (onlyRenamableMethods && !memberInfos.type(method.Owner).NameChecker.isValidPropertyName(propName))
							continue;
						createPropertySetter(propName, method);
					}
				}
			}

			foreach (var type in modules.AllTypes) {
				foreach (var method in type.AllMethodsSorted) {
					if (method.isVirtual())
						continue;	// Virtual methods are in allScopes, so already fixed above
					if (method.Property != null)
						continue;
					var methodName = method.MethodDefinition.Name;
					if (Utils.StartsWith(methodName, "get_", StringComparison.Ordinal))
						createPropertyGetter(methodName.Substring(4), method);
					else if (Utils.StartsWith(methodName, "set_", StringComparison.Ordinal))
						createPropertySetter(methodName.Substring(4), method);
				}
			}
		}

		PropertyDef createPropertyGetter(string name, MethodDef propMethod) {
			if (string.IsNullOrEmpty(name))
				return null;
			var ownerType = propMethod.Owner;
			if (!ownerType.HasModule)
				return null;
			if (propMethod.Property != null)
				return null;

			var method = propMethod.MethodDefinition;
			var propType = method.MethodReturnType.ReturnType;
			var propDef = createProperty(ownerType, name, propType);
			if (propDef == null)
				return null;
			if (propDef.GetMethod != null)
				return null;
			Log.v("Restoring property getter {0} ({1:X8}), Property: {2} ({3:X8})",
						propMethod,
						propMethod.MethodDefinition.MetadataToken.ToInt32(),
						propDef.PropertyDefinition,
						propDef.PropertyDefinition.MetadataToken.ToInt32());
			propDef.PropertyDefinition.GetMethod = propMethod.MethodDefinition;
			propDef.GetMethod = propMethod;
			propMethod.Property = propDef;
			return propDef;
		}

		PropertyDef createPropertySetter(string name, MethodDef propMethod) {
			if (string.IsNullOrEmpty(name))
				return null;
			var ownerType = propMethod.Owner;
			if (!ownerType.HasModule)
				return null;
			if (propMethod.Property != null)
				return null;

			var method = propMethod.MethodDefinition;
			if (method.Parameters.Count == 0)
				return null;
			var propType = method.Parameters[method.Parameters.Count - 1].ParameterType;
			var propDef = createProperty(ownerType, name, propType);
			if (propDef == null)
				return null;
			if (propDef.SetMethod != null)
				return null;
			Log.v("Restoring property setter {0} ({1:X8}), Property: {2} ({3:X8})",
						propMethod,
						propMethod.MethodDefinition.MetadataToken.ToInt32(),
						propDef.PropertyDefinition,
						propDef.PropertyDefinition.MetadataToken.ToInt32());
			propDef.PropertyDefinition.SetMethod = propMethod.MethodDefinition;
			propDef.SetMethod = propMethod;
			propMethod.Property = propDef;
			return propDef;
		}

		PropertyDef createProperty(TypeDef ownerType, string name, TypeReference propType) {
			if (string.IsNullOrEmpty(name) || propType.FullName == "System.Void")
				return null;
			var newProp = DotNetUtils.createPropertyDefinition(name, propType);
			var propDef = ownerType.find(newProp);
			if (propDef != null)
				return propDef;

			propDef = ownerType.create(newProp);
			memberInfos.add(propDef);
			Log.v("Restoring property: {0}", newProp);
			return propDef;
		}

		void restoreVirtualEvents(IEnumerable<MethodNameScope> allScopes) {
			if (!RestoreEvents)
				return;
			foreach (var scope in allScopes)
				restoreVirtualEvents(scope);
		}

		enum EventMethodType {
			None,
			Other,
			Adder,
			Remover,
			Raiser,
		}

		void restoreVirtualEvents(MethodNameScope scope) {
			if (scope.Methods.Count <= 1 || !scope.hasEvent())
				return;

			EventMethodType methodType = EventMethodType.None;
			EventDef evt = null;
			List<MethodDef> missingEvents = null;
			foreach (var method in scope.Methods) {
				if (method.Event == null) {
					if (missingEvents == null)
						missingEvents = new List<MethodDef>();
					missingEvents.Add(method);
				}
				else if (evt == null) {
					evt = method.Event;
					if (evt.AddMethod == method)
						methodType = EventMethodType.Adder;
					else if (evt.RemoveMethod == method)
						methodType = EventMethodType.Remover;
					else if (evt.RaiseMethod == method)
						methodType = EventMethodType.Raiser;
					else
						methodType = EventMethodType.Other;
				}
			}
			if (evt == null)
				return;	// Should never happen
			if (missingEvents == null)
				return;

			foreach (var method in missingEvents) {
				if (!method.Owner.HasModule)
					continue;

				switch (methodType) {
				case EventMethodType.Adder:
					createEventAdder(evt.EventDefinition.Name, method);
					break;
				case EventMethodType.Remover:
					createEventRemover(evt.EventDefinition.Name, method);
					break;
				}
			}
		}

		void restoreEventsFromNames(IEnumerable<MethodNameScope> allScopes) {
			if (!RestoreEventsFromNames)
				return;

			foreach (var scope in allScopes) {
				var scopeMethod = scope.Methods[0];
				var methodName = scopeMethod.MethodDefinition.Name;
				bool onlyRenamableMethods = !scope.hasNonRenamableMethod();

				if (Utils.StartsWith(methodName, "add_", StringComparison.Ordinal)) {
					var eventName = methodName.Substring(4);
					foreach (var method in scope.Methods) {
						if (onlyRenamableMethods && !memberInfos.type(method.Owner).NameChecker.isValidEventName(eventName))
							continue;
						createEventAdder(eventName, method);
					}
				}
				else if (Utils.StartsWith(methodName, "remove_", StringComparison.Ordinal)) {
					var eventName = methodName.Substring(7);
					foreach (var method in scope.Methods) {
						if (onlyRenamableMethods && !memberInfos.type(method.Owner).NameChecker.isValidEventName(eventName))
							continue;
						createEventRemover(eventName, method);
					}
				}
			}

			foreach (var type in modules.AllTypes) {
				foreach (var method in type.AllMethodsSorted) {
					if (method.isVirtual())
						continue;	// Virtual methods are in allScopes, so already fixed above
					if (method.Event != null)
						continue;
					var methodName = method.MethodDefinition.Name;
					if (Utils.StartsWith(methodName, "add_", StringComparison.Ordinal))
						createEventAdder(methodName.Substring(4), method);
					else if (Utils.StartsWith(methodName, "remove_", StringComparison.Ordinal))
						createEventRemover(methodName.Substring(7), method);
				}
			}
		}

		EventDef createEventAdder(string name, MethodDef eventMethod) {
			if (string.IsNullOrEmpty(name))
				return null;
			var ownerType = eventMethod.Owner;
			if (!ownerType.HasModule)
				return null;
			if (eventMethod.Event != null)
				return null;

			var method = eventMethod.MethodDefinition;
			var eventDef = createEvent(ownerType, name, getEventType(method));
			if (eventDef == null)
				return null;
			if (eventDef.AddMethod != null)
				return null;
			Log.v("Restoring event adder {0} ({1:X8}), Event: {2} ({3:X8})",
						eventMethod,
						eventMethod.MethodDefinition.MetadataToken.ToInt32(),
						eventDef.EventDefinition,
						eventDef.EventDefinition.MetadataToken.ToInt32());
			eventDef.EventDefinition.AddMethod = eventMethod.MethodDefinition;
			eventDef.AddMethod = eventMethod;
			eventMethod.Event = eventDef;
			return eventDef;
		}

		EventDef createEventRemover(string name, MethodDef eventMethod) {
			if (string.IsNullOrEmpty(name))
				return null;
			var ownerType = eventMethod.Owner;
			if (!ownerType.HasModule)
				return null;
			if (eventMethod.Event != null)
				return null;

			var method = eventMethod.MethodDefinition;
			var eventDef = createEvent(ownerType, name, getEventType(method));
			if (eventDef == null)
				return null;
			if (eventDef.RemoveMethod != null)
				return null;
			Log.v("Restoring event remover {0} ({1:X8}), Event: {2} ({3:X8})",
						eventMethod,
						eventMethod.MethodDefinition.MetadataToken.ToInt32(),
						eventDef.EventDefinition,
						eventDef.EventDefinition.MetadataToken.ToInt32());
			eventDef.EventDefinition.RemoveMethod = eventMethod.MethodDefinition;
			eventDef.RemoveMethod = eventMethod;
			eventMethod.Event = eventDef;
			return eventDef;
		}

		TypeReference getEventType(MethodReference method) {
			if (method.MethodReturnType.ReturnType.FullName != "System.Void")
				return null;
			if (method.Parameters.Count != 1)
				return null;
			return method.Parameters[0].ParameterType;
		}

		EventDef createEvent(TypeDef ownerType, string name, TypeReference eventType) {
			if (string.IsNullOrEmpty(name) || eventType == null || eventType.FullName == "System.Void")
				return null;
			var newEvent = DotNetUtils.createEventDefinition(name, eventType);
			var eventDef = ownerType.find(newEvent);
			if (eventDef != null)
				return eventDef;

			eventDef = ownerType.create(newEvent);
			memberInfos.add(eventDef);
			Log.v("Restoring event: {0}", newEvent);
			return eventDef;
		}

		void prepareRenameMemberDefinitions(MethodNameScopes scopes) {
			Log.v("Renaming member definitions #1");

			prepareRenameEntryPoints();

			var virtualMethods = new ScopeHelper(memberInfos, modules.AllTypes);
			var ifaceMethods = new ScopeHelper(memberInfos, modules.AllTypes);
			var propMethods = new ScopeHelper(memberInfos, modules.AllTypes);
			var eventMethods = new ScopeHelper(memberInfos, modules.AllTypes);
			foreach (var scope in getSorted(scopes)) {
				if (scope.hasNonRenamableMethod())
					continue;
				else if (scope.hasGetterOrSetterPropertyMethod() && getPropertyMethodType(scope.Methods[0]) != PropertyMethodType.Other)
					propMethods.add(scope);
				else if (scope.hasAddRemoveOrRaiseEventMethod())
					eventMethods.add(scope);
				else if (scope.hasInterfaceMethod())
					ifaceMethods.add(scope);
				else
					virtualMethods.add(scope);
			}

			var prepareHelper = new PrepareHelper(memberInfos, modules.AllTypes);
			prepareHelper.prepare((info) => info.prepareRenameMembers());

			prepareHelper.prepare((info) => info.prepareRenamePropsAndEvents());
			propMethods.visitAll((scope) => prepareRenameProperty(scope, false));
			eventMethods.visitAll((scope) => prepareRenameEvent(scope, false));
			propMethods.visitAll((scope) => prepareRenameProperty(scope, true));
			eventMethods.visitAll((scope) => prepareRenameEvent(scope, true));

			foreach (var typeDef in modules.AllTypes)
				memberInfos.type(typeDef).initializeEventHandlerNames();

			prepareHelper.prepare((info) => info.prepareRenameMethods());
			ifaceMethods.visitAll((scope) => prepareRenameVirtualMethods(scope, "imethod_", false));
			virtualMethods.visitAll((scope) => prepareRenameVirtualMethods(scope, "vmethod_", false));
			ifaceMethods.visitAll((scope) => prepareRenameVirtualMethods(scope, "imethod_", true));
			virtualMethods.visitAll((scope) => prepareRenameVirtualMethods(scope, "vmethod_", true));

			foreach (var typeDef in modules.AllTypes)
				memberInfos.type(typeDef).prepareRenameMethods2();
		}

		class PrepareHelper {
			Dictionary<TypeDef, bool> prepareMethodCalled = new Dictionary<TypeDef, bool>();
			MemberInfos memberInfos;
			Action<TypeInfo> func;
			IEnumerable<TypeDef> allTypes;

			public PrepareHelper(MemberInfos memberInfos, IEnumerable<TypeDef> allTypes) {
				this.memberInfos = memberInfos;
				this.allTypes = allTypes;
			}

			public void prepare(Action<TypeInfo> func) {
				this.func = func;
				prepareMethodCalled.Clear();
				foreach (var typeDef in allTypes)
					prepare(typeDef);
			}

			void prepare(TypeDef type) {
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

		static List<MethodNameScope> getSorted(MethodNameScopes scopes) {
			var allScopes = new List<MethodNameScope>(scopes.getAllScopes());
			allScopes.Sort((a, b) => Utils.compareInt32(b.Count, a.Count));
			return allScopes;
		}

		class ScopeHelper {
			MemberInfos memberInfos;
			Dictionary<TypeDef, bool> visited = new Dictionary<TypeDef, bool>();
			Dictionary<MethodDef, MethodNameScope> methodToScope;
			List<MethodNameScope> scopes = new List<MethodNameScope>();
			IEnumerable<TypeDef> allTypes;
			Action<MethodNameScope> func;

			public ScopeHelper(MemberInfos memberInfos, IEnumerable<TypeDef> allTypes) {
				this.memberInfos = memberInfos;
				this.allTypes = allTypes;
			}

			public void add(MethodNameScope scope) {
				scopes.Add(scope);
			}

			public void visitAll(Action<MethodNameScope> func) {
				this.func = func;
				visited.Clear();

				methodToScope = new Dictionary<MethodDef, MethodNameScope>();
				foreach (var scope in scopes) {
					foreach (var method in scope.Methods)
						methodToScope[method] = scope;
				}

				foreach (var type in allTypes)
					visit(type);
			}

			void visit(TypeDef type) {
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
					MethodNameScope scope;
					if (!methodToScope.TryGetValue(method, out scope))
						continue;
					foreach (var m in scope.Methods)
						methodToScope.Remove(m);
					func(scope);
				}
			}
		}

		static readonly Regex removeGenericsArityRegex = new Regex(@"`[0-9]+");
		static string getOverridePrefix(MethodNameScope scope, MethodDef method) {
			if (method == null || method.MethodDefinition.Overrides.Count == 0)
				return "";
			if (scope.Methods.Count > 1) {
				// Don't use an override prefix if the scope has an iface method.
				foreach (var m in scope.Methods) {
					if (m.Owner.TypeDefinition.IsInterface)
						return "";
				}
			}
			var overrideMethod = method.MethodDefinition.Overrides[0];
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

		void prepareRenameEvent(MethodNameScope scope, bool renameOverrides) {
			string methodPrefix, overridePrefix;
			var eventName = prepareRenameEvent(scope, renameOverrides, out overridePrefix, out methodPrefix);
			if (eventName == null)
				return;

			var methodName = overridePrefix + methodPrefix + eventName;
			foreach (var method in scope.Methods)
				memberInfos.method(method).rename(methodName);
		}

		string prepareRenameEvent(MethodNameScope scope, bool renameOverrides, out string overridePrefix, out string methodPrefix) {
			var eventMethod = getEventMethod(scope);
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

			overridePrefix = getOverridePrefix(scope, eventMethod);
			if (renameOverrides && overridePrefix == "")
				return null;
			if (!renameOverrides && overridePrefix != "")
				return null;

			string newEventName, oldEventName;
			var eventInfo = memberInfos.evt(eventDef);

			if (overridePrefix == "")
				oldEventName = eventInfo.oldName;
			else {
				var overriddenEventDef = getOverriddenEvent(eventMethod);
				if (overriddenEventDef == null)
					oldEventName = getRealName(eventInfo.oldName);
				else
					oldEventName = getRealName(memberInfos.evt(overriddenEventDef).newName);
			}

			if (eventInfo.renamed)
				newEventName = getRealName(eventInfo.newName);
			else if (eventDef.Owner.Module.ObfuscatedFile.NameChecker.isValidEventName(oldEventName))
				newEventName = oldEventName;
			else {
				mergeStateHelper.merge(MergeStateFlags.Events, scope);
				newEventName = getAvailableName("Event_", scope, (scope2, newName) => isEventAvailable(scope2, newName));
			}

			var newEventNameWithPrefix = overridePrefix + newEventName;
			foreach (var method in scope.Methods) {
				if (method.Event != null) {
					memberInfos.evt(method.Event).rename(newEventNameWithPrefix);
					var ownerInfo = memberInfos.type(method.Owner);
					ownerInfo.variableNameState.addEventName(newEventName);
					ownerInfo.variableNameState.addEventName(newEventNameWithPrefix);
				}
			}

			return newEventName;
		}

		EventDef getOverriddenEvent(MethodDef overrideMethod) {
			var overriddenMethod = modules.resolve(overrideMethod.MethodDefinition.Overrides[0]);
			if (overriddenMethod == null)
				return null;
			return overriddenMethod.Event;
		}

		MethodDef getEventMethod(MethodNameScope scope) {
			foreach (var method in scope.Methods) {
				if (method.Event != null)
					return method;
			}
			return null;
		}

		void prepareRenameProperty(MethodNameScope scope, bool renameOverrides) {
			string overridePrefix;
			var propName = prepareRenameProperty(scope, renameOverrides, out overridePrefix);
			if (propName == null)
				return;

			string methodPrefix;
			switch (getPropertyMethodType(scope.Methods[0])) {
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
			foreach (var method in scope.Methods)
				memberInfos.method(method).rename(methodName);
		}

		string prepareRenameProperty(MethodNameScope scope, bool renameOverrides, out string overridePrefix) {
			var propMethod = getPropertyMethod(scope);
			if (propMethod == null)
				throw new ApplicationException("No properties found");

			overridePrefix = getOverridePrefix(scope, propMethod);

			if (renameOverrides && overridePrefix == "")
				return null;
			if (!renameOverrides && overridePrefix != "")
				return null;

			string newPropName, oldPropName;
			var propDef = propMethod.Property;
			var propInfo = memberInfos.prop(propDef);

			if (overridePrefix == "")
				oldPropName = propInfo.oldName;
			else {
				var overriddenPropDef = getOverriddenProperty(propMethod);
				if (overriddenPropDef == null)
					oldPropName = getRealName(propInfo.oldName);
				else
					oldPropName = getRealName(memberInfos.prop(overriddenPropDef).newName);
			}

			if (propInfo.renamed)
				newPropName = getRealName(propInfo.newName);
			else if (propDef.Owner.Module.ObfuscatedFile.NameChecker.isValidPropertyName(oldPropName))
				newPropName = oldPropName;
			else {
				var propPrefix = getSuggestedPropertyName(scope) ?? getNewPropertyNamePrefix(scope);
				mergeStateHelper.merge(MergeStateFlags.Properties, scope);
				newPropName = getAvailableName(propPrefix, scope, (scope2, newName) => isPropertyAvailable(scope2, newName));
			}

			var newPropNameWithPrefix = overridePrefix + newPropName;
			foreach (var method in scope.Methods) {
				if (method.Property != null) {
					memberInfos.prop(method.Property).rename(newPropNameWithPrefix);
					var ownerInfo = memberInfos.type(method.Owner);
					ownerInfo.variableNameState.addPropertyName(newPropName);
					ownerInfo.variableNameState.addPropertyName(newPropNameWithPrefix);
				}
			}

			return newPropName;
		}

		PropertyDef getOverriddenProperty(MethodDef overrideMethod) {
			var overriddenMethod = modules.resolve(overrideMethod.MethodDefinition.Overrides[0]);
			if (overriddenMethod == null)
				return null;
			return overriddenMethod.Property;
		}

		MethodDef getPropertyMethod(MethodNameScope scope) {
			foreach (var method in scope.Methods) {
				if (method.Property != null)
					return method;
			}
			return null;
		}

		string getSuggestedPropertyName(MethodNameScope scope) {
			foreach (var method in scope.Methods) {
				if (method.Property == null)
					continue;
				var info = memberInfos.prop(method.Property);
				if (info.suggestedName != null)
					return info.suggestedName;
			}
			return null;
		}

		string getNewPropertyNamePrefix(MethodNameScope scope) {
			const string defaultVal = "Prop_";

			var propType = getPropertyType(scope);
			if (propType == null || propType is GenericInstanceType)
				return defaultVal;

			string name = propType.Name;
			int i;
			if ((i = name.IndexOf('`')) >= 0)
				name = name.Substring(0, i);
			if ((i = name.LastIndexOf('.')) >= 0)
				name = name.Substring(i + 1);
			if (name == "")
				return defaultVal;
			return name + "_";
		}

		enum PropertyMethodType {
			Other,
			Getter,
			Setter,
		}

		static PropertyMethodType getPropertyMethodType(MethodDef method) {
			if (method.MethodDefinition.MethodReturnType.ReturnType.FullName != "System.Void")
				return PropertyMethodType.Getter;
			if (method.ParamDefs.Count > 0)
				return PropertyMethodType.Setter;
			return PropertyMethodType.Other;
		}

		// Returns property type, or null if not all methods have the same type
		TypeReference getPropertyType(MethodNameScope scope) {
			var methodType = getPropertyMethodType(scope.Methods[0]);
			if (methodType == PropertyMethodType.Other)
				return null;

			TypeReference type = null;
			foreach (var propMethod in scope.Methods) {
				TypeReference propType;
				if (methodType == PropertyMethodType.Setter)
					propType = propMethod.ParamDefs[propMethod.ParamDefs.Count - 1].ParameterDefinition.ParameterType;
				else
					propType = propMethod.MethodDefinition.MethodReturnType.ReturnType;
				if (type == null)
					type = propType;
				else if (!MemberReferenceHelper.compareTypes(type, propType))
					return null;
			}
			return type;
		}

		MethodDef getOverrideMethod(MethodNameScope scope) {
			foreach (var method in scope.Methods) {
				if (method.MethodDefinition.Overrides.Count > 0)
					return method;
			}
			return null;
		}

		void prepareRenameVirtualMethods(MethodNameScope scope, string namePrefix, bool renameOverrides) {
			if (!hasInvalidMethodName(scope))
				return;

			if (hasDelegateOwner(scope)) {
				switch (scope.Methods[0].MethodDefinition.Name) {
				case "Invoke":
				case "BeginInvoke":
				case "EndInvoke":
					return;
				}
			}

			var overrideMethod = getOverrideMethod(scope);
			var overridePrefix = getOverridePrefix(scope, overrideMethod);
			if (renameOverrides && overridePrefix == "")
				return;
			if (!renameOverrides && overridePrefix != "")
				return;

			string newMethodName;
			if (overridePrefix != "") {
				var overrideInfo = memberInfos.method(overrideMethod);
				var overriddenMethod = getOverriddenMethod(overrideMethod);
				if (overriddenMethod == null)
					newMethodName = getRealName(overrideMethod.MethodDefinition.Overrides[0].Name);
				else
					newMethodName = getRealName(memberInfos.method(overriddenMethod).newName);
			}
			else {
				newMethodName = getSuggestedMethodName(scope);
				if (newMethodName == null) {
					mergeStateHelper.merge(MergeStateFlags.Methods, scope);
					newMethodName = getAvailableName(namePrefix, scope, (scope2, newName) => isMethodAvailable(scope2, newName));
				}
			}

			var newMethodNameWithPrefix = overridePrefix + newMethodName;
			foreach (var method in scope.Methods)
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
			Dictionary<TypeDef, bool> visited = new Dictionary<TypeDef, bool>();

			public MergeStateHelper(MemberInfos memberInfos) {
				this.memberInfos = memberInfos;
			}

			public void merge(MergeStateFlags flags, MethodNameScope scope) {
				this.flags = flags;
				visited.Clear();
				foreach (var method in scope.Methods)
					merge(method.Owner);
			}

			void merge(TypeDef type) {
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

			void merge(TypeInfo info, TypeDef other) {
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

		MethodDef getOverriddenMethod(MethodDef overrideMethod) {
			return modules.resolve(overrideMethod.MethodDefinition.Overrides[0]);
		}

		string getSuggestedMethodName(MethodNameScope scope) {
			foreach (var method in scope.Methods) {
				var info = memberInfos.method(method);
				if (info.suggestedName != null)
					return info.suggestedName;
			}
			return null;
		}

		bool hasInvalidMethodName(MethodNameScope scope) {
			foreach (var method in scope.Methods) {
				var typeInfo = memberInfos.type(method.Owner);
				var methodInfo = memberInfos.method(method);
				if (!typeInfo.NameChecker.isValidMethodName(methodInfo.oldName))
					return true;
			}
			return false;
		}

		static string getAvailableName(string prefix, MethodNameScope scope, Func<MethodNameScope, string, bool> checkAvailable) {
			for (int i = 0; ; i++) {
				string newName = prefix + i;
				if (checkAvailable(scope, newName))
					return newName;
			}
		}

		bool isMethodAvailable(MethodNameScope scope, string methodName) {
			foreach (var method in scope.Methods) {
				if (memberInfos.type(method.Owner).variableNameState.isMethodNameUsed(methodName))
					return false;
			}
			return true;
		}

		bool isPropertyAvailable(MethodNameScope scope, string methodName) {
			foreach (var method in scope.Methods) {
				if (memberInfos.type(method.Owner).variableNameState.isPropertyNameUsed(methodName))
					return false;
			}
			return true;
		}

		bool isEventAvailable(MethodNameScope scope, string methodName) {
			foreach (var method in scope.Methods) {
				if (memberInfos.type(method.Owner).variableNameState.isEventNameUsed(methodName))
					return false;
			}
			return true;
		}

		bool hasDelegateOwner(MethodNameScope scope) {
			foreach (var method in scope.Methods) {
				if (isDelegateClass.check(method.Owner))
					return true;
			}
			return false;
		}

		void prepareRenameEntryPoints() {
			foreach (var module in modules.TheModules) {
				var entryPoint = module.ModuleDefinition.EntryPoint;
				if (entryPoint == null)
					continue;
				var methodDef = modules.resolve(entryPoint);
				if (methodDef == null) {
					Log.w(string.Format("Could not find entry point. Module: {0}, Method: {1}", module.ModuleDefinition.FullyQualifiedName, entryPoint));
					continue;
				}
				if (!methodDef.isStatic())
					continue;
				memberInfos.method(methodDef).suggestedName = "Main";
				if (methodDef.ParamDefs.Count == 1) {
					var paramDef = methodDef.ParamDefs[0];
					var type = paramDef.ParameterDefinition.ParameterType;
					if (type.FullName == "System.String[]")
						memberInfos.param(paramDef).newName = "args";
				}
			}
		}
	}
}
