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
using dnlib.IO;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class ResourceInfo {
		public EmbeddedResource resource;
		public string name;

		public ResourceInfo(EmbeddedResource resource, string name) {
			this.resource = resource;
			this.name = name;
		}

		public override string ToString() {
			return string.Format("{0} (rsrc: {1})", name, Utils.ToCsharpString(resource.Name));
		}
	}

	class AssemblyResolver {
		ModuleDefMD module;
		TypeDef assemblyResolverType;
		MethodDef assemblyResolverInitMethod;
		MethodDef assemblyResolverMethod;

		public bool Detected {
			get { return assemblyResolverType != null; }
		}

		public TypeDef Type {
			get { return assemblyResolverType; }
		}

		public MethodDef InitMethod {
			get { return assemblyResolverInitMethod; }
		}

		public AssemblyResolver(ModuleDefMD module) {
			this.module = module;
		}

		public AssemblyResolver(ModuleDefMD module, AssemblyResolver oldOne) {
			this.module = module;
			this.assemblyResolverType = Lookup(oldOne.assemblyResolverType, "Could not find assembly resolver type");
			this.assemblyResolverMethod = Lookup(oldOne.assemblyResolverMethod, "Could not find assembly resolver method");
			this.assemblyResolverInitMethod = Lookup(oldOne.assemblyResolverInitMethod, "Could not find assembly resolver init method");
		}

		T Lookup<T>(T def, string errorMessage) where T : class, ICodedToken {
			return DeobUtils.Lookup(module, def, errorMessage);
		}

		public void Find(ISimpleDeobfuscator simpleDeobfuscator) {
			if (CheckMethod(simpleDeobfuscator, module.EntryPoint))
				return;
			if (module.EntryPoint != null) {
				if (CheckMethod(simpleDeobfuscator, module.EntryPoint.DeclaringType.FindStaticConstructor()))
					return;
			}
		}

		bool CheckMethod(ISimpleDeobfuscator simpleDeobfuscator, MethodDef methodToCheck) {
			if (methodToCheck == null)
				return false;

			var resolverLocals = new string[] {
				"System.Byte[]",
				"System.Reflection.Assembly",
				"System.String",
				"System.IO.BinaryReader",
				"System.IO.Stream",
			};

			simpleDeobfuscator.Deobfuscate(methodToCheck);
			foreach (var method in DotNetUtils.GetCalledMethods(module, methodToCheck)) {
				var type = method.DeclaringType;
				if (!DotNetUtils.IsMethod(method, "System.Void", "()"))
					continue;
				if (!method.IsStatic)
					continue;

				if (type.Fields.Count != 2)
					continue;
				if (type.HasNestedTypes)
					continue;
				if (type.HasEvents || type.HasProperties)
					continue;
				if (!CheckFields(type.Fields))
					continue;

				var resolverMethod = FindAssemblyResolveMethod(type);
				if (resolverMethod == null)
					continue;

				var localTypes = new LocalTypes(resolverMethod);
				if (!localTypes.All(resolverLocals))
					continue;

				assemblyResolverType = type;
				assemblyResolverMethod = resolverMethod;
				assemblyResolverInitMethod = method;
				return true;
			}

			return false;
		}

		static bool CheckFields(IList<FieldDef> fields) {
			if (fields.Count != 2)
				return false;

			var fieldTypes = new FieldTypes(fields);
			return fieldTypes.Count("System.Boolean") == 1 &&
				(fieldTypes.Count("System.Collections.Hashtable") == 1 ||
				 fieldTypes.Count("System.Object") == 1);
		}

		static MethodDef FindAssemblyResolveMethod(TypeDef type) {
			foreach (var method in type.Methods) {
				if (DotNetUtils.IsMethod(method, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
					return method;
			}
			foreach (var method in type.Methods) {
				if (DotNetUtils.IsMethod(method, "System.Reflection.Assembly", "(System.Object,System.Object)"))
					return method;
			}
			return null;
		}

		public List<ResourceInfo> GetEmbeddedAssemblies(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			var infos = new List<ResourceInfo>();
			if (assemblyResolverMethod == null)
				return infos;
			simpleDeobfuscator.Deobfuscate(assemblyResolverMethod);
			simpleDeobfuscator.DecryptStrings(assemblyResolverMethod, deob);

			foreach (var resourcePrefix in DotNetUtils.GetCodeStrings(assemblyResolverMethod))
				infos.AddRange(GetResourceInfos(resourcePrefix));

			return infos;
		}

		List<ResourceInfo> GetResourceInfos(string prefix) {
			var infos = new List<ResourceInfo>();

			foreach (var resource in FindResources(prefix))
				infos.Add(new ResourceInfo(resource, GetAssemblyName(resource)));

			return infos;
		}

		List<EmbeddedResource> FindResources(string prefix) {
			var result = new List<EmbeddedResource>();

			if (string.IsNullOrEmpty(prefix))
				return result;

			foreach (var rsrc in module.Resources) {
				var resource = rsrc as EmbeddedResource;
				if (resource == null)
					continue;
				if (!Utils.StartsWith(resource.Name.String, prefix, StringComparison.Ordinal))
					continue;

				result.Add(resource);
			}

			return result;
		}

		static int unknownNameCounter = 0;
		static string GetAssemblyName(EmbeddedResource resource) {
			try {
				var resourceModule = ModuleDefMD.Load(resource.Data.ReadAllBytes());
				return resourceModule.Assembly.FullName;
			}
			catch {
				return string.Format("unknown_name_{0}", unknownNameCounter++);
			}
		}
	}
}
