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
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class BooleanDecrypter {
		ModuleDefMD module;
		EncryptedResource encryptedResource;
		byte[] fileData;
		byte[] decryptedData;

		public bool Detected => encryptedResource.Method != null;
		public TypeDef DecrypterType => encryptedResource.Type;
		public MethodDef Method => encryptedResource.Method;
		public EmbeddedResource Resource => encryptedResource.Resource;

		public BooleanDecrypter(ModuleDefMD module) {
			this.module = module;
			encryptedResource = new EncryptedResource(module);
		}

		public BooleanDecrypter(ModuleDefMD module, BooleanDecrypter oldOne) {
			this.module = module;
			encryptedResource = new EncryptedResource(module, oldOne.encryptedResource);
		}

		public void Find() {
			var additionalTypes = new string[] {
				"System.Boolean",
			};
			foreach (var type in module.Types) {
				if (type.BaseType == null || type.BaseType.FullName != "System.Object")
					continue;
				foreach (var method in type.Methods) {
					if (!method.IsStatic || !method.HasBody)
						continue;
					if (!DotNetUtils.IsMethod(method, "System.Boolean", "(System.Int32)"))
						continue;
					if (!encryptedResource.CouldBeResourceDecrypter(method, additionalTypes))
						continue;

					encryptedResource.Method = method;
					return;
				}
			}
		}

		public void Initialize(byte[] fileData, ISimpleDeobfuscator simpleDeobfuscator) {
			if (encryptedResource.Method == null)
				return;
			this.fileData = fileData;

			encryptedResource.Initialize(simpleDeobfuscator);
			if (!encryptedResource.FoundResource)
				return;

			Logger.v("Adding boolean decrypter. Resource: {0}", Utils.ToCsharpString(encryptedResource.Resource.Name));
			decryptedData = encryptedResource.Decrypt();
		}

		public bool Decrypt(int offset) {
			uint byteOffset = BitConverter.ToUInt32(decryptedData, offset);
			return fileData[byteOffset] == 0x80;
		}
	}
}
