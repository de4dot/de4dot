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
using System.IO;
using Mono.Cecil;
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
			return string.Format("{0} (rsrc: {1})", name, Utils.toCsharpString(resource.Name));
		}
	}

	class AssemblyResolver {
		ModuleDefinition module;
		TypeDefinition assemblyResolverType;
		MethodDefinition assemblyResolverInitMethod;
		MethodDefinition assemblyResolverMethod;

		public bool Detected {
			get { return assemblyResolverType != null; }
		}

		public TypeDefinition Type {
			get { return assemblyResolverType; }
		}

		public MethodDefinition InitMethod {
			get { return assemblyResolverInitMethod; }
		}

		public AssemblyResolver(ModuleDefinition module) {
			this.module = module;
		}

		public AssemblyResolver(ModuleDefinition module, AssemblyResolver oldOne) {
			this.module = module;
			this.assemblyResolverType = lookup(oldOne.assemblyResolverType, "Could not find assembly resolver type");
			this.assemblyResolverMethod = lookup(oldOne.assemblyResolverMethod, "Could not find assembly resolver method");
			this.assemblyResolverInitMethod = lookup(oldOne.assemblyResolverInitMethod, "Could not find assembly resolver init method");
		}

		T lookup<T>(T def, string errorMessage) where T : MemberReference {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public void find(ISimpleDeobfuscator simpleDeobfuscator) {
			if (checkMethod(simpleDeobfuscator, module.EntryPoint))
				return;
			if (module.EntryPoint != null) {
				if (checkMethod(simpleDeobfuscator, DotNetUtils.getMethod(module.EntryPoint.DeclaringType, ".cctor")))
					return;
			}
		}

		bool checkMethod(ISimpleDeobfuscator simpleDeobfuscator, MethodDefinition methodToCheck) {
			if (methodToCheck == null)
				return false;

			var resolverLocals = new string[] {
				"System.Byte[]",
				"System.Reflection.Assembly",
				"System.Security.Cryptography.MD5",
				"System.String",
				"System.IO.BinaryReader",
				"System.IO.Stream",
			};

			simpleDeobfuscator.deobfuscate(methodToCheck);
			foreach (var method in DotNetUtils.getCalledMethods(module, methodToCheck)) {
				var type = method.DeclaringType;
				if (!DotNetUtils.isMethod(method, "System.Void", "()"))
					continue;
				if (!method.IsStatic)
					continue;

				if (type.Fields.Count != 2)
					continue;
				if (type.HasNestedTypes)
					continue;
				if (type.HasEvents || type.HasProperties)
					continue;
				if (!checkFields(type.Fields))
					continue;

				var resolverMethod = findAssemblyResolveMethod(type);
				if (resolverMethod == null)
					continue;

				var localTypes = new LocalTypes(resolverMethod);
				if (!localTypes.all(resolverLocals))
					continue;

				assemblyResolverType = type;
				assemblyResolverMethod = resolverMethod;
				assemblyResolverInitMethod = method;
				return true;
			}

			return false;
		}

		static bool checkFields(IList<FieldDefinition> fields) {
			if (fields.Count != 2)
				return false;

			var fieldTypes = new FieldTypes(fields);
			return fieldTypes.count("System.Boolean") == 1 &&
				(fieldTypes.count("System.Collections.Hashtable") == 1 ||
				 fieldTypes.count("System.Object") == 1);
		}

		static MethodDefinition findAssemblyResolveMethod(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (DotNetUtils.isMethod(method, "System.Reflection.Assembly", "(System.Object,System.ResolveEventArgs)"))
					return method;
			}
			foreach (var method in type.Methods) {
				if (DotNetUtils.isMethod(method, "System.Reflection.Assembly", "(System.Object,System.Object)"))
					return method;
			}
			return null;
		}

		public List<ResourceInfo> getEmbeddedAssemblies(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			var infos = new List<ResourceInfo>();
			if (assemblyResolverMethod == null)
				return infos;
			simpleDeobfuscator.deobfuscate(assemblyResolverMethod);
			simpleDeobfuscator.decryptStrings(assemblyResolverMethod, deob);

			foreach (var resourcePrefix in DotNetUtils.getCodeStrings(assemblyResolverMethod))
				infos.AddRange(getResourceInfos(resourcePrefix));

			return infos;
		}

		List<ResourceInfo> getResourceInfos(string prefix) {
			var infos = new List<ResourceInfo>();

			foreach (var resource in findResources(prefix))
				infos.Add(new ResourceInfo(resource, getAssemblyName(resource)));

			return infos;
		}

		List<EmbeddedResource> findResources(string prefix) {
			var result = new List<EmbeddedResource>();

			if (string.IsNullOrEmpty(prefix))
				return result;

			foreach (var rsrc in module.Resources) {
				var resource = rsrc as EmbeddedResource;
				if (resource == null)
					continue;
				if (!Utils.StartsWith(resource.Name, prefix, StringComparison.Ordinal))
					continue;

				result.Add(resource);
			}

			return result;
		}

		static int unknownNameCounter = 0;
		static string getAssemblyName(EmbeddedResource resource) {
			try {
				var resourceModule = ModuleDefinition.ReadModule(new MemoryStream(resource.GetResourceData()));
				return resourceModule.Assembly.Name.FullName;
			}
			catch {
				return string.Format("unknown_name_{0}", unknownNameCounter++);
			}
		}
	}
}
