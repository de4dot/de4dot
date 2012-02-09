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
using System.IO;
using System.Xml;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CodeVeil {
	class AssemblyResolver {
		ModuleDefinition module;
		EmbeddedResource bundleData;
		EmbeddedResource bundleXmlFile;
		TypeDefinition bundleType;
		List<AssemblyInfo> infos = new List<AssemblyInfo>();

		public class AssemblyInfo {
			public string fullName;
			public string simpleName;
			public string extension;
			public byte[] data;

			public AssemblyInfo(string fullName, string extension, byte[] data) {
				this.fullName = fullName;
				this.simpleName = Utils.getAssemblySimpleName(fullName);
				this.extension = extension;
				this.data = data;
			}

			public override string ToString() {
				return fullName;
			}
		}

		public IEnumerable<AssemblyInfo> AssemblyInfos {
			get { return infos; }
		}

		public EmbeddedResource BundleDataResource {
			get { return bundleData; }
		}

		public EmbeddedResource BundleXmlFileResource {
			get { return bundleXmlFile; }
		}

		public AssemblyResolver(ModuleDefinition module) {
			this.module = module;
		}

		public void initialize() {
			if (!findTypeAndResources())
				return;

			findEmbeddedAssemblies();
		}

		bool findTypeAndResources() {
			var bundleDataTmp = DotNetUtils.getResource(module, ".bundle.dat") as EmbeddedResource;
			var bundleXmlFileTmp = DotNetUtils.getResource(module, ".bundle.manifest") as EmbeddedResource;
			if (bundleDataTmp == null || bundleXmlFileTmp == null)
				return false;

			var bundleTypeTmp = findBundleType();
			if (bundleTypeTmp == null)
				return false;

			bundleData = bundleDataTmp;
			bundleXmlFile = bundleXmlFileTmp;
			bundleType = bundleTypeTmp;
			return true;
		}

		void findEmbeddedAssemblies() {
			var data = bundleData.GetResourceData();

			var doc = new XmlDocument();
			doc.Load(XmlReader.Create(bundleXmlFile.GetResourceStream()));
			var manifest = doc.DocumentElement;
			if (manifest.Name.ToLowerInvariant() != "manifest") {
				Log.w("Could not find Manifest element");
				return;
			}
			foreach (var tmp in manifest.ChildNodes) {
				var assemblyElem = tmp as XmlElement;
				if (assemblyElem == null)
					continue;

				if (assemblyElem.Name.ToLowerInvariant() != "assembly") {
					Log.w("Unknown element: {0}", assemblyElem.Name);
					continue;
				}

				int offset = getAttributeValueInt32(assemblyElem, "offset");
				if (offset < 0) {
					Log.w("Could not find offset attribute");
					continue;
				}

				var assemblyData = DeobUtils.inflate(data, offset, data.Length - offset, true);
				var mod = ModuleDefinition.ReadModule(new MemoryStream(assemblyData));
				infos.Add(new AssemblyInfo(mod.Assembly.FullName, DeobUtils.getExtension(mod.Kind), assemblyData));
			}
		}

		static int getAttributeValueInt32(XmlElement elem, string attrName) {
			var str = elem.GetAttribute(attrName);
			if (string.IsNullOrEmpty(str))
				return -1;

			int value;
			if (!int.TryParse(str, out value))
				return -1;

			return value;
		}

		TypeDefinition findBundleType() {
			foreach (var type in module.Types) {
				if (type.Namespace != "")
					continue;
				if (type.Fields.Count != 2)
					continue;

				var ctor = DotNetUtils.getMethod(type, ".ctor");
				if (ctor == null || !ctor.IsPrivate)
					continue;
				if (!DotNetUtils.isMethod(ctor, "System.Void", "(System.Reflection.Assembly)"))
					continue;

				var initMethodTmp = findInitMethod(type);
				if (initMethodTmp == null)
					continue;
				var getTempFilenameMethod = findGetTempFilenameMethod(type);
				if (getTempFilenameMethod == null)
					continue;

				return type;
			}

			return null;
		}

		MethodDefinition findInitMethod(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || method.Body == null)
					continue;
				if (!method.IsPublic && !method.IsAssembly)
					continue;
				if (!DotNetUtils.isMethod(method, "System.Void", "(System.Reflection.Assembly)"))
					continue;

				return method;
			}

			return null;
		}

		MethodDefinition findGetTempFilenameMethod(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (method.IsStatic || method.Body == null)
					continue;
				if (!method.IsPublic && !method.IsAssembly)
					continue;
				if (!DotNetUtils.isMethod(method, "System.String", "(System.String)"))
					continue;

				return method;
			}

			return null;
		}
	}
}
