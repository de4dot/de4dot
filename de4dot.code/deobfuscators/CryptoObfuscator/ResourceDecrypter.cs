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
using System.IO.Compression;
using System.Security.Cryptography;
using Mono.Cecil;

namespace de4dot.deobfuscators.CryptoObfuscator {
	class ResourceDecrypter {
		const int BUFLEN = 0x8000;
		ModuleDefinition module;
		byte[] buffer1 = new byte[BUFLEN];
		byte[] buffer2 = new byte[BUFLEN];

		public ResourceDecrypter(ModuleDefinition module) {
			this.module = module;
		}

		public byte[] decrypt(Stream resourceStream) {
			byte flags = (byte)resourceStream.ReadByte();
			Stream sourceStream = resourceStream;

			if ((flags & 1) != 0) {
				var memStream = new MemoryStream((int)resourceStream.Length);
				using (var provider = new DESCryptoServiceProvider()) {
					var iv = new byte[8];
					sourceStream.Read(iv, 0, 8);
					provider.IV = iv;
					provider.Key = getKey(sourceStream);

					using (var transform = provider.CreateDecryptor()) {
						while (true) {
							int count = sourceStream.Read(buffer1, 0, buffer1.Length);
							if (count <= 0)
								break;
							int count2 = transform.TransformBlock(buffer1, 0, count, buffer2, 0);
							memStream.Write(buffer2, 0, count2);
						}
						var finalData = transform.TransformFinalBlock(buffer1, 0, 0);
						memStream.Write(finalData, 0, finalData.Length);
					}
				}
				sourceStream = memStream;
			}

			if ((flags & 2) != 0) {
				var memStream = new MemoryStream((int)resourceStream.Length);
				sourceStream.Position = 0;
				using (var inflater = new DeflateStream(sourceStream, CompressionMode.Decompress)) {
					while (true) {
						int count = inflater.Read(buffer1, 0, buffer1.Length);
						if (count <= 0)
							break;
						memStream.Write(buffer1, 0, count);
					}
				}

				sourceStream = memStream;
			}

			if ((flags & 4) != 0) {
				var memStream = new MemoryStream((int)resourceStream.Length);
				sourceStream.Position = 0;
				for (int i = 0; i < sourceStream.Length; i++)
					memStream.WriteByte((byte)~sourceStream.ReadByte());

				sourceStream = memStream;
			}

			if (sourceStream is MemoryStream) {
				var memStream = (MemoryStream)sourceStream;
				return memStream.ToArray();
			}
			else {
				int len = (int)(sourceStream.Length - sourceStream.Position);
				byte[] data = new byte[len];
				sourceStream.Read(data, 0, len);
				return data;
			}
		}

		byte[] getKey(Stream resourceStream) {
			byte[] key = new byte[8];
			resourceStream.Read(key, 0, key.Length);
			for (int i = 0; i < key.Length; i++) {
				if (key[i] != 0)
					return key;
			}
			key = module.Assembly.Name.PublicKeyToken;
			if (key == null)
				throw new ApplicationException("PublicKeyToken is null, can't decrypt resources");
			return key;
		}
	}
}
