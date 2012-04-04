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
	class Modules : IResolver {
		bool initializeCalled = false;
		IDeobfuscatorContext deobfuscatorContext;
		List<Module> modules = new List<Module>();
		Dictionary<ModuleDefinition, Module> modulesDict = new Dictionary<ModuleDefinition, Module>();
		AssemblyHash assemblyHash = new AssemblyHash();

		List<TypeDef> allTypes = new List<TypeDef>();
		List<TypeDef> baseTypes = new List<TypeDef>();
		List<TypeDef> nonNestedTypes;

		public IList<Module> TheModules {
			get { return modules; }
		}

		public IEnumerable<TypeDef> AllTypes {
			get { return allTypes; }
		}

		public IEnumerable<TypeDef> BaseTypes {
			get { return baseTypes; }
		}

		public List<TypeDef> NonNestedTypes {
			get { return nonNestedTypes; }
		}

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
					if (mainModule != null) {
						throw new UserException(string.Format(
							"Two modules in the same assembly are main modules.\n" +
							"Is one 32-bit and the other 64-bit?\n" +
							"  Module1: \"{0}\"" +
							"  Module2: \"{1}\"",
							module.ModuleDefinition.FullyQualifiedName,
							mainModule.ModuleDefinition.FullyQualifiedName));
					}
					mainModule = module;
				}

				modulesDict.add(module);
			}

			public Module lookup(string moduleName) {
				return modulesDict.lookup(moduleName);
			}

			public IEnumerable<Module> Modules {
				get { return modulesDict.Modules; }
			}
		}

		class ModulesDict {
			IDictionary<string, Module> modulesDict = new Dictionary<string, Module>(StringComparer.Ordinal);

			public void add(Module module) {
				var moduleName = module.ModuleDefinition.Name;
				if (lookup(moduleName) != null)
					throw new ApplicationException(string.Format("Module \"{0}\" was found twice", moduleName));
				modulesDict[moduleName] = module;
			}

			public Module lookup(string moduleName) {
				Module module;
				if (modulesDict.TryGetValue(moduleName, out module))
					return module;
				return null;
			}

			public IEnumerable<Module> Modules {
				get { return modulesDict.Values; }
			}
		}

		public bool Empty {
			get { return modules.Count == 0; }
		}

		public Modules(IDeobfuscatorContext deobfuscatorContext) {
			this.deobfuscatorContext = deobfuscatorContext;
		}

		public void add(Module module) {
			if (initializeCalled)
				throw new ApplicationException("initialize() has been called");
			Module otherModule;
			if (modulesDict.TryGetValue(module.ModuleDefinition, out otherModule))
				return;
			modulesDict[module.ModuleDefinition] = module;
			modules.Add(module);
			assemblyHash.add(module);
		}

		public void initialize() {
			initializeCalled = true;
			findAllMemberReferences();
			initAllTypes();
			resolveAllRefs();
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

		void initAllTypes() {
			foreach (var module in modules)
				allTypes.AddRange(module.getAllTypes());

			var typeToTypeDef = new Dictionary<TypeDefinition, TypeDef>(allTypes.Count);
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
				var baseTypeDef = resolve(baseType) ?? resolveOther(baseType);
				if (baseTypeDef != null) {
					typeDef.addBaseType(baseTypeDef, baseType);
					baseTypeDef.derivedTypes.Add(typeDef);
				}
			}

			// Initialize interfaces
			foreach (var typeDef in allTypes) {
				if (typeDef.TypeDefinition.Interfaces == null)
					continue;
				foreach (var iface in typeDef.TypeDefinition.Interfaces) {
					var ifaceTypeDef = resolve(iface) ?? resolveOther(iface);
					if (ifaceTypeDef != null)
						typeDef.addInterface(ifaceTypeDef, iface);
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

			foreach (var typeDef in allTypes) {
				if (typeDef.baseType == null || !typeDef.baseType.typeDef.HasModule)
					baseTypes.Add(typeDef);
			}
		}

		class AssemblyKeyDictionary<T> where T : class {
			Dictionary<TypeReferenceSameVersionKey, T> dict = new Dictionary<TypeReferenceSameVersionKey, T>();
			Dictionary<TypeReferenceKey, List<TypeReference>> refs = new Dictionary<TypeReferenceKey, List<TypeReference>>();

			public T this[TypeReference type] {
				get {
					T value;
					if (tryGetValue(type, out value))
						return value;
					throw new KeyNotFoundException();
				}
				set {
					var key = new TypeReferenceSameVersionKey(type);
					dict[key] = value;

					if (value != null) {
						var key2 = new TypeReferenceKey(type);
						List<TypeReference> list;
						if (!refs.TryGetValue(key2, out list))
							refs[key2] = list = new List<TypeReference>();
						list.Add(type);
					}
				}
			}

			public bool tryGetValue(TypeReference type, out T value) {
				return dict.TryGetValue(new TypeReferenceSameVersionKey(type), out value);
			}

			public bool tryGetSimilarValue(TypeReference type, out T value) {
				var key2 = new TypeReferenceKey(type);
				List<TypeReference> list;
				if (!refs.TryGetValue(key2, out list)) {
					value = default(T);
					return false;
				}

				// Find a type whose version is >= type's version and closest to it.

				TypeReference foundType = null;
				var typeAsmName = MemberReferenceHelper.getAssemblyNameReference(type.Scope);
				AssemblyNameReference foundAsmName = null;
				foreach (var otherRef in list) {
					var key = new TypeReferenceSameVersionKey(otherRef);
					if (!dict.TryGetValue(key, out value))
						continue;

					if (typeAsmName == null) {
						foundType = otherRef;
						break;
					}

					var otherAsmName = MemberReferenceHelper.getAssemblyNameReference(otherRef.Scope);
					if (otherAsmName == null)
						continue;
					// Check pkt or we could return a type in eg. a SL assembly when it's not a SL app.
					if (!same(typeAsmName.PublicKeyToken, otherAsmName.PublicKeyToken))
						continue;
					if (typeAsmName.Version > otherAsmName.Version)
						continue;	// old version

					if (foundType == null) {
						foundAsmName = otherAsmName;
						foundType = otherRef;
						continue;
					}

					if (foundAsmName.Version <= otherAsmName.Version)
						continue;
					foundAsmName = otherAsmName;
					foundType = otherRef;
				}

				if (foundType != null) {
					value = dict[new TypeReferenceSameVersionKey(foundType)];
					return true;
				}

				value = default(T);
				return false;
			}

			bool same(byte[] a, byte[] b) {
				if (ReferenceEquals(a, b))
					return true;
				if (a == null || b == null)
					return false;
				if (a.Length != b.Length)
					return false;
				for (int i = 0; i < a.Length; i++) {
					if (a[i] != b[i])
						return false;
				}
				return true;
			}
		}

		AssemblyKeyDictionary<TypeDef> typeToTypeDefDict = new AssemblyKeyDictionary<TypeDef>();
		public TypeDef resolveOther(TypeReference type) {
			if (type == null)
				return null;
			type = type.GetElementType();

			TypeDef typeDef;
			if (typeToTypeDefDict.tryGetValue(type, out typeDef))
				return typeDef;

			var typeDefinition = deobfuscatorContext.resolve(type);
			if (typeDefinition == null) {
				typeToTypeDefDict.tryGetSimilarValue(type, out typeDef);
				typeToTypeDefDict[type] = typeDef;
				return typeDef;
			}

			if (typeToTypeDefDict.tryGetValue(typeDefinition, out typeDef)) {
				typeToTypeDefDict[type] = typeDef;
				return typeDef;
			}

			typeToTypeDefDict[type] = null;	// In case of a circular reference
			typeToTypeDefDict[typeDefinition] = null;

			typeDef = new TypeDef(typeDefinition, null, 0);
			typeDef.addMembers();
			foreach (var iface in typeDef.TypeDefinition.Interfaces) {
				var ifaceDef = resolveOther(iface);
				if (ifaceDef == null)
					continue;
				typeDef.addInterface(ifaceDef, iface);
			}
			var baseDef = resolveOther(typeDef.TypeDefinition.BaseType);
			if (baseDef != null)
				typeDef.addBaseType(baseDef, typeDef.TypeDefinition.BaseType);

			typeToTypeDefDict[type] = typeDef;
			if (type != typeDefinition)
				typeToTypeDefDict[typeDefinition] = typeDef;
			return typeDef;
		}

		public MethodNameGroups initializeVirtualMembers() {
			var groups = new MethodNameGroups();
			foreach (var typeDef in allTypes)
				typeDef.initializeVirtualMembers(groups, this);
			return groups;
		}

		public void onTypesRenamed() {
			foreach (var module in modules)
				module.onTypesRenamed();
		}

		public void cleanUp() {
			foreach (var module in DotNetUtils.typeCaches.invalidateAll())
				AssemblyResolver.Instance.removeModule(module);
		}

		// Returns null if it's a non-loaded module/assembly
		IEnumerable<Module> findModules(TypeReference type) {
			var scope = type.Scope;

			if (scope is AssemblyNameReference)
				return findModules((AssemblyNameReference)scope);

			if (scope is ModuleDefinition) {
				var modules = findModules((ModuleDefinition)scope);
				if (modules != null)
					return modules;
			}

			if (scope is ModuleReference) {
				var moduleReference = (ModuleReference)scope;
				if (moduleReference.Name == type.Module.Name) {
					var modules = findModules(type.Module);
					if (modules != null)
						return modules;
				}

				var asm = type.Module.Assembly;
				if (asm == null)
					return null;
				var moduleHash = assemblyHash.lookup(asm.ToString());
				if (moduleHash == null)
					return null;
				var module = moduleHash.lookup(moduleReference.Name);
				if (module == null)
					return null;
				return new List<Module> { module };
			}

			throw new ApplicationException(string.Format("scope is an unsupported type: {0}", scope.GetType()));
		}

		IEnumerable<Module> findModules(AssemblyNameReference assemblyRef) {
			var moduleHash = assemblyHash.lookup(assemblyRef.ToString());
			if (moduleHash != null)
				return moduleHash.Modules;
			return null;
		}

		IEnumerable<Module> findModules(ModuleDefinition moduleDefinition) {
			Module module;
			if (modulesDict.TryGetValue(moduleDefinition, out module))
				return new List<Module> { module };
			return null;
		}

		bool isAutoCreatedType(TypeReference typeReference) {
			return typeReference is ArrayType || typeReference is PointerType || typeReference is FunctionPointerType;
		}

		public TypeDef resolve(TypeReference typeReference) {
			var modules = findModules(typeReference);
			if (modules == null)
				return null;
			foreach (var module in modules) {
				var rv = module.resolve(typeReference);
				if (rv != null)
					return rv;
			}
			if (isAutoCreatedType(typeReference))
				return null;
			Log.e("Could not resolve TypeReference {0} ({1:X8}) (from {2} -> {3})",
						Utils.removeNewlines(typeReference),
						typeReference.MetadataToken.ToInt32(),
						typeReference.Module,
						typeReference.Scope);
			return null;
		}

		public MethodDef resolve(MethodReference methodReference) {
			if (methodReference.DeclaringType == null)
				return null;
			var modules = findModules(methodReference.DeclaringType);
			if (modules == null)
				return null;
			foreach (var module in modules) {
				var rv = module.resolve(methodReference);
				if (rv != null)
					return rv;
			}
			if (isAutoCreatedType(methodReference.DeclaringType))
				return null;
			Log.e("Could not resolve MethodReference {0} ({1:X8}) (from {2} -> {3})",
						Utils.removeNewlines(methodReference),
						methodReference.MetadataToken.ToInt32(),
						methodReference.DeclaringType.Module,
						methodReference.DeclaringType.Scope);
			return null;
		}

		public FieldDef resolve(FieldReference fieldReference) {
			if (fieldReference.DeclaringType == null)
				return null;
			var modules = findModules(fieldReference.DeclaringType);
			if (modules == null)
				return null;
			foreach (var module in modules) {
				var rv = module.resolve(fieldReference);
				if (rv != null)
					return rv;
			}
			if (isAutoCreatedType(fieldReference.DeclaringType))
				return null;
			Log.e("Could not resolve FieldReference {0} ({1:X8}) (from {2} -> {3})",
						Utils.removeNewlines(fieldReference),
						fieldReference.MetadataToken.ToInt32(),
						fieldReference.DeclaringType.Module,
						fieldReference.DeclaringType.Scope);
			return null;
		}
	}
}
