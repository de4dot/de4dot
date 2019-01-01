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

using System.Collections.Generic;
using System.Xml;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeVeil {
	class AssemblyResolver {
		ModuleDefMD module;
		EmbeddedResource bundleData;
		EmbeddedResource bundleXmlFile;
		TypeDef bundleType;
		TypeDef assemblyManagerType;
		TypeDef bundleStreamProviderIFace;
		TypeDef xmlParserType;
		TypeDef bundledAssemblyType;
		TypeDef streamProviderType;
		List<AssemblyInfo> infos = new List<AssemblyInfo>();

		public class AssemblyInfo {
			public string fullName;
			public string simpleName;
			public string extension;
			public byte[] data;

			public AssemblyInfo(string fullName, string extension, byte[] data) {
				this.fullName = fullName;
				simpleName = Utils.GetAssemblySimpleName(fullName);
				this.extension = extension;
				this.data = data;
			}

			public override string ToString() => fullName;
		}

		public bool CanRemoveTypes =>
			bundleType != null &&
			assemblyManagerType != null &&
			bundleStreamProviderIFace != null &&
			xmlParserType != null &&
			bundledAssemblyType != null &&
			streamProviderType != null;

		public IEnumerable<TypeDef> BundleTypes {
			get {
				var list = new List<TypeDef>();
				if (!CanRemoveTypes)
					return list;

				list.Add(bundleType);
				list.Add(assemblyManagerType);
				list.Add(bundleStreamProviderIFace);
				list.Add(xmlParserType);
				list.Add(bundledAssemblyType);
				list.Add(streamProviderType);

				return list;
			}
		}

		public IEnumerable<AssemblyInfo> AssemblyInfos => infos;
		public EmbeddedResource BundleDataResource => bundleData;
		public EmbeddedResource BundleXmlFileResource => bundleXmlFile;
		public AssemblyResolver(ModuleDefMD module) => this.module = module;

		public void Initialize() {
			if (!FindTypeAndResources())
				return;

			FindEmbeddedAssemblies();
		}

		bool FindTypeAndResources() {
			var bundleDataTmp = DotNetUtils.GetResource(module, ".bundle.dat") as EmbeddedResource;
			var bundleXmlFileTmp = DotNetUtils.GetResource(module, ".bundle.manifest") as EmbeddedResource;
			if (bundleDataTmp == null || bundleXmlFileTmp == null)
				return false;

			var bundleTypeTmp = FindBundleType();
			if (bundleTypeTmp == null)
				return false;

			bundleData = bundleDataTmp;
			bundleXmlFile = bundleXmlFileTmp;
			bundleType = bundleTypeTmp;
			FindOtherTypes();
			return true;
		}

		void FindEmbeddedAssemblies() {
			var data = bundleData.CreateReader().ToArray();

			var doc = new XmlDocument();
			doc.Load(XmlReader.Create(bundleXmlFile.CreateReader().AsStream()));
			var manifest = doc.DocumentElement;
			if (manifest.Name.ToLowerInvariant() != "manifest") {
				Logger.w("Could not find Manifest element");
				return;
			}
			foreach (var tmp in manifest.ChildNodes) {
				var assemblyElem = tmp as XmlElement;
				if (assemblyElem == null)
					continue;

				if (assemblyElem.Name.ToLowerInvariant() != "assembly") {
					Logger.w("Unknown element: {0}", assemblyElem.Name);
					continue;
				}

				int offset = GetAttributeValueInt32(assemblyElem, "offset");
				if (offset < 0) {
					Logger.w("Could not find offset attribute");
					continue;
				}

				var assemblyData = DeobUtils.Inflate(data, offset, data.Length - offset, true);
				var mod = ModuleDefMD.Load(assemblyData);
				infos.Add(new AssemblyInfo(mod.Assembly.FullName, DeobUtils.GetExtension(mod.Kind), assemblyData));
			}
		}

		static int GetAttributeValueInt32(XmlElement elem, string attrName) {
			var str = elem.GetAttribute(attrName);
			if (string.IsNullOrEmpty(str))
				return -1;

			if (!int.TryParse(str, out int value))
				return -1;

			return value;
		}

		TypeDef FindBundleType() {
			foreach (var type in module.Types) {
				if (type.Namespace != "")
					continue;
				if (type.Fields.Count != 2)
					continue;

				var ctor = type.FindMethod(".ctor");
				if (ctor == null || !ctor.IsPrivate)
					continue;
				if (!DotNetUtils.IsMethod(ctor, "System.Void", "(System.Reflection.Assembly)"))
					continue;

				var initMethodTmp = FindInitMethod(type);
				if (initMethodTmp == null)
					continue;
				var getTempFilenameMethod = FindGetTempFilenameMethod(type);
				if (getTempFilenameMethod == null)
					continue;

				return type;
			}

			return null;
		}

		MethodDef FindInitMethod(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!method.IsPublic && !method.IsAssembly)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.Void", "(System.Reflection.Assembly)"))
					continue;

				return method;
			}

			return null;
		}

		MethodDef FindGetTempFilenameMethod(TypeDef type) {
			foreach (var method in type.Methods) {
				if (method.IsStatic || method.Body == null)
					continue;
				if (!method.IsPublic && !method.IsAssembly)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.String", "(System.String)"))
					continue;

				return method;
			}

			return null;
		}

		void FindOtherTypes() {
			FindAssemblyManagerType();
			FindXmlParserType();
			FindStreamProviderType();
		}

		void FindAssemblyManagerType() {
			if (bundleType == null)
				return;

			foreach (var field in bundleType.Fields) {
				var type = field.FieldSig.GetFieldType().TryGetTypeDef();
				if (type == null)
					continue;
				if (type == bundleType)
					continue;
				if (type.Fields.Count != 2)
					continue;

				var ctor = type.FindMethod(".ctor");
				if (ctor == null)
					continue;
				var sig = ctor.MethodSig;
				if (sig == null || sig.Params.Count != 2)
					continue;
				var iface = sig.Params[1].TryGetTypeDef();
				if (iface == null || !iface.IsInterface)
					continue;

				assemblyManagerType = type;
				bundleStreamProviderIFace = iface;
				return;
			}
		}

		void FindXmlParserType() {
			if (assemblyManagerType == null)
				return;
			foreach (var field in assemblyManagerType.Fields) {
				var type = field.FieldSig.GetFieldType().TryGetTypeDef();
				if (type == null || type.IsInterface)
					continue;
				var ctor = type.FindMethod(".ctor");
				if (!DotNetUtils.IsMethod(ctor, "System.Void", "()"))
					continue;
				if (type.Fields.Count != 1)
					continue;
				var git = type.Fields[0].FieldSig.GetFieldType().ToGenericInstSig();
				if (git == null)
					continue;
				if (git.GenericType.FullName != "System.Collections.Generic.List`1")
					continue;
				if (git.GenericArguments.Count != 1)
					continue;
				var type2 = git.GenericArguments[0].TryGetTypeDef();
				if (type2 == null)
					continue;

				xmlParserType = type;
				bundledAssemblyType = type2;
				return;
			}
		}

		void FindStreamProviderType() {
			if (bundleType == null)
				return;
			var ctor = bundleType.FindMethod(".ctor");
			if (!DotNetUtils.IsMethod(ctor, "System.Void", "(System.Reflection.Assembly)"))
				return;
			foreach (var instr in ctor.Body.Instructions) {
				if (instr.OpCode.Code != Code.Newobj)
					continue;
				var newobjCtor = instr.Operand as MethodDef;
				if (newobjCtor == null)
					continue;
				if (newobjCtor.DeclaringType == assemblyManagerType)
					continue;
				if (!DotNetUtils.IsMethod(newobjCtor, "System.Void", "(System.Reflection.Assembly,System.String)"))
					continue;
				var type = newobjCtor.DeclaringType;
				if (type.Interfaces.Count != 1)
					continue;
				if (type.Interfaces[0].Interface != bundleStreamProviderIFace)
					continue;

				streamProviderType = type;
				return;
			}
		}
	}
}
