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

using System.Collections.Generic;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Spices_Net {
	class ResourceNamesRestorer {
		ModuleDefMD module;
		TypeDef resourceManagerType;
		TypeDef componentResourceManagerType;
		MethodDefAndDeclaringTypeDict<IMethod> resourceManagerCtors = new MethodDefAndDeclaringTypeDict<IMethod>();
		MethodDefAndDeclaringTypeDict<IMethod> componentManagerCtors = new MethodDefAndDeclaringTypeDict<IMethod>();

		public TypeDef ResourceManagerType {
			get { return resourceManagerType; }
		}

		public TypeDef ComponentResourceManagerType {
			get { return componentResourceManagerType; }
		}

		public ResourceNamesRestorer(ModuleDefMD module) {
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

		void initializeCtors(TypeDef manager, MethodDefAndDeclaringTypeDict<IMethod> ctors) {
			if (manager == null)
				return;

			foreach (var ctor in manager.Methods) {
				if (ctor.Name != ".ctor")
					continue;

				var newCtor = new MemberRefUser(module, ctor.Name, ctor.MethodSig.Clone(), manager.BaseType);
				module.UpdateRowId(newCtor);
				ctors.add(ctor, newCtor);
			}
		}

		static bool isResourceType(TypeDef type, string baseTypeName) {
			if (type.BaseType == null || type.BaseType.FullName != baseTypeName)
				return false;
			if (type.HasProperties || type.HasEvents || type.HasFields)
				return false;
			if (type.Interfaces.Count > 0)
				return false;
			var method = type.FindMethod("GetResourceFileName");
			if (!DotNetUtils.isMethod(method, "System.String", "(System.Globalization.CultureInfo)"))
				return false;

			return true;
		}

		public void renameResources() {
			if (resourceManagerType == null && componentResourceManagerType == null)
				return;

			var numToResource = new Dictionary<uint, Resource>(module.Resources.Count);
			foreach (var resource in module.Resources) {
				var name = resource.Name.String;
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
				rename(numToResource, type.Namespace.String, type.Name.String);
				rename(numToResource, type.Namespace.String, type.Name.String + ".g");
			}

			if (module.Assembly != null)
				rename(numToResource, "", module.Assembly.Name.String + ".g");
		}

		static void rename(Dictionary<uint, Resource> numToResource, string ns, string name) {
			var resourceName = name + ".resources";
			uint hash = getResourceHash(resourceName);
			Resource resource;
			if (!numToResource.TryGetValue(hash, out resource))
				return;

			int index = resource.Name.String.LastIndexOf('.');
			string resourceNamespace, newName;
			if (index < 0) {
				resourceNamespace = "";
				newName = resourceName;
			}
			else {
				resourceNamespace = resource.Name.String.Substring(0, index);
				newName = resourceNamespace + "." + resourceName;
			}
			if (resourceNamespace != ns)
				return;

			Logger.v("Restoring resource name: '{0}' => '{1}'",
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
			if (resourceManagerType == null && componentResourceManagerType == null)
				return;

			foreach (var block in blocks.MethodBlocks.getAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode.Code != Code.Newobj)
						continue;
					var ctor = instr.Operand as IMethod;
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
