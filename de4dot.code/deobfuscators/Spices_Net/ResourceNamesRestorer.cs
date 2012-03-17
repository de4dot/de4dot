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

using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Spices_Net {
	class ResourceNamesRestorer {
		ModuleDefinition module;
		TypeDefinition resourceManagerType;
		TypeDefinition componentResourceManagerType;
		MethodDefinitionAndDeclaringTypeDict<MethodReference> resourceManagerCtors = new MethodDefinitionAndDeclaringTypeDict<MethodReference>();
		MethodDefinitionAndDeclaringTypeDict<MethodReference> componentManagerCtors = new MethodDefinitionAndDeclaringTypeDict<MethodReference>();

		public TypeDefinition ResourceManagerType {
			get { return resourceManagerType; }
		}

		public TypeDefinition ComponentResourceManagerType {
			get { return componentResourceManagerType; }
		}

		public ResourceNamesRestorer(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
			foreach (var type in module.Types) {
				if (isResourceType(type, "System.Resources.ResourceManager"))
					resourceManagerType = type;
				else if (isResourceType(type, "System.ComponentModel.ComponentResourceManager"))
					componentResourceManagerType = type;
			}

			initializeCtors(resourceManagerType, resourceManagerCtors);
			initializeCtors(componentResourceManagerType, componentManagerCtors);
		}

		static void initializeCtors(TypeDefinition manager, MethodDefinitionAndDeclaringTypeDict<MethodReference> ctors) {
			if (manager == null)
				return;

			foreach (var ctor in manager.Methods) {
				if (ctor.Name != ".ctor")
					continue;

				var newCtor = new MethodReference(ctor.Name, ctor.MethodReturnType.ReturnType, manager.BaseType);
				newCtor.HasThis = true;
				foreach (var param in ctor.Parameters)
					newCtor.Parameters.Add(new ParameterDefinition(param.ParameterType));
				ctors.add(ctor, newCtor);
			}
		}

		static bool isResourceType(TypeDefinition type, string baseTypeName) {
			if (type.BaseType == null || type.BaseType.FullName != baseTypeName)
				return false;
			if (type.HasProperties || type.HasEvents || type.HasFields)
				return false;
			if (type.Interfaces.Count > 0)
				return false;
			var method = DotNetUtils.getMethod(type, "GetResourceFileName");
			if (!DotNetUtils.isMethod(method, "System.String", "(System.Globalization.CultureInfo)"))
				return false;

			return true;
		}

		public void renameResources() {
			if (resourceManagerType == null && componentResourceManagerType == null)
				return;

			var numToResource = new Dictionary<uint, Resource>(module.Resources.Count);
			foreach (var resource in module.Resources) {
				var name = resource.Name;
				int index = name.LastIndexOf('.');
				string ext;
				if (index < 0)
					ext = name;
				else
					ext = name.Substring(index + 1);
				uint extNum;
				if (!uint.TryParse(ext, out extNum))
					continue;
				numToResource[extNum] = resource;
			}

			foreach (var type in module.GetTypes()) {
				rename(numToResource, "", type.FullName);
				rename(numToResource, "", type.FullName + ".g");
				rename(numToResource, type.Namespace, type.Name);
				rename(numToResource, type.Namespace, type.Name + ".g");
			}

			if (module.Assembly != null)
				rename(numToResource, "", module.Assembly.Name.Name + ".g");
		}

		static void rename(Dictionary<uint, Resource> numToResource, string ns, string name) {
			var resourceName = name + ".resources";
			uint hash = getResourceHash(resourceName);
			Resource resource;
			if (!numToResource.TryGetValue(hash, out resource))
				return;

			int index = resource.Name.LastIndexOf('.');
			string resourceNamespace, newName;
			if (index < 0) {
				resourceNamespace = "";
				newName = resourceName;
			}
			else {
				resourceNamespace = resource.Name.Substring(0, index);
				newName = resourceNamespace + "." + resourceName;
			}
			if (resourceNamespace != ns)
				return;

			Log.v("Restoring resource name: '{0}' => '{1}'",
								Utils.removeNewlines(resource.Name),
								Utils.removeNewlines(newName));
			resource.Name = newName;
			numToResource.Remove(hash);
		}

		static uint getResourceHash(string name) {
			uint hash = 0;
			foreach (var c in name)
				hash = ror(hash ^ c, 1);
			return hash;
		}

		static uint ror(uint val, int n) {
			return (val << (32 - n)) + (val >> n);
		}

		public void deobfuscate(Blocks blocks) {
			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode.Code != Code.Newobj)
						continue;
					var ctor = instr.Operand as MethodReference;
					if (ctor == null)
						continue;
					var newCtor = resourceManagerCtors.find(ctor);
					if (newCtor == null)
						newCtor = componentManagerCtors.find(ctor);
					if (newCtor == null)
						continue;
					instr.Operand = newCtor;
				}
			}
		}
	}
}
