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

namespace de4dot.code.renamer.asmmodules {
	public class Modules : IResolver {
		bool initializeCalled = false;
		IDeobfuscatorContext deobfuscatorContext;
		List<Module> modules = new List<Module>();
		Dictionary<ModuleDef, Module> modulesDict = new Dictionary<ModuleDef, Module>();
		AssemblyHash assemblyHash = new AssemblyHash();

		List<MTypeDef> allTypes = new List<MTypeDef>();
		List<MTypeDef> baseTypes = new List<MTypeDef>();
		List<MTypeDef> nonNestedTypes;

		public IList<Module> TheModules => modules;
		public IEnumerable<MTypeDef> AllTypes => allTypes;
		public IEnumerable<MTypeDef> BaseTypes => baseTypes;
		public List<MTypeDef> NonNestedTypes => nonNestedTypes;

		class AssemblyHash {
			IDictionary<string, ModuleHash> assemblyHash = new Dictionary<string, ModuleHash>(StringComparer.Ordinal);

			public void Add(Module module) {
				var key = GetModuleKey(module);
				if (!assemblyHash.TryGetValue(key, out var moduleHash))
					assemblyHash[key] = moduleHash = new ModuleHash();
				moduleHash.Add(module);
			}

			static string GetModuleKey(Module module) {
				if (module.ModuleDefMD.Assembly != null)
					return GetAssemblyName(module.ModuleDefMD.Assembly);
				return Utils.GetBaseName(module.ModuleDefMD.Location);
			}

			public ModuleHash Lookup(IAssembly asm) {
				if (assemblyHash.TryGetValue(GetAssemblyName(asm), out var moduleHash))
					return moduleHash;
				return null;
			}

			static string GetAssemblyName(IAssembly asm) {
				if (asm == null)
					return string.Empty;
				if (PublicKeyBase.IsNullOrEmpty2(asm.PublicKeyOrToken))
					return asm.Name;
				return asm.FullName;
			}
		}

		class ModuleHash {
			ModulesDict modulesDict = new ModulesDict();
			Module mainModule = null;

			public void Add(Module module) {
				var asm = module.ModuleDefMD.Assembly;
				if (asm != null && ReferenceEquals(asm.ManifestModule, module.ModuleDefMD)) {
					if (mainModule != null) {
						throw new UserException(
							"Two modules in the same assembly are main modules.\n" +
							"Is one 32-bit and the other 64-bit?\n" +
							$"  Module1: \"{module.ModuleDefMD.Location}\"" +
							$"  Module2: \"{mainModule.ModuleDefMD.Location}\"");
					}
					mainModule = module;
				}

				modulesDict.Add(module);
			}

			public Module Lookup(string moduleName) => modulesDict.Lookup(moduleName);
			public IEnumerable<Module> Modules => modulesDict.Modules;
		}

		class ModulesDict {
			IDictionary<string, Module> modulesDict = new Dictionary<string, Module>(StringComparer.Ordinal);

			public void Add(Module module) {
				var moduleName = module.ModuleDefMD.Name.String;
				if (Lookup(moduleName) != null)
					throw new ApplicationException($"Module \"{moduleName}\" was found twice");
				modulesDict[moduleName] = module;
			}

			public Module Lookup(string moduleName) {
				if (modulesDict.TryGetValue(moduleName, out var module))
					return module;
				return null;
			}

			public IEnumerable<Module> Modules => modulesDict.Values;
		}

		public bool Empty => modules.Count == 0;
		public Modules(IDeobfuscatorContext deobfuscatorContext) => this.deobfuscatorContext = deobfuscatorContext;

		public void Add(Module module) {
			if (initializeCalled)
				throw new ApplicationException("initialize() has been called");
			if (modulesDict.TryGetValue(module.ModuleDefMD, out var otherModule))
				return;
			modulesDict[module.ModuleDefMD] = module;
			modules.Add(module);
			assemblyHash.Add(module);
		}

		public void Initialize() {
			initializeCalled = true;
			FindAllMemberRefs();
			InitAllTypes();
			ResolveAllRefs();
		}

		void FindAllMemberRefs() {
			Logger.v("Finding all MemberRefs");
			int index = 0;
			foreach (var module in modules) {
				if (modules.Count > 1)
					Logger.v("Finding all MemberRefs ({0})", module.Filename);
				Logger.Instance.Indent();
				module.FindAllMemberRefs(ref index);
				Logger.Instance.DeIndent();
			}
		}

		void ResolveAllRefs() {
			Logger.v("Resolving references");
			foreach (var module in modules) {
				if (modules.Count > 1)
					Logger.v("Resolving references ({0})", module.Filename);
				Logger.Instance.Indent();
				module.ResolveAllRefs(this);
				Logger.Instance.DeIndent();
			}
		}

		void InitAllTypes() {
			foreach (var module in modules)
				allTypes.AddRange(module.GetAllTypes());

			var typeToTypeDef = new Dictionary<TypeDef, MTypeDef>(allTypes.Count);
			foreach (var typeDef in allTypes)
				typeToTypeDef[typeDef.TypeDef] = typeDef;

			// Initialize Owner
			foreach (var typeDef in allTypes) {
				if (typeDef.TypeDef.DeclaringType != null)
					typeDef.Owner = typeToTypeDef[typeDef.TypeDef.DeclaringType];
			}

			// Initialize baseType and derivedTypes
			foreach (var typeDef in allTypes) {
				var baseType = typeDef.TypeDef.BaseType;
				if (baseType == null)
					continue;
				var baseTypeDef = ResolveType(baseType) ?? ResolveOther(baseType);
				if (baseTypeDef != null) {
					typeDef.AddBaseType(baseTypeDef, baseType);
					baseTypeDef.derivedTypes.Add(typeDef);
				}
			}

			// Initialize interfaces
			foreach (var typeDef in allTypes) {
				foreach (var iface in typeDef.TypeDef.Interfaces) {
					var ifaceTypeDef = ResolveType(iface.Interface) ?? ResolveOther(iface.Interface);
					if (ifaceTypeDef != null)
						typeDef.AddInterface(ifaceTypeDef, iface.Interface);
				}
			}

			// Find all non-nested types
			var allTypesDict = new Dictionary<MTypeDef, bool>();
			foreach (var t in allTypes)
				allTypesDict[t] = true;
			foreach (var t in allTypes) {
				foreach (var t2 in t.NestedTypes)
					allTypesDict.Remove(t2);
			}
			nonNestedTypes = new List<MTypeDef>(allTypesDict.Keys);

			foreach (var typeDef in allTypes) {
				if (typeDef.baseType == null || !typeDef.baseType.typeDef.HasModule)
					baseTypes.Add(typeDef);
			}
		}

		class AssemblyKeyDictionary<T> where T : class {
			Dictionary<ITypeDefOrRef, T> dict = new Dictionary<ITypeDefOrRef, T>(new TypeEqualityComparer(SigComparerOptions.CompareAssemblyVersion));
			Dictionary<ITypeDefOrRef, List<ITypeDefOrRef>> refs = new Dictionary<ITypeDefOrRef, List<ITypeDefOrRef>>(TypeEqualityComparer.Instance);

			public T this[ITypeDefOrRef type] {
				get {
					if (TryGetValue(type, out var value))
						return value;
					throw new KeyNotFoundException();
				}
				set {
					dict[type] = value;

					if (value != null) {
						if (!refs.TryGetValue(type, out var list))
							refs[type] = list = new List<ITypeDefOrRef>();
						list.Add(type);
					}
				}
			}

			public bool TryGetValue(ITypeDefOrRef type, out T value) => dict.TryGetValue(type, out value);

			public bool TryGetSimilarValue(ITypeDefOrRef type, out T value) {
				if (!refs.TryGetValue(type, out var list)) {
					value = default;
					return false;
				}

				// Find a type whose version is >= type's version and closest to it.

				ITypeDefOrRef foundType = null;
				var typeAsmName = type.DefinitionAssembly;
				IAssembly foundAsmName = null;
				foreach (var otherRef in list) {
					if (!dict.TryGetValue(otherRef, out value))
						continue;

					if (typeAsmName == null) {
						foundType = otherRef;
						break;
					}

					var otherAsmName = otherRef.DefinitionAssembly;
					if (otherAsmName == null)
						continue;
					// Check pkt or we could return a type in eg. a SL assembly when it's not a SL app.
					if (!PublicKeyBase.TokenEquals(typeAsmName.PublicKeyOrToken, otherAsmName.PublicKeyOrToken))
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
					value = dict[foundType];
					return true;
				}

				value = default;
				return false;
			}
		}

		AssemblyKeyDictionary<MTypeDef> typeToTypeDefDict = new AssemblyKeyDictionary<MTypeDef>();
		public MTypeDef ResolveOther(ITypeDefOrRef type) {
			if (type == null)
				return null;
			type = type.ScopeType;
			if (type == null)
				return null;

			if (typeToTypeDefDict.TryGetValue(type, out var typeDef))
				return typeDef;

			var typeDef2 = deobfuscatorContext.ResolveType(type);
			if (typeDef2 == null) {
				typeToTypeDefDict.TryGetSimilarValue(type, out typeDef);
				typeToTypeDefDict[type] = typeDef;
				return typeDef;
			}

			if (typeToTypeDefDict.TryGetValue(typeDef2, out typeDef)) {
				typeToTypeDefDict[type] = typeDef;
				return typeDef;
			}

			typeToTypeDefDict[type] = null;	// In case of a circular reference
			typeToTypeDefDict[typeDef2] = null;

			typeDef = new MTypeDef(typeDef2, null, 0);
			typeDef.AddMembers();
			foreach (var iface in typeDef.TypeDef.Interfaces) {
				var ifaceDef = ResolveOther(iface.Interface);
				if (ifaceDef == null)
					continue;
				typeDef.AddInterface(ifaceDef, iface.Interface);
			}
			var baseDef = ResolveOther(typeDef.TypeDef.BaseType);
			if (baseDef != null)
				typeDef.AddBaseType(baseDef, typeDef.TypeDef.BaseType);

			typeToTypeDefDict[type] = typeDef;
			if (type != typeDef2)
				typeToTypeDefDict[typeDef2] = typeDef;
			return typeDef;
		}

		public MethodNameGroups InitializeVirtualMembers() {
			var groups = new MethodNameGroups();
			foreach (var typeDef in allTypes)
				typeDef.InitializeVirtualMembers(groups, this);
			return groups;
		}

		public void OnTypesRenamed() {
			foreach (var module in modules)
				module.OnTypesRenamed();
		}

		public void CleanUp() {
#if PORT
			foreach (var module in DotNetUtils.typeCaches.invalidateAll())
				AssemblyResolver.Instance.removeModule(module);
#endif
		}

		// Returns null if it's a non-loaded module/assembly
		IEnumerable<Module> FindModules(ITypeDefOrRef type) {
			if (type == null)
				return null;
			var scope = type.Scope;
			if (scope == null)
				return null;

			var scopeType = scope.ScopeType;
			if (scopeType == ScopeType.AssemblyRef)
				return FindModules((AssemblyRef)scope);

			if (scopeType == ScopeType.ModuleDef) {
				var modules = FindModules((ModuleDef)scope);
				if (modules != null)
					return modules;
			}

			if (scopeType == ScopeType.ModuleRef) {
				var moduleRef = (ModuleRef)scope;
				if (moduleRef.Name == type.Module.Name) {
					var modules = FindModules(type.Module);
					if (modules != null)
						return modules;
				}
			}

			if (scopeType == ScopeType.ModuleRef || scopeType == ScopeType.ModuleDef) {
				var asm = type.Module.Assembly;
				if (asm == null)
					return null;
				var moduleHash = assemblyHash.Lookup(asm);
				if (moduleHash == null)
					return null;
				var module = moduleHash.Lookup(scope.ScopeName);
				if (module == null)
					return null;
				return new List<Module> { module };
			}

			throw new ApplicationException($"scope is an unsupported type: {scope.GetType()}");
		}

		IEnumerable<Module> FindModules(AssemblyRef assemblyRef) {
			var moduleHash = assemblyHash.Lookup(assemblyRef);
			if (moduleHash != null)
				return moduleHash.Modules;
			return null;
		}

		IEnumerable<Module> FindModules(ModuleDef moduleDef) {
			if (modulesDict.TryGetValue(moduleDef, out var module))
				return new List<Module> { module };
			return null;
		}

		bool IsAutoCreatedType(ITypeDefOrRef typeRef) {
			var ts = typeRef as TypeSpec;
			if (ts == null)
				return false;
			var sig = ts.TypeSig;
			if (sig == null)
				return false;
			return sig.IsSZArray || sig.IsArray || sig.IsPointer;
		}

		public MTypeDef ResolveType(ITypeDefOrRef typeRef) {
			var modules = FindModules(typeRef);
			if (modules == null)
				return null;
			foreach (var module in modules) {
				var rv = module.ResolveType(typeRef);
				if (rv != null)
					return rv;
			}
			if (IsAutoCreatedType(typeRef))
				return null;
			Logger.e("Could not resolve TypeRef {0} ({1:X8}) (from {2} -> {3})",
						Utils.RemoveNewlines(typeRef),
						typeRef.MDToken.ToInt32(),
						typeRef.Module,
						typeRef.Scope);
			return null;
		}

		public MMethodDef ResolveMethod(IMethodDefOrRef methodRef) {
			if (methodRef.DeclaringType == null)
				return null;
			var modules = FindModules(methodRef.DeclaringType);
			if (modules == null)
				return null;
			foreach (var module in modules) {
				var rv = module.ResolveMethod(methodRef);
				if (rv != null)
					return rv;
			}
			if (IsAutoCreatedType(methodRef.DeclaringType))
				return null;
			Logger.e("Could not resolve MethodRef {0} ({1:X8}) (from {2} -> {3})",
						Utils.RemoveNewlines(methodRef),
						methodRef.MDToken.ToInt32(),
						methodRef.DeclaringType.Module,
						methodRef.DeclaringType.Scope);
			return null;
		}

		public MFieldDef ResolveField(MemberRef fieldRef) {
			if (fieldRef.DeclaringType == null)
				return null;
			var modules = FindModules(fieldRef.DeclaringType);
			if (modules == null)
				return null;
			foreach (var module in modules) {
				var rv = module.ResolveField(fieldRef);
				if (rv != null)
					return rv;
			}
			if (IsAutoCreatedType(fieldRef.DeclaringType))
				return null;
			Logger.e("Could not resolve FieldRef {0} ({1:X8}) (from {2} -> {3})",
						Utils.RemoveNewlines(fieldRef),
						fieldRef.MDToken.ToInt32(),
						fieldRef.DeclaringType.Module,
						fieldRef.DeclaringType.Scope);
			return null;
		}
	}
}
