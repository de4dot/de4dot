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
using System.Text;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.CryptoObfuscator {
	class StringDecrypter {
		ModuleDefMD module;
		EmbeddedResource stringResource;
		TypeDef stringDecrypterType;
		MethodDef stringDecrypterMethod;
		byte[] decryptedData;

		public bool Detected => stringDecrypterType != null;
		public TypeDef Type => stringDecrypterType;
		public MethodDef Method => stringDecrypterMethod;
		public EmbeddedResource Resource => stringResource;
		public StringDecrypter(ModuleDefMD module) => this.module = module;

		public void Find() {
			if (!FindStringDecrypterType(out var type, out var method))
				return;

			stringDecrypterType = type;
			stringDecrypterMethod = method;
		}

		public void Initialize(ResourceDecrypter resourceDecrypter) {
			if (decryptedData != null || stringDecrypterType == null)
				return;

			var resourceName = GetResourceName();
			stringResource = DotNetUtils.GetResource(module, resourceName) as EmbeddedResource;
			if (stringResource == null)
				return;
			Logger.v("Adding string decrypter. Resource: {0}", Utils.ToCsharpString(stringResource.Name));

			decryptedData = resourceDecrypter.Decrypt(stringResource.CreateReader().AsStream());
		}

		string GetResourceName() {
			var defaultName = module.Assembly.Name.String + module.Assembly.Name.String;

			var cctor = stringDecrypterType.FindStaticConstructor();
			if (cctor == null)
				return defaultName;

			foreach (var s in DotNetUtils.GetCodeStrings(cctor)) {
				if (DotNetUtils.GetResource(module, s) != null)
					return s;
				try {
					return Encoding.UTF8.GetString(Convert.FromBase64String(s));
				}
				catch {
					string s2 = CoUtils.DecryptResourceName(module, cctor);
					try {
						return Encoding.UTF8.GetString(Convert.FromBase64String(s2));
					}
					catch {
					}
				}
			}

			return defaultName;
		}

		public string Decrypt(int index) {
			int len = DeobUtils.ReadVariableLengthInt32(decryptedData, ref index);
			return Encoding.Unicode.GetString(decryptedData, index, len);
		}

		bool FindStringDecrypterType(out TypeDef theType, out MethodDef theMethod) {
			theType = null;
			theMethod = null;

			foreach (var type in module.Types) {
				if (type.IsPublic)
					continue;
				if (type.Fields.Count != 1)
					continue;
				if (DotNetUtils.FindFieldType(type, "System.Byte[]", true) == null)
					continue;
				if (type.Methods.Count != 2 && type.Methods.Count != 3)
					continue;
				if (type.NestedTypes.Count > 0)
					continue;

				MethodDef method = null;
				foreach (var m in type.Methods) {
					if (m.Name == ".ctor" || m.Name == ".cctor")
						continue;
					if (DotNetUtils.IsMethod(m, "System.String", "(System.Int32)")) {
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
