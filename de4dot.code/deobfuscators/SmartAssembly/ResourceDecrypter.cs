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
using System.IO;
using System.Security.Cryptography;
using dnlib.DotNet;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class ResourceDecrypter {
		ResourceDecrypterInfo resourceDecrypterInfo;

		public ResourceDecrypter(ResourceDecrypterInfo resourceDecrypterInfo) {
			this.resourceDecrypterInfo = resourceDecrypterInfo;
		}

		public bool CanDecrypt {
			get { return resourceDecrypterInfo != null && resourceDecrypterInfo.CanDecrypt; }
		}

		public byte[] Decrypt(EmbeddedResource resource) {
			if (!CanDecrypt)
				throw new ApplicationException("Can't decrypt resources");
			var encryptedData = resource.GetResourceData();
			return Decrypt(encryptedData);
		}

		byte[] Decrypt(byte[] encryptedData) {
			var reader = new BinaryReader(new MemoryStream(encryptedData));
			int headerMagic = reader.ReadInt32();
			if (headerMagic == 0x04034B50)
				throw new NotImplementedException("Not implemented yet since I haven't seen anyone use it.");

			byte encryption = (byte)(headerMagic >> 24);
			if ((headerMagic & 0x00FFFFFF) != 0x007D7A7B)	// Check if "{z}"
				throw new ApplicationException(string.Format("Invalid SA header magic 0x{0:X8}", headerMagic));

			switch (encryption) {
			case 1:
				int totalInflatedLength = reader.ReadInt32();
				if (totalInflatedLength < 0)
					throw new ApplicationException("Invalid length");
				var inflatedBytes = new byte[totalInflatedLength];
				int partInflatedLength;
				for (int inflateOffset = 0; inflateOffset < totalInflatedLength; inflateOffset += partInflatedLength) {
					int partLength = reader.ReadInt32();
					partInflatedLength = reader.ReadInt32();
					if (partLength < 0 || partInflatedLength < 0)
						throw new ApplicationException("Invalid length");
					var inflater = new Inflater(true);
					inflater.SetInput(encryptedData, checked((int)reader.BaseStream.Position), partLength);
					reader.BaseStream.Seek(partLength, SeekOrigin.Current);
					int realInflatedLen = inflater.Inflate(inflatedBytes, inflateOffset, inflatedBytes.Length - inflateOffset);
					if (realInflatedLen != partInflatedLength)
						throw new ApplicationException("Could not inflate");
				}
				return inflatedBytes;

			case 2:
				if (resourceDecrypterInfo.DES_Key == null || resourceDecrypterInfo.DES_IV == null)
					throw new ApplicationException("DES key / iv have not been set yet");
				using (var provider = new DESCryptoServiceProvider()) {
					provider.Key = resourceDecrypterInfo.DES_Key;
					provider.IV  = resourceDecrypterInfo.DES_IV;
					using (var transform = provider.CreateDecryptor()) {
						return Decrypt(transform.TransformFinalBlock(encryptedData, 4, encryptedData.Length - 4));
					}
				}

			case 3:
				if (resourceDecrypterInfo.AES_Key == null || resourceDecrypterInfo.AES_IV == null)
					throw new ApplicationException("AES key / iv have not been set yet");
				using (var provider = new RijndaelManaged()) {
					provider.Key = resourceDecrypterInfo.AES_Key;
					provider.IV  = resourceDecrypterInfo.AES_IV;
					using (var transform = provider.CreateDecryptor()) {
						return Decrypt(transform.TransformFinalBlock(encryptedData, 4, encryptedData.Length - 4));
					}
				}

			default:
				throw new ApplicationException(string.Format("Unknown encryption type 0x{0:X2}", encryption));
			}
		}
	}
}
