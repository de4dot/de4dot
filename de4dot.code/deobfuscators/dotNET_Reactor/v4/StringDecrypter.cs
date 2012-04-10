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
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;
using de4dot.PE;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class StringDecrypter {
		ModuleDefinition module;
		EncryptedResource encryptedResource;
		List<DecrypterInfo> decrypterInfos = new List<DecrypterInfo>();
		MethodDefinition otherStringDecrypter;
		byte[] decryptedData;
		PeImage peImage;
		byte[] fileData;
		StringDecrypterVersion stringDecrypterVersion;

		enum StringDecrypterVersion {
			UNKNOWN = 0,
			VER_37,		// 3.7-
			VER_38,		// 3.8+
		}

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
			get { return encryptedResource.Method != null; }
		}

		public TypeDefinition DecrypterType {
			get { return encryptedResource.Type; }
		}

		public EmbeddedResource Resource {
			get { return encryptedResource.Resource; }
		}

		public IEnumerable<DecrypterInfo> DecrypterInfos {
			get { return decrypterInfos; }
		}

		public MethodDefinition OtherStringDecrypter {
			get { return otherStringDecrypter; }
		}

		public StringDecrypter(ModuleDefinition module) {
			this.module = module;
			this.encryptedResource = new EncryptedResource(module);
		}

		public StringDecrypter(ModuleDefinition module, StringDecrypter oldOne) {
			this.module = module;
			this.stringDecrypterVersion = oldOne.stringDecrypterVersion;
			this.encryptedResource = new EncryptedResource(module, oldOne.encryptedResource);
			foreach (var oldInfo in oldOne.decrypterInfos) {
				var method = lookup(oldInfo.method, "Could not find string decrypter method");
				decrypterInfos.Add(new DecrypterInfo(method, oldInfo.key, oldInfo.iv));
			}
			otherStringDecrypter = lookup(oldOne.otherStringDecrypter, "Could not find string decrypter method");
		}

		T lookup<T>(T def, string errorMessage) where T : MemberReference {
			return DeobUtils.lookup(module, def, errorMessage);
		}

		public void find(ISimpleDeobfuscator simpleDeobfuscator) {
			var additionalTypes = new string[] {
				"System.String",
			};
			EmbeddedResource stringsResource = null;
			foreach (var type in module.Types) {
				if (decrypterInfos.Count > 0)
					break;
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
					encryptedResource.Method = method;

					var info = new DecrypterInfo(method, null, null);
					simpleDeobfuscator.deobfuscate(info.method);
					findKeyIv(info.method, out info.key, out info.iv);

					decrypterInfos.Add(info);
				}
			}

			if (decrypterInfos.Count > 0)
				findOtherStringDecrypter(decrypterInfos[0].method.DeclaringType);
		}

		void findOtherStringDecrypter(TypeDefinition type) {
			foreach (var method in type.Methods) {
				if (!method.IsStatic || !method.HasBody)
					continue;
				if (method.MethodReturnType.ReturnType.FullName != "System.String")
					continue;
				if (method.Parameters.Count != 1)
					continue;
				if (method.Parameters[0].ParameterType.FullName != "System.Object" &&
					method.Parameters[0].ParameterType.FullName != "System.String")
					continue;

				otherStringDecrypter = method;
				return;
			}
		}

		public void init(PeImage peImage, byte[] fileData, ISimpleDeobfuscator simpleDeobfuscator) {
			if (encryptedResource.Method == null)
				return;
			this.peImage = peImage;
			this.fileData = fileData;

			encryptedResource.init(simpleDeobfuscator);
			if (!encryptedResource.FoundResource)
				return;
			Log.v("Adding string decrypter. Resource: {0}", Utils.toCsharpString(encryptedResource.Resource.Name));
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
			foreach (var calledMethod in DotNetUtils.getCalledMethods(module, method)) {
				if (calledMethod.DeclaringType != method.DeclaringType)
					continue;
				if (calledMethod.MethodReturnType.ReturnType.FullName != "System.Byte[]")
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

				initializeStringDecrypterVersion(method);
				key = newKey;
				iv = newIv;
				return;
			}
		}

		void initializeStringDecrypterVersion(MethodDefinition method) {
			var localTypes = new LocalTypes(method);
			if (localTypes.exists("System.IntPtr"))
				stringDecrypterVersion = StringDecrypterVersion.VER_38;
			else
				stringDecrypterVersion = StringDecrypterVersion.VER_37;
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
				byte[] encryptedStringData;
				if (stringDecrypterVersion == StringDecrypterVersion.VER_37) {
					int fileOffset = BitConverter.ToInt32(decryptedData, offset);
					int length = BitConverter.ToInt32(fileData, fileOffset);
					encryptedStringData = new byte[length];
					Array.Copy(fileData, fileOffset + 4, encryptedStringData, 0, length);
				}
				else if (stringDecrypterVersion == StringDecrypterVersion.VER_38) {
					uint rva = BitConverter.ToUInt32(decryptedData, offset);
					int length = peImage.readInt32(rva);
					encryptedStringData = peImage.readBytes(rva + 4, length);
				}
				else
					throw new ApplicationException("Unknown string decrypter version");

				return Encoding.Unicode.GetString(DeobUtils.aesDecrypt(encryptedStringData, info.key, info.iv));
			}
		}

		public string decrypt(string s) {
			return Encoding.Unicode.GetString(Convert.FromBase64String(s));
		}
	}
}
