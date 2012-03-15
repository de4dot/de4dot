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
using System.Text;
using Mono.Cecil;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class StringDecrypter {
		ModuleDefinition module;
		EmbeddedResource stringResource;
		TypeDefinition stringDecrypterType;
		MethodDefinition stringDecrypterMethod;
		byte[] decryptedData;

		public bool Detected {
			get { return stringDecrypterType != null; }
		}

		public TypeDefinition Type {
			get { return stringDecrypterType; }
		}

		public MethodDefinition Method {
			get { return stringDecrypterMethod; }
		}

		public EmbeddedResource Resource {
			get { return stringResource; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public void find() {
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

			var resourceName = getResourceName();
			stringResource = DotNetUtils.getResource(module, resourceName) as EmbeddedResource;
			if (stringResource == null)
				return;
			Log.v("Adding string decrypter. Resource: {0}", Utils.toCsharpString(stringResource.Name));

			decryptedData = resourceDecrypter.decrypt(stringResource.GetResourceStream());
		}

		string getResourceName() {
			var defaultName = module.Assembly.Name.Name + module.Assembly.Name.Name;

			var cctor = DotNetUtils.getMethod(stringDecrypterType, ".cctor");
			if (cctor == null)
				return defaultName;

			foreach (var s in DotNetUtils.getCodeStrings(cctor)) {
				if (DotNetUtils.getResource(module, s) != null)
					return s;
				try {
					return Encoding.UTF8.GetString(Convert.FromBase64String(s));
				}
				catch {
				}
			}

			return defaultName;
		}

		public string decrypt(int index) {
			int len = DeobUtils.readVariableLengthInt32(decryptedData, ref index);
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
				if (type.Methods.Count != 2 && type.Methods.Count != 3)
					continue;
				if (type.NestedTypes.Count > 0)
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
