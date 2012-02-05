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
using System.IO;
using System.Security.Cryptography;
using Mono.Cecil;
using ICSharpCode.SharpZipLib.Zip.Compression;
using de4dot.blocks;

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

		public static byte[] desDecrypt(byte[] data, int start, int len, byte[] key, byte[] iv) {
			using (var des = new DESCryptoServiceProvider()) {
				using (var transform = des.CreateDecryptor(key, iv)) {
					return transform.TransformFinalBlock(data, start, len);
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

		public static byte[] inflate(byte[] data, bool hasHeader) {
			return inflate(data, 0, data.Length, hasHeader);
		}

		public static byte[] inflate(byte[] data, int start, int len, bool hasHeader) {
			var buffer = new byte[0x1000];
			var memStream = new MemoryStream();
			var inflater = new Inflater(hasHeader);
			inflater.SetInput(data, start, len);
			while (true) {
				int count = inflater.Inflate(buffer, 0, buffer.Length);
				if (count == 0)
					break;
				memStream.Write(buffer, 0, count);
			}
			return memStream.ToArray();
		}

		public static EmbeddedResource getEmbeddedResourceFromCodeStrings(ModuleDefinition module, MethodDefinition method) {
			foreach (var s in DotNetUtils.getCodeStrings(method)) {
				var resource = DotNetUtils.getResource(module, s) as EmbeddedResource;
				if (resource != null)
					return resource;
			}
			return null;
		}

		public static int readVariableLengthInteger(BinaryReader reader) {
			byte b = reader.ReadByte();
			if ((b & 0x80) == 0)
				return b;
			if ((b & 0x40) == 0)
				return (((int)b & 0x3F) << 8) + reader.ReadByte();
			return (((int)b & 0x3F) << 24) +
					((int)reader.ReadByte() << 16) +
					((int)reader.ReadByte() << 8) +
					reader.ReadByte();
		}
	}
}
