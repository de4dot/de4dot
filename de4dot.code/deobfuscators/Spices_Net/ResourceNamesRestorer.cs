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
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Spices_Net {
	class ResourceNamesRestorer {
		ModuleDefMD module;
		TypeDef resourceManagerType;
		TypeDef componentResourceManagerType;
		MethodDefAndDeclaringTypeDict<IMethod> resourceManagerCtors = new MethodDefAndDeclaringTypeDict<IMethod>();
		MethodDefAndDeclaringTypeDict<IMethod> componentManagerCtors = new MethodDefAndDeclaringTypeDict<IMethod>();
		Dictionary<TypeDef, bool> callsResourceManager = new Dictionary<TypeDef, bool>();

		public TypeDef ResourceManagerType {
			get { return resourceManagerType; }
		}

		public TypeDef ComponentResourceManagerType {
			get { return componentResourceManagerType; }
		}

		public ResourceNamesRestorer(ModuleDefMD module) {
			this.module = module;
		}

		public void Find() {
			foreach (var type in module.Types) {
				if (IsResourceType(type, "System.Resources.ResourceManager"))
					resourceManagerType = type;
				else if (IsResourceType(type, "System.ComponentModel.ComponentResourceManager"))
					componentResourceManagerType = type;
			}

			InitializeCtors(resourceManagerType, resourceManagerCtors);
			InitializeCtors(componentResourceManagerType, componentManagerCtors);
		}

		void InitializeCtors(TypeDef manager, MethodDefAndDeclaringTypeDict<IMethod> ctors) {
			if (manager == null)
				return;

			foreach (var ctor in manager.Methods) {
				if (ctor.Name != ".ctor")
					continue;

				var newCtor = new MemberRefUser(module, ctor.Name, ctor.MethodSig.Clone(), manager.BaseType);
				module.UpdateRowId(newCtor);
				ctors.Add(ctor, newCtor);
			}
		}

		static bool IsResourceType(TypeDef type, string baseTypeName) {
			if (type.BaseType == null || type.BaseType.FullName != baseTypeName)
				return false;
			if (type.HasProperties || type.HasEvents || type.HasFields)
				return false;
			if (type.Interfaces.Count > 0)
				return false;
			var method = type.FindMethod("GetResourceFileName");
			if (!DotNetUtils.IsMethod(method, "System.String", "(System.Globalization.CultureInfo)"))
				return false;

			return true;
		}

		class ResourceDictionary {
			struct Key {
				public readonly uint hash;
				public readonly string ns;
				public Key(uint hash, string ns) {
					this.hash = hash;
					this.ns = ns;
				}

				public override int GetHashCode() {
					return (int)(hash ^ ns.GetHashCode());
				}

				public override bool Equals(object obj) {
					if (!(obj is Key))
						return false;
					var other = (Key)obj;
					return hash == other.hash &&
						ns == other.ns;
				}

				public override string ToString() {
					if (ns == string.Empty)
						return string.Format("{0}", hash);
					return string.Format("{0}.{1}", ns, hash);
				}
			}
			Dictionary<Key, Resource> resources = new Dictionary<Key, Resource>();

			public int Count {
				get { return resources.Count; }
			}

			public bool Add(Resource resource) {
				var name = resource.Name.String;
				int index = name.LastIndexOf('.');
				string ext;
				if (index < 0)
					ext = name;
				else
					ext = name.Substring(index + 1);
				uint extNum;
				if (!uint.TryParse(ext, out extNum))
					return false;
				var ns = index < 0 ? string.Empty : name.Substring(0, index);

				resources.Add(new Key(extNum, ns), resource);
				return true;
			}

			public Resource GetAndRemove(uint hash, string ns) {
				var key = new Key(hash, ns);
				Resource resource;
				if (resources.TryGetValue(key, out resource))
					resources.Remove(key);
				return resource;
			}
		}

		public void RenameResources() {
			if (resourceManagerType == null && componentResourceManagerType == null)
				return;

			var rsrcDict = new ResourceDictionary();
			foreach (var resource in module.Resources)
				rsrcDict.Add(resource);

			if (module.Assembly != null)
				Rename(rsrcDict, "", module.Assembly.Name + ".g");

			foreach (var type in callsResourceManager.Keys)
				Rename(rsrcDict, type);

			if (rsrcDict.Count != 0) {
				foreach (var type in module.GetTypes()) {
					if (rsrcDict.Count == 0)
						break;
					if (!IsWinFormType(type))
						continue;
					Rename(rsrcDict, type);
				}
			}

			if (rsrcDict.Count != 0) {
				foreach (var type in module.GetTypes()) {
					if (rsrcDict.Count == 0)
						break;
					Rename(rsrcDict, type);
				}
			}

			if (rsrcDict.Count != 0)
				Logger.e("Couldn't restore all renamed resource names");
		}

		static bool IsWinFormType(TypeDef type) {
			for (int i = 0; i < 100; i++) {
				var baseType = type.BaseType;
				if (baseType == null)
					break;
				if (baseType.FullName == "System.Object" ||
					baseType.FullName == "System.ValueType")
					return false;
				// Speed up common cases
				if (baseType.FullName == "System.Windows.Forms.Control" ||
					baseType.FullName == "System.Windows.Forms.Form" ||
					baseType.FullName == "System.Windows.Forms.UserControl")
					return true;
				var resolvedBaseType = baseType.ResolveTypeDef();
				if (resolvedBaseType == null)
					break;
				type = resolvedBaseType;
			}
			return false;
		}

		static bool Rename(ResourceDictionary rsrcDict, TypeDef type) {
			if (!IsWinFormType(type) && Rename(rsrcDict, "", type.FullName))
				return true;
			return Rename(rsrcDict, type.Namespace, type.Name);
		}

		static bool Rename(ResourceDictionary rsrcDict, string ns, string name) {
			var resourceName = name + ".resources";
			uint hash = GetResourceHash(resourceName);
			var resource = rsrcDict.GetAndRemove(hash, ns);
			if (resource == null)
				return false;

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
				throw new ApplicationException("Invalid resource namespace");

			Logger.v("Restoring resource name: '{0}' => '{1}'",
								Utils.RemoveNewlines(resource.Name),
								Utils.RemoveNewlines(newName));
			resource.Name = newName;
			return true;
		}

		static uint GetResourceHash(string name) {
			uint hash = 0;
			foreach (var c in name)
				hash = Ror(hash ^ c, 1);
			return hash;
		}

		static uint Ror(uint val, int n) {
			return (val << (32 - n)) + (val >> n);
		}

		public void Deobfuscate(Blocks blocks) {
			if (resourceManagerType == null && componentResourceManagerType == null)
				return;

			foreach (var block in blocks.MethodBlocks.GetAllBlocks()) {
				var instrs = block.Instructions;
				for (int i = 0; i < instrs.Count; i++) {
					var instr = instrs[i];
					if (instr.OpCode.Code != Code.Newobj)
						continue;
					var ctor = instr.Operand as IMethod;
					if (ctor == null)
						continue;
					var newCtor = resourceManagerCtors.Find(ctor);
					if (newCtor == null)
						newCtor = componentManagerCtors.Find(ctor);
					if (newCtor == null)
						continue;
					instr.Operand = newCtor;
					callsResourceManager[blocks.Method.DeclaringType] = true;
				}
			}
		}
	}
}
