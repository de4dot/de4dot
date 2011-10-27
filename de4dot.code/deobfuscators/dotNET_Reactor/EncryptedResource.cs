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
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using de4dot.blocks;
using de4dot.blocks.cflow;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class EncryptedResource {
		ModuleDefinition module;
		MethodDefinition resourceDecrypterMethod;
		EmbeddedResource encryptedDataResource;
		byte[] key, iv;

		public MethodDefinition ResourceDecrypterMethod {
			get { return resourceDecrypterMethod; }
			set { resourceDecrypterMethod = value; }
		}

		public EmbeddedResource EncryptedDataResource {
			get { return encryptedDataResource; }
		}

		public EncryptedResource(ModuleDefinition module) {
			this.module = module;
		}

		public EncryptedResource(ModuleDefinition module, EncryptedResource oldOne) {
			this.module = module;
			if (oldOne.resourceDecrypterMethod != null)
				resourceDecrypterMethod = module.LookupToken(oldOne.resourceDecrypterMethod.MetadataToken.ToInt32()) as MethodDefinition;
			if (oldOne.encryptedDataResource != null)
				encryptedDataResource = DotNetUtils.getResource(module, oldOne.encryptedDataResource.Name) as EmbeddedResource;
			key = oldOne.key;
			iv = oldOne.iv;

			if (resourceDecrypterMethod == null && oldOne.resourceDecrypterMethod != null)
				throw new ApplicationException("Could not initialize EncryptedResource");
			if (encryptedDataResource == null && oldOne.encryptedDataResource != null)
				throw new ApplicationException("Could not initialize EncryptedResource");
		}

		public bool couldBeResourceDecrypter(MethodDefinition method, IList<string> additionalTypes) {
			if (!method.IsStatic)
				return false;
			if (method.Body == null)
				return false;
			if (method.Body.Instructions.Count < 1000)
				return false;

			var localTypes = new LocalTypes(method);
			var requiredTypes = new List<string> {
				"System.Byte[]",
				"System.IO.BinaryReader",
				"System.IO.MemoryStream",
				"System.Security.Cryptography.CryptoStream",
				"System.Security.Cryptography.ICryptoTransform",
				"System.Security.Cryptography.RijndaelManaged",
			};
			requiredTypes.AddRange(additionalTypes);
			if (!localTypes.all(requiredTypes))
				return false;

			if (findMethodsDecrypterResource(method) == null)
				return false;

			return true;
		}

		public void init(ISimpleDeobfuscator simpleDeobfuscator) {
			if (resourceDecrypterMethod == null)
				return;

			simpleDeobfuscator.deobfuscate(resourceDecrypterMethod);

			encryptedDataResource = findMethodsDecrypterResource(resourceDecrypterMethod);
			if (encryptedDataResource == null)
				throw new ApplicationException("Could not find encrypted resource");

			key = initArray(resourceDecrypterMethod, 32);
			if (key == null)
				throw new ApplicationException("Could not find resource decrypter key");
			iv = initArray(resourceDecrypterMethod, 16);
			if (iv == null)
				throw new ApplicationException("Could not find resource decrypter IV");
			var publicKeyToken = module.Assembly.Name.PublicKeyToken;
			if (publicKeyToken != null && publicKeyToken.Length > 0) {
				for (int i = 0; i < 8; i++)
					iv[i * 2 + 1] = publicKeyToken[i];
			}
		}

		EmbeddedResource findMethodsDecrypterResource(MethodDefinition method) {
			foreach (var s in DotNetUtils.getCodeStrings(method)) {
				var resource = DotNetUtils.getResource(module, s) as EmbeddedResource;
				if (resource != null)
					return resource;
			}
			return null;
		}

		static byte[] initArray(MethodDefinition method, int arraySize) {
			int newarrIndex = findNewarr(method, arraySize);
			if (newarrIndex < 0)
				return null;

			var resultValueArray = new Value[arraySize];

			var emulator = new InstructionEmulator(method.HasThis, false, method.Parameters, method.Body.Variables);
			var theArray = new UnknownValue();
			emulator.push(theArray);

			var instructions = method.Body.Instructions;
			for (int i = newarrIndex + 1; i < instructions.Count; i++) {
				var instr = instructions[i];
				if (instr.OpCode.FlowControl != FlowControl.Next)
					break;
				if (instr.OpCode.Code == Code.Newarr)
					break;

				if (instr.OpCode.Code == Code.Stelem_I1) {
					var value = emulator.pop();
					var index = emulator.pop() as Int32Value;
					var array = emulator.pop();
					if (ReferenceEquals(array, theArray) && index != null && index.allBitsValid()) {
						if (0 <= index.value && index.value < resultValueArray.Length)
							resultValueArray[index.value] = value;
					}
				}
				else
					emulator.emulate(instr);
			}

			byte[] resultArray = new byte[resultValueArray.Length];
			for (int i = 0; i < resultArray.Length; i++) {
				var intValue = resultValueArray[i] as Int32Value;
				if (intValue == null || !intValue.allBitsValid())
					return null;
				resultArray[i] = (byte)intValue.value;
			}

			return resultArray;
		}

		static int findNewarr(MethodDefinition method, int arraySize) {
			var instructions = method.Body.Instructions;
			for (int i = 0; i < instructions.Count; i++) {
				var instr = instructions[i];
				if (instr.OpCode.Code != Code.Newarr || i < 1)
					continue;
				var ldci4 = instructions[i - 1];
				if (!DotNetUtils.isLdcI4(ldci4))
					continue;
				if (DotNetUtils.getLdcI4Value(ldci4) != arraySize)
					continue;

				return i;
			}

			return -1;
		}

		public byte[] decrypt() {
			if (encryptedDataResource == null || key == null || iv == null)
				throw new ApplicationException("Can't decrypt resource");

			using (var aes = new RijndaelManaged()) {
				aes.Mode = CipherMode.CBC;
				using (var transform = aes.CreateDecryptor(key, iv)) {
					var encryptedData = encryptedDataResource.GetResourceData();
					return transform.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
				}
			}
		}
	}
}
