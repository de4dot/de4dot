/*
    Copyright (C) 2011 de4dot@gmail.com

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

using System.Text;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.deobfuscators.CryptoObfuscator {
	class StringDecrypter {
		ModuleDefinition module;
		EmbeddedResource stringResource;
		TypeDefinition stringDecrypterType;
		MethodDefinition stringDecrypterMethod;
		byte[] decryptedData;

		public bool Detected {
			get { return stringDecrypterType != null; }
		}

		public MethodDefinition StringDecrypterMethod {
			get { return stringDecrypterMethod; }
		}

		public EmbeddedResource StringResource {
			get { return stringResource; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void detect() {
			TypeDefinition type;
			MethodDefinition method;
			if (!findStringDecrypterType(out type, out method))
				return;

			stringDecrypterType = type;
			stringDecrypterMethod = method;
		}

		public void init(ResourceDecrypter resourceDecrypter) {
			if (decryptedData != null)
				return;

			var resourceName = module.Assembly.Name.Name + module.Assembly.Name.Name;
			stringResource = DotNetUtils.getResource(module, resourceName) as EmbeddedResource;
			if (stringResource == null)
				return;

			decryptedData = resourceDecrypter.decrypt(stringResource.GetResourceStream());
		}

		public string decrypt(int index) {
			int len;
			byte b = decryptedData[index++];
			if ((b & 0x80) == 0)
				len = b;
			else if ((b & 0x40) == 0)
				len = ((b & 0x3F) << 8) + decryptedData[index++];
			else {
				len = ((b & 0x3F) << 24) +
						((int)decryptedData[index++] << 16) +
						((int)decryptedData[index++] << 8) +
						decryptedData[index++];
			}

			return Encoding.Unicode.GetString(decryptedData, index, len);
		}

		bool findStringDecrypterType(out TypeDefinition theType, out MethodDefinition theMethod) {
			theType = null;
			theMethod = null;

			foreach (var type in module.Types) {
				if (type.IsPublic)
					continue;
				if (type.Fields.Count != 1)
					continue;
				if (DotNetUtils.findFieldType(type, "System.Byte[]", true) == null)
					continue;
				if (type.Methods.Count != 3)
					continue;
				if (type.HasEvents || type.HasProperties)
					continue;

				MethodDefinition method = null;
				foreach (var m in type.Methods) {
					if (m.Name == ".ctor" || m.Name == ".cctor")
						continue;
					if (DotNetUtils.isMethod(m, "System.String", "(System.Int32)")) {
						method = m;
						continue;
					}
					break;
				}
				if (method == null)
					continue;

				theType = type;
				theMethod = method;
				return true;
			}

			return false;
		}
	}
}
