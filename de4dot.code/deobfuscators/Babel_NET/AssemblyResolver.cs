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
using System.IO;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.Babel_NET {
	class AssemblyResolver {
		ModuleDefMD module;
		ResourceDecrypter resourceDecrypter;
		TypeDef resolverType;
		MethodDef registerMethod;
		EmbeddedResource encryptedResource;
		EmbeddedAssemblyInfo[] embeddedAssemblyInfos = new EmbeddedAssemblyInfo[0];

		public class EmbeddedAssemblyInfo {
			public string fullname;
			public string extension;
			public byte[] data;

			public EmbeddedAssemblyInfo(string fullName, string extension, byte[] data) {
				fullname = fullName;
				this.extension = extension;
				this.data = data;
			}
		}

		public bool Detected => resolverType != null;
		public TypeDef Type => resolverType;
		public MethodDef InitMethod => registerMethod;
		public EmbeddedResource EncryptedResource => encryptedResource;
		public EmbeddedAssemblyInfo[] EmbeddedAssemblyInfos => embeddedAssemblyInfos;

		public AssemblyResolver(ModuleDefMD module, ResourceDecrypter resourceDecrypter) {
			this.module = module;
			this.resourceDecrypter = resourceDecrypter;
		}

		public void Find() {
			var requiredTypes = new string[] {
				"System.Object",
				"System.Int32",
				"System.Collections.Hashtable",
			};
			foreach (var type in module.Types) {
				if (type.HasEvents)
					continue;
				if (!new FieldTypes(type).Exactly(requiredTypes))
					continue;

				if (!BabelUtils.FindRegisterMethod(type, out var regMethod, out var handler))
					continue;

				var decryptMethod = FindDecryptMethod(type);
				if (decryptMethod == null)
					throw new ApplicationException("Couldn't find resource type decrypt method");
				resourceDecrypter.DecryptMethod = ResourceDecrypter.FindDecrypterMethod(decryptMethod);

				resolverType = type;
				registerMethod = regMethod;
				return;
			}
		}

		static MethodDef FindDecryptMethod(TypeDef type) {
			foreach (var method in type.Methods) {
				if (!DotNetUtils.IsMethod(method, "System.Void", "(System.IO.Stream)"))
					continue;
				return method;
			}
			return null;
		}

		public void Initialize(ISimpleDeobfuscator simpleDeobfuscator, IDeobfuscator deob) {
			if (resolverType == null)
				return;

			encryptedResource = BabelUtils.FindEmbeddedResource(module, resolverType, simpleDeobfuscator, deob);
			if (encryptedResource == null) {
				Logger.w("Could not find embedded assemblies resource");
				return;
			}

			var decrypted = resourceDecrypter.Decrypt(encryptedResource.CreateReader().ToArray());
			var reader = new BinaryReader(new MemoryStream(decrypted));
			int numAssemblies = reader.ReadInt32();
			embeddedAssemblyInfos = new EmbeddedAssemblyInfo[numAssemblies];
			for (int i = 0; i < numAssemblies; i++) {
				string name = reader.ReadString();
				var data = reader.ReadBytes(reader.ReadInt32());
				var mod = ModuleDefMD.Load(data);
				embeddedAssemblyInfos[i] = new EmbeddedAssemblyInfo(name, DeobUtils.GetExtension(mod.Kind), data);
			}
		}
	}
}
