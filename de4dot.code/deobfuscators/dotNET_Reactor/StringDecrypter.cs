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

using System;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class StringDecrypter {
		ModuleDefinition module;
		EncryptedResource encryptedResource;
		List<DecrypterInfo> decrypterInfos = new List<DecrypterInfo>();
		byte[] decryptedData;
		PE.PeImage peImage;

		public class DecrypterInfo {
			public MethodDefinition method;
			public byte[] key;
			public byte[] iv;

			public DecrypterInfo(MethodDefinition method, byte[] key, byte[] iv) {
				this.method = method;
				this.key = key;
				this.iv = iv;
			}
		}

		public bool Detected {
			get { return encryptedResource.ResourceDecrypterMethod != null; }
		}

		public IEnumerable<DecrypterInfo> DecrypterInfos {
			get { return decrypterInfos; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
			this.encryptedResource = new EncryptedResource(module);
		}

		public StringDecrypter(ModuleDefinition module, StringDecrypter oldOne) {
			this.module = module;
			this.encryptedResource = new EncryptedResource(module, oldOne.encryptedResource);
			foreach (var oldInfo in oldOne.decrypterInfos) {
				var method = module.LookupToken(oldInfo.method.MetadataToken.ToInt32()) as MethodDefinition;
				if (method == null)
					throw new ApplicationException("Could not find string decrypter method");
				decrypterInfos.Add(new DecrypterInfo(method, oldInfo.key, oldInfo.iv));
			}
		}

		public void find() {
			var additionalTypes = new string[] {
				"System.String",
			};
			EmbeddedResource stringsResource = null;
			foreach (var type in module.Types) {
				if (type.BaseType == null || type.BaseType.FullName != "System.Object")
					continue;
				foreach (var method in type.Methods) {
					if (!method.IsStatic || !method.HasBody)
						continue;
					if (!DotNetUtils.isMethod(method, "System.String", "(System.Int32)"))
						continue;
					if (!encryptedResource.couldBeResourceDecrypter(method, additionalTypes))
						continue;

					var resource = DotNetUtils.getResource(module, DotNetUtils.getCodeStrings(method)) as EmbeddedResource;
					if (resource == null)
						throw new ApplicationException("Could not find strings resource");
					if (stringsResource != null && stringsResource != resource)
						throw new ApplicationException("Two different string resources found");

					stringsResource = resource;
					encryptedResource.ResourceDecrypterMethod = method;
					decrypterInfos.Add(new DecrypterInfo(method, null, null));
				}
			}
		}

		public void init(PE.PeImage peImage, ISimpleDeobfuscator simpleDeobfuscator) {
			if (encryptedResource.ResourceDecrypterMethod == null)
				return;
			this.peImage = peImage;

			foreach (var info in decrypterInfos)
				findKeyIv(info.method, out info.key, out info.iv);

			encryptedResource.init(simpleDeobfuscator);
			decryptedData = encryptedResource.decrypt();
		}

		void findKeyIv(MethodDefinition method, out byte[] key, out byte[] iv) {
			key = null;
			iv = null;

			var requiredTypes = new string[] {
				"System.Byte[]",
				"System.IO.MemoryStream",
				"System.Security.Cryptography.CryptoStream",
				"System.Security.Cryptography.Rijndael",
			};
			foreach (var info in DotNetUtils.getCalledMethods(module, method)) {
				var calledMethod = info.Item2;
				if (calledMethod.DeclaringType != method.DeclaringType)
					continue;
				var localTypes = new LocalTypes(calledMethod);
				if (!localTypes.all(requiredTypes))
					continue;

				var instructions = calledMethod.Body.Instructions;
				byte[] newKey = null, newIv = null;
				for (int i = 0; i < instructions.Count && (newKey == null || newIv == null); i++) {
					var instr = instructions[i];
					if (instr.OpCode.Code != Code.Ldtoken)
						continue;
					var field = instr.Operand as FieldDefinition;
					if (field == null)
						continue;
					if (field.InitialValue == null)
						continue;
					if (field.InitialValue.Length == 32)
						newKey = field.InitialValue;
					else if (field.InitialValue.Length == 16)
						newIv = field.InitialValue;
				}
				if (newKey == null || newIv == null)
					continue;

				key = newKey;
				iv = newIv;
				return;
			}
		}

		DecrypterInfo getDecrypterInfo(MethodDefinition method) {
			foreach (var info in decrypterInfos) {
				if (info.method == method)
					return info;
			}
			throw new ApplicationException("Invalid string decrypter method");
		}

		public string decrypt(MethodDefinition method, int offset) {
			var info = getDecrypterInfo(method);

			if (info.key == null) {
				int length = BitConverter.ToInt32(decryptedData, offset);
				return Encoding.Unicode.GetString(decryptedData, offset + 4, length);
			}
			else {
				uint rva = BitConverter.ToUInt32(decryptedData, offset);
				int length = peImage.readInt32(rva);
				var encryptedStringData = peImage.readBytes(rva + 4, length);
				byte[] decryptedStringData;
				using (var aes = new RijndaelManaged()) {
					aes.Mode = CipherMode.CBC;
					using (var transform = aes.CreateDecryptor(info.key, info.iv)) {
						decryptedStringData = transform.TransformFinalBlock(encryptedStringData, 0, encryptedStringData.Length);
					}
				}
				return Encoding.Unicode.GetString(decryptedStringData, 0, decryptedStringData.Length);
			}
		}
	}
}
