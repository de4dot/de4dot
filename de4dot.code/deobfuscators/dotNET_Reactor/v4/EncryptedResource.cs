/*
    Copyright (C) 2011-2014 de4dot@gmail.com

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
using System.Collections.Generic;
using System.Security.Cryptography;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class EncryptedResource {
		ModuleDefMD module;
		MethodDef resourceDecrypterMethod;
		EmbeddedResource encryptedDataResource;
		byte[] key, iv;

		public TypeDef Type {
			get { return resourceDecrypterMethod == null ? null : resourceDecrypterMethod.DeclaringType; }
		}

		public MethodDef Method {
			get { return resourceDecrypterMethod; }
			set { resourceDecrypterMethod = value; }
		}

		public EmbeddedResource Resource {
			get { return encryptedDataResource; }
		}

		public bool FoundResource {
			get { return encryptedDataResource != null; }
		}

		public EncryptedResource(ModuleDefMD module) {
			this.module = module;
		}

		public EncryptedResource(ModuleDefMD module, EncryptedResource oldOne) {
			this.module = module;
			resourceDecrypterMethod = Lookup(oldOne.resourceDecrypterMethod, "Could not find resource decrypter method");
			if (oldOne.encryptedDataResource != null)
				encryptedDataResource = DotNetUtils.GetResource(module, oldOne.encryptedDataResource.Name.String) as EmbeddedResource;
			key = oldOne.key;
			iv = oldOne.iv;

			if (encryptedDataResource == null && oldOne.encryptedDataResource != null)
				throw new ApplicationException("Could not initialize EncryptedResource");
		}

		T Lookup<T>(T def, string errorMessage) where T : class, ICodedToken {
			return DeobUtils.Lookup(module, def, errorMessage);
		}

		public bool CouldBeResourceDecrypter(MethodDef method, IEnumerable<string> additionalTypes) {
			return CouldBeResourceDecrypter(method, additionalTypes, true);
		}

		public bool CouldBeResourceDecrypter(MethodDef method, IEnumerable<string> additionalTypes, bool checkResource) {
			if (!method.IsStatic)
				return false;
			if (method.Body == null)
				return false;

			var localTypes = new LocalTypes(method);
			var requiredTypes = new List<string> {
				"System.Byte[]",
				"System.IO.BinaryReader",
				"System.IO.MemoryStream",
				"System.Security.Cryptography.CryptoStream",
				"System.Security.Cryptography.ICryptoTransform",
			};
			requiredTypes.AddRange(additionalTypes);
			if (!localTypes.All(requiredTypes))
				return false;
			if (!localTypes.Exists("System.Security.Cryptography.RijndaelManaged") &&
				!localTypes.Exists("System.Security.Cryptography.AesManaged") &&
				!localTypes.Exists("System.Security.Cryptography.SymmetricAlgorithm"))
				return false;

			if (checkResource && FindMethodsDecrypterResource(method) == null)
				return false;

			return true;
		}

		public void Initialize(ISimpleDeobfuscator simpleDeobfuscator) {
			if (resourceDecrypterMethod == null)
				return;

			simpleDeobfuscator.Deobfuscate(resourceDecrypterMethod);

			encryptedDataResource = FindMethodsDecrypterResource(resourceDecrypterMethod);
			if (encryptedDataResource == null)
				return;

			key = ArrayFinder.GetInitializedByteArray(resourceDecrypterMethod, 32);
			if (key == null)
				throw new ApplicationException("Could not find resource decrypter key");
			iv = ArrayFinder.GetInitializedByteArray(resourceDecrypterMethod, 16);
			if (iv == null)
				throw new ApplicationException("Could not find resource decrypter IV");
			if (NeedReverse())
				Array.Reverse(iv);	// DNR 4.5.0.0
			if (UsesPublicKeyToken()) {
				var publicKeyToken = module.Assembly.PublicKeyToken;
				if (publicKeyToken != null && publicKeyToken.Data.Length > 0) {
					for (int i = 0; i < 8; i++)
						iv[i * 2 + 1] = publicKeyToken.Data[i];
				}
			}
		}

		static int[] pktIndexes = new int[16] { 1, 0, 3, 1, 5, 2, 7, 3, 9, 4, 11, 5, 13, 6, 15, 7 };
		bool UsesPublicKeyToken() {
			int pktIndex = 0;
			foreach (var instr in resourceDecrypterMethod.Body.Instructions) {
				if (instr.OpCode.FlowControl != FlowControl.Next) {
					pktIndex = 0;
					continue;
				}
				if (!instr.IsLdcI4())
					continue;
				int val = instr.GetLdcI4Value();
				if (val != pktIndexes[pktIndex++]) {
					pktIndex = 0;
					continue;
				}
				if (pktIndex >= pktIndexes.Length)
					return true;
			}
			return false;
		}

		bool NeedReverse() {
			return DotNetUtils.CallsMethod(resourceDecrypterMethod, "System.Void System.Array::Reverse(System.Array)");
		}

		EmbeddedResource FindMethodsDecrypterResource(MethodDef method) {
			foreach (var s in DotNetUtils.GetCodeStrings(method)) {
				var resource = DotNetUtils.GetResource(module, s) as EmbeddedResource;
				if (resource != null)
					return resource;
			}
			return null;
		}

		public byte[] Decrypt() {
			if (encryptedDataResource == null || key == null || iv == null)
				throw new ApplicationException("Can't decrypt resource");

			return DeobUtils.AesDecrypt(encryptedDataResource.GetResourceData(), key, iv);
		}

		public byte[] Encrypt(byte[] data) {
			if (key == null || iv == null)
				throw new ApplicationException("Can't encrypt resource");

			using (var aes = new RijndaelManaged { Mode = CipherMode.CBC }) {
				using (var transform = aes.CreateEncryptor(key, iv)) {
					return transform.TransformFinalBlock(data, 0, data.Length);
				}
			}
		}
	}
}
