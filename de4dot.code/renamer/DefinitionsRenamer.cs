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
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.renamer {
	// Renames all typedefs, methoddefs, eventdefs, fielddefs, and propdefs
	class DefinitionsRenamer : IResolver, IDefFinder {
		// All types that don't derive from an existing type definition (most likely mscorlib
		// isn't loaded, so this won't have just one element).
		IList<TypeDef> baseTypes = new List<TypeDef>();
		IList<TypeDef> nonNestedTypes;
		IList<Module> modules = new List<Module>();
		List<TypeDef> allTypes = new List<TypeDef>();
		IDictionary<MethodDefinition, MethodDef> methodToMethodDef = new Dictionary<MethodDefinition, MethodDef>();
		TypeNameState typeNameState;
		ModulesDict modulesDict = new ModulesDict();
		AssemblyHash assemblyHash = new AssemblyHash();

		class AssemblyHash {
			IDictionary<string, ModuleHash> assemblyHash = new Dictionary<string, ModuleHash>(StringComparer.Ordinal);

			public void add(Module module) {
				ModuleHash moduleHash;
				var key = getModuleKey(module);
				if (!assemblyHash.TryGetValue(key, out moduleHash))
					assemblyHash[key] = moduleHash = new ModuleHash();
				moduleHash.add(module);
			}

			string getModuleKey(Module module) {
				if (module.ModuleDefinition.Assembly != null)
					return module.ModuleDefinition.Assembly.ToString();
				return Utils.getBaseName(module.ModuleDefinition.FullyQualifiedName);
			}

			public ModuleHash lookup(string assemblyName) {
				ModuleHash moduleHash;
				if (assemblyHash.TryGetValue(assemblyName, out moduleHash))
					return moduleHash;
				return null;
			}
		}

		class ModuleHash {
			ModulesDict modulesDict = new ModulesDict();
			Module mainModule = null;

			public void add(Module module) {
				var asm = module.ModuleDefinition.Assembly;
				if (asm != null && ReferenceEquals(asm.MainModule, module.ModuleDefinition)) {
					if (mainModule != null)
						throw new UserException(string.Format("Two modules in the same assembly are main modules. If 32-bit vs 64-bit, don't use both assemblies at the same time! \"{0}\" and \"{1}\"", module.ModuleDefinition.FullyQualifiedName, mainModule.ModuleDefinition.FullyQualifiedName));
					mainModule = module;
				}

				modulesDict.add(module);
			}

			public IEnumerable<Module> Modules {
				get { return modulesDict.Modules; }
			}
		}

		class ModulesDict {
			IDictionary<string, Module> modulesDict = new Dictionary<string, Module>(StringComparer.OrdinalIgnoreCase);

			public void add(Module module) {
				if (lookup(module.Pathname) != null)
					throw new ApplicationException(string.Format("Module \"{0}\" was found twice", module.Pathname));
				modulesDict[module.Pathname] = module;
			}

			public Module lookup(string pathname) {
				Module module;
				if (modulesDict.TryGetValue(pathname, out module))
					return module;
				return null;
			}

			public IEnumerable<Module> Modules {
				get { return modulesDict.Values; }
			}
		}

		public DefinitionsRenamer(IEnumerable<IObfuscatedFile> files) {
			foreach (var file in files) {
				var module = new Module(file);
				modulesDict.add(module);
				modules.Add(module);
				assemblyHash.add(module);
			}
		}

		public void renameAll() {
			if (modules.Count == 0)
				return;
			Log.n("Renaming all obfuscated names");
			findAllMemberReferences();
			resolveAllRefs();
			initAllTypes();
			renameTypeDefinitions();
			renameTypeReferences();
			prepareRenameMemberDefinitions();
			renameMemberDefinitions();
			renameMemberReferences();
			renameResources();
		}

		void initAllTypes() {
			foreach (var module in modules)
				allTypes.AddRange(module.getAllTypes());

			var typeToTypeDef = new Dictionary<TypeDefinition, TypeDef>();
			foreach (var typeDef in allTypes)
				typeToTypeDef[typeDef.TypeDefinition] = typeDef;

			// Initialize Owner
			foreach (var typeDef in allTypes) {
				if (typeDef.TypeDefinition.DeclaringType != null)
					typeDef.Owner = typeToTypeDef[typeDef.TypeDefinition.DeclaringType];
			}

			// Initialize baseType and derivedTypes
			foreach (var typeDef in allTypes) {
				var baseType = typeDef.TypeDefinition.BaseType;
				if (baseType == null)
					continue;
				var baseTypeDefinition = resolve(baseType);
				if (baseTypeDefinition == null)
					continue;
				var baseTypeDef = typeToTypeDef[baseTypeDefinition];
				typeDef.baseType = new TypeInfo(baseType, baseTypeDef);
				baseTypeDef.derivedTypes.Add(typeDef);
			}

			// Initialize interfaces
			foreach (var typeDef in allTypes) {
				if (typeDef.TypeDefinition.Interfaces == null)
					continue;
				foreach (var iface in typeDef.TypeDefinition.Interfaces) {
					var ifaceTypeDefinition = resolve(iface);
					if (ifaceTypeDefinition != null)
						typeDef.interfaces.Add(new TypeInfo(iface, typeToTypeDef[ifaceTypeDefinition]));
				}
			}

			// Find all non-nested types
			var allTypesDict = new Dictionary<TypeDef, bool>();
			foreach (var t in allTypes)
				allTypesDict[t] = true;
			foreach (var t in allTypes) {
				foreach (var t2 in t.NestedTypes)
					allTypesDict.Remove(t2);
			}
			nonNestedTypes = new List<TypeDef>(allTypesDict.Keys);

			// So we can quickly look up MethodDefs
			foreach (var typeDef in allTypes) {
				foreach (var methodDef in typeDef.Methods)
					methodToMethodDef[methodDef.MethodDefinition] = methodDef;
				typeDef.defFinder = this;
			}

			foreach (var typeDef in allTypes) {
				if (typeDef.baseType == null)
					baseTypes.Add(typeDef);
			}
		}

		void findAllMemberReferences() {
			Log.v("Finding all MemberReferences");
			int index = 0;
			foreach (var module in modules) {
				if (modules.Count > 1)
					Log.v("Finding all MemberReferences ({0})", module.Filename);
				Log.indent();
				module.findAllMemberReferences(ref index);
				Log.deIndent();
			}
		}

		void resolveAllRefs() {
			Log.v("Resolving references");
			foreach (var module in modules) {
				if (modules.Count > 1)
					Log.v("Resolving references ({0})", module.Filename);
				Log.indent();
				module.resolveAllRefs(this);
				Log.deIndent();
			}
		}

		void renameTypeDefinitions() {
			Log.v("Renaming obfuscated type definitions");
			typeNameState = new TypeNameState();
			prepareRenameTypeDefinitions(baseTypes);
			typeNameState = null;

			fixClsTypeNames();
			renameTypeDefinitions(nonNestedTypes);

			foreach (var module in modules)
				module.onTypesRenamed();
		}

		void prepareRenameTypeDefinitions(IEnumerable<TypeDef> typeDefs) {
			foreach (var typeDef in typeDefs) {
				typeNameState.IsValidName = typeDef.module.IsValidName;
				typeDef.prepareRename(typeNameState);
				prepareRenameTypeDefinitions(typeDef.derivedTypes);
			}
		}

		void renameTypeDefinitions(IEnumerable<TypeDef> typeDefs) {
			Log.indent();
			foreach (var typeDef in typeDefs) {
				typeDef.rename();
				renameTypeDefinitions(typeDef.NestedTypes);
			}
			Log.deIndent();
		}

		// Make sure the renamed types are using valid CLS names. That means renaming all
		// generic types from eg. Class1 to Class1`2. If we don't do this, some decompilers
		// (eg. ILSpy v1.0) won't produce correct output.
		void fixClsTypeNames() {
			foreach (var type in nonNestedTypes)
				fixClsTypeNames(null, type);
		}

		void fixClsTypeNames(TypeDef nesting, TypeDef nested) {
			int nestingCount = nesting == null ? 0 : nesting.GenericParams.Count;
			int arity = nested.GenericParams.Count - nestingCount;
			if (nested.gotNewName() && arity > 0)
				nested.NewName += "`" + arity;
			foreach (var nestedType in nested.NestedTypes)
				fixClsTypeNames(nested, nestedType);
		}

		void renameTypeReferences() {
			Log.v("Renaming references to type definitions");
			foreach (var module in modules) {
				if (modules.Count > 1)
					Log.v("Renaming references to type definitions ({0})", module.Filename);
				Log.indent();
				module.renameTypeReferences();
				Log.deIndent();
			}
		}

		class InterfaceScope {
			Dictionary<TypeDef, bool> interfaces = new Dictionary<TypeDef, bool>();
			Dictionary<TypeDef, bool> classes = new Dictionary<TypeDef, bool>();

			public IEnumerable<TypeDef> Interfaces {
				get { return interfaces.Keys; }
			}

			public IEnumerable<TypeDef> Classes {
				get { return classes.Keys; }
			}

			public void addInterfaces(IEnumerable<TypeDef> list) {
				foreach (var iface in list)
					interfaces[iface] = true;
			}

			public void addClass(TypeDef cls) {
				classes[cls] = true;
			}

			public void merge(InterfaceScope other) {
				if (ReferenceEquals(this, other))
					return;
				addInterfaces(other.interfaces.Keys);
				foreach (var cls in other.classes.Keys)
					addClass(cls);
			}
		}

		void prepareRenameMemberDefinitions() {
			Log.v("Renaming member definitions #1");

			var interfaceScopes = createInterfaceScopes();
			foreach (var interfaceScope in interfaceScopes) {
				var state = new MemberRenameState(new InterfaceVariableNameState());
				foreach (var iface in interfaceScope.Interfaces)
					iface.MemberRenameState = state.cloneVariables();
				foreach (var cls in interfaceScope.Classes) {
					if (cls.isInterface())
						continue;
					cls.InterfaceScopeState = state;
				}
			}
			foreach (var interfaceScope in interfaceScopes) {
				foreach (var iface in interfaceScope.Interfaces)
					iface.prepareRenameMembers();
			}

			var variableNameState = new VariableNameState();
			foreach (var typeDef in baseTypes) {
				var state = new MemberRenameState(variableNameState.clone());
				typeDef.MemberRenameState = state;
			}

			foreach (var typeDef in allTypes)
				typeDef.prepareRenameMembers();

			renameEntryPoints();
		}

		void renameEntryPoints() {
			foreach (var module in modules) {
				var entryPoint = module.ModuleDefinition.EntryPoint;
				if (entryPoint == null)
					continue;
				var methodDef = findMethod(entryPoint);
				if (methodDef == null)
					throw new ApplicationException(string.Format("Could not find entry point. Module: {0}, Method: {1}", module.ModuleDefinition.FullyQualifiedName, entryPoint));
				if (!methodDef.MethodDefinition.IsStatic)
					continue;
				methodDef.NewName = "Main";
				if (methodDef.ParamDefs.Count == 1) {
					var paramDef = methodDef.ParamDefs[0];
					var type = paramDef.ParameterDefinition.ParameterType;
					if (MemberReferenceHelper.verifyType(type, "mscorlib", "System.String", "[]"))
						paramDef.NewName = "args";
				}
			}
		}

		class InterfaceScopeInfo {
			public TypeDef theClass;
			public List<TypeDef> interfaces;
			public InterfaceScopeInfo(TypeDef theClass, List<TypeDef> interfaces) {
				this.theClass = theClass;
				this.interfaces = interfaces;
			}
		}

		IList<InterfaceScope> createInterfaceScopes() {
			var interfaceScopes = new Dictionary<TypeDef, InterfaceScope>();
			foreach (var scopeInfo in getInterfaceScopeInfo(baseTypes)) {
				InterfaceScope interfaceScope = null;
				foreach (var iface in scopeInfo.interfaces) {
					if (interfaceScopes.TryGetValue(iface, out interfaceScope))
						break;
				}
				List<InterfaceScope> mergeScopes = null;
				if (interfaceScope == null)
					interfaceScope = new InterfaceScope();
				else {
					// Find all interfaces in scopeInfo.interfaces that are in another
					// InterfaceScope, and merge them with interfaceScope.
					foreach (var iface in scopeInfo.interfaces) {
						InterfaceScope scope;
						if (!interfaceScopes.TryGetValue(iface, out scope))
							continue;	// not in any scope yet
						if (ReferenceEquals(scope, interfaceScope))
							continue;	// same scope

						if (mergeScopes == null)
							mergeScopes = new List<InterfaceScope>();
						mergeScopes.Add(scope);
					}
				}

				foreach (var iface in scopeInfo.interfaces)
					interfaceScopes[iface] = interfaceScope;
				if (mergeScopes != null) {
					foreach (var scope in mergeScopes) {
						interfaceScope.merge(scope);
						foreach (var iface in scope.Interfaces)
							interfaceScopes[iface] = interfaceScope;
					}
				}
				interfaceScope.addInterfaces(scopeInfo.interfaces);
				interfaceScope.addClass(scopeInfo.theClass);
			}

			return new List<InterfaceScope>(Utils.unique(interfaceScopes.Values));
		}

		IEnumerable<InterfaceScopeInfo> getInterfaceScopeInfo(IEnumerable<TypeDef> baseTypes) {
			foreach (var typeDef in baseTypes) {
				yield return new InterfaceScopeInfo(typeDef, new List<TypeDef>(typeDef.getAllInterfaces()));
			}
		}

		void renameMemberDefinitions() {
			Log.v("Renaming member definitions #2");

			Log.indent();
			foreach (var typeDef in allTypes)
				typeDef.renameMembers();
			Log.deIndent();
		}

		void renameMemberReferences() {
			Log.v("Renaming references to other definitions");
			foreach (var module in modules) {
				if (modules.Count > 1)
					Log.v("Renaming references to other definitions ({0})", module.Filename);
				Log.indent();
				module.renameMemberReferences();
				Log.deIndent();
			}
		}

		void renameResources() {
			Log.v("Renaming resources");
			foreach (var module in modules) {
				if (modules.Count > 1)
					Log.v("Renaming resources ({0})", module.Filename);
				Log.indent();
				module.renameResources();
				Log.deIndent();
			}
		}

		// Returns null if it's a non-loaded module/assembly
		IEnumerable<Module> findModules(IMetadataScope scope) {
			if (scope is AssemblyNameReference) {
				var assemblyRef = (AssemblyNameReference)scope;
				var moduleHash = assemblyHash.lookup(assemblyRef.ToString());
				if (moduleHash != null)
					return moduleHash.Modules;
			}
			else if (scope is ModuleDefinition) {
				var moduleDefinition = (ModuleDefinition)scope;
				var module = modulesDict.lookup(moduleDefinition.FullyQualifiedName);
				if (module != null)
					return new List<Module> { module };
			}
			else
				throw new ApplicationException(string.Format("IMetadataScope is an unsupported type: {0}", scope.GetType()));

			return null;
		}

		bool isAutoCreatedType(TypeReference typeReference) {
			return typeReference is ArrayType || typeReference is PointerType;
		}

		public TypeDefinition resolve(TypeReference typeReference) {
			var modules = findModules(typeReference.Scope);
			if (modules == null)
				return null;
			foreach (var module in modules) {
				var rv = module.resolve(typeReference);
				if (rv != null)
					return rv;
			}
			if (isAutoCreatedType(typeReference))
				return null;
			throw new ApplicationException(string.Format("Could not resolve TypeReference {0}", typeReference));
		}

		public MethodDefinition resolve(MethodReference methodReference) {
			if (methodReference.DeclaringType == null)
				return null;
			var modules = findModules(methodReference.DeclaringType.Scope);
			if (modules == null)
				return null;
			foreach (var module in modules) {
				var rv = module.resolve(methodReference);
				if (rv != null)
					return rv;
			}
			if (isAutoCreatedType(methodReference.DeclaringType))
				return null;
			throw new ApplicationException(string.Format("Could not resolve MethodReference {0}", methodReference));
		}

		public FieldDefinition resolve(FieldReference fieldReference) {
			if (fieldReference.DeclaringType == null)
				return null;
			var modules = findModules(fieldReference.DeclaringType.Scope);
			if (modules == null)
				return null;
			foreach (var module in modules) {
				var rv = module.resolve(fieldReference);
				if (rv != null)
					return rv;
			}
			if (isAutoCreatedType(fieldReference.DeclaringType))
				return null;
			throw new ApplicationException(string.Format("Could not resolve FieldReference {0}", fieldReference));
		}

		public MethodDef findMethod(MethodReference methodReference) {
			var method = resolve(methodReference);
			if (method == null)
				return null;
			return methodToMethodDef[method];
		}

		public PropertyDef findProp(MethodReference methodReference) {
			var methodDef = findMethod(methodReference);
			if (methodDef == null)
				return null;
			return methodDef.Property;
		}

		public EventDef findEvent(MethodReference methodReference) {
			var methodDef = findMethod(methodReference);
			if (methodDef == null)
				return null;
			return methodDef.Event;
		}
	}
}
