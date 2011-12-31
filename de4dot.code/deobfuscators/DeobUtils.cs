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
using System.Security.Cryptography;
using Mono.Cecil;

namespace de4dot.code.deobfuscators {
	static class DeobUtils {
		public static void decryptAndAddResources(ModuleDefinition module, string encryptedName, Func<byte[]> decryptResource) {
			Log.v("Decrypting resources, name: {0}", Utils.toCsharpString(encryptedName));
			var decryptedResourceData = decryptResource();
			if (decryptedResourceData == null)
				throw new ApplicationException("decryptedResourceData is null");
			var resourceModule = ModuleDefinition.ReadModule(new MemoryStream(decryptedResourceData));

			Log.indent();
			foreach (var rsrc in resourceModule.Resources) {
				Log.v("Adding decrypted resource {0}", Utils.toCsharpString(rsrc.Name));
				module.Resources.Add(rsrc);
			}
			Log.deIndent();
		}

		public static T lookup<T>(ModuleDefinition module, T def, string errorMessage) where T : MemberReference {
			if (def == null)
				return null;
			var newDef = module.LookupToken(def.MetadataToken.ToInt32()) as T;
			if (newDef == null)
				throw new ApplicationException(errorMessage);
			return newDef;
		}

		public static byte[] readModule(ModuleDefinition module) {
			return Utils.readFile(module.FullyQualifiedName);
		}

		public static bool isCode(short[] nativeCode, byte[] code) {
			if (nativeCode.Length != code.Length)
				return false;
			for (int i = 0; i < nativeCode.Length; i++) {
				if (nativeCode[i] == -1)
					continue;
				if ((byte)nativeCode[i] != code[i])
					return false;
			}
			return true;
		}

		public static byte[] aesDecrypt(byte[] data, byte[] key, byte[] iv) {
			using (var aes = new RijndaelManaged { Mode = CipherMode.CBC }) {
				using (var transform = aes.CreateDecryptor(key, iv)) {
					return transform.TransformFinalBlock(data, 0, data.Length);
				}
			}
		}

		public static byte[] des3Decrypt(byte[] data, byte[] key, byte[] iv) {
			using (var des3 = TripleDES.Create()) {
				using (var transform = des3.CreateDecryptor(key, iv)) {
					return transform.TransformFinalBlock(data, 0, data.Length);
				}
			}
		}

		public static string getExtension(ModuleKind kind) {
			switch (kind) {
			case ModuleKind.Dll:
				return ".dll";
			case ModuleKind.NetModule:
				return ".netmodule";
			case ModuleKind.Console:
			case ModuleKind.Windows:
			default:
				return ".exe";
			}
		}
	}
}
