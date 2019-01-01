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
using System.Collections.Generic;
using System.IO;
using dnlib.IO;
using dnlib.DotNet;

namespace de4dot.code.deobfuscators.ILProtector {
	class StaticMethodsDecrypter : MethodsDecrypterBase {
		IDecrypter decrypter;

		interface IDecrypter {
			string Version { get; }
			byte[] GetMethodsData(EmbeddedResource resource);
		}

		class DecrypterBase : IDecrypter {
			protected static readonly byte[] ilpPublicKeyToken = new byte[8] { 0x20, 0x12, 0xD3, 0xC0, 0x55, 0x1F, 0xE0, 0x3D };

			protected string ilpVersion;
			protected int startOffset;
			protected byte[] decryptionKey;
			protected int decryptionKeyMod;

			public string Version => ilpVersion;

			protected void SetVersion(Version version) {
				if (version.Revision == 0)
					ilpVersion = $"{version.Major}.{version.Minor}.{version.Build}";
				else
					ilpVersion = version.ToString();
			}

			public virtual byte[] GetMethodsData(EmbeddedResource resource) {
				var reader = resource.CreateReader();
				reader.Position = (uint)startOffset;
				if ((reader.ReadInt32() & 1) != 0)
					return Decompress(ref reader);
				else
					return reader.ReadRemainingBytes();
			}

			byte[] Decompress(ref DataReader reader) => Decompress(ref reader, decryptionKey, decryptionKeyMod);

			static void Copy(byte[] src, int srcIndex, byte[] dst, int dstIndex, int size) {
				for (int i = 0; i < size; i++)
					dst[dstIndex++] = src[srcIndex++];
			}

			static byte[] Decompress(ref DataReader reader, byte[] key, int keyMod) =>
				Decompress(new byte[reader.Read7BitEncodedUInt32()], ref reader, key, keyMod);

			protected static byte[] Decompress(byte[] decrypted, ref DataReader reader, byte[] key, int keyMod) {
				int destIndex = 0;
				while (reader.Position < reader.Length) {
					if (destIndex >= decrypted.Length)
						break;
					byte flags = reader.ReadByte();
					for (int mask = 1; mask != 0x100; mask <<= 1) {
						if (reader.Position >= reader.Length)
							break;
						if (destIndex >= decrypted.Length)
							break;
						if ((flags & mask) != 0) {
							int displ = (int)reader.Read7BitEncodedUInt32();
							int size = (int)reader.Read7BitEncodedUInt32();
							Copy(decrypted, destIndex - displ, decrypted, destIndex, size);
							destIndex += size;
						}
						else {
							byte b = reader.ReadByte();
							if (key != null)
								b ^= key[destIndex % keyMod];
							decrypted[destIndex++] = b;
						}
					}
				}

				return decrypted;
			}
		}

		// 1.0.0 - 1.0.4
		class DecrypterV100 : DecrypterBase {
			// This is the first four bytes of ILProtector's public key token
			const uint RESOURCE_MAGIC = 0xC0D31220;

			DecrypterV100(Version ilpVersion) {
				SetVersion(ilpVersion);
				startOffset = 8;
				decryptionKey = ilpPublicKeyToken;
				decryptionKeyMod = 8;
			}

			public static DecrypterV100 Create(ref DataReader reader) {
				reader.Position = 0;
				if (reader.Length < 12)
					return null;
				if (reader.ReadUInt32() != RESOURCE_MAGIC)
					return null;

				return new DecrypterV100(new Version(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()));
			}
		}

		// 1.0.5
		class DecrypterV105 : DecrypterBase {
			DecrypterV105(Version ilpVersion, byte[] key) {
				SetVersion(ilpVersion);
				startOffset = 0xA0;
				decryptionKey = key;
				decryptionKeyMod = key.Length - 1;
			}

			public static DecrypterV105 Create(ref DataReader reader) {
				reader.Position = 0;
				if (reader.Length < 0xA4)
					return null;
				var key = reader.ReadBytes(0x94);
				if (!Utils.Compare(reader.ReadBytes(8), ilpPublicKeyToken))
					return null;
				return new DecrypterV105(new Version(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte()), key);
			}
		}

		// 1.0.6.x
		class DecrypterV106 : DecrypterBase {
			byte[] decryptionKey6;
			byte[] decryptionKey7;

			DecrypterV106(byte[] key0, byte[] key6, byte[] key7, int startOffset) {
				ilpVersion = "1.0.6.x";
				this.startOffset = startOffset;
				decryptionKey = key0;
				decryptionKey6 = key6;
				decryptionKey7 = key7;
				decryptionKeyMod = key0.Length - 1;
			}

			public static DecrypterV106 Create(ref DataReader reader) {
				try {
					int keyXorOffs7 = (ReadByteAt(ref reader, 0) ^ ReadByteAt(ref reader, 2)) + 2;
					reader.Position = (uint)(keyXorOffs7 + (ReadByteAt(ref reader, 1) ^ ReadByteAt(ref reader, keyXorOffs7)));

					int sha1DataLen = reader.Read7BitEncodedInt32() + 0x80;
					int keyXorOffs6 = (int)reader.Position;
					int encryptedOffs = (int)reader.Position + sha1DataLen;
					var sha1Data = reader.ReadBytes(sha1DataLen);
					uint crc32 = CRC32.CheckSum(sha1Data);

					reader.Position = reader.Length - 0x18;
					uint origCrc32 = reader.ReadUInt32();
					if (crc32 != origCrc32)
						return null;

					var key0 = DeobUtils.Sha1Sum(sha1Data);				// 1.0.6.0
					var key6 = GetKey(ref reader, key0, keyXorOffs6);	// 1.0.6.6
					var key7 = GetKey(ref reader, key0, keyXorOffs7);	// 1.0.6.7
					return new DecrypterV106(key0, key6, key7, encryptedOffs);
				}
				catch (Exception ex) when (ex is IOException || ex is ArgumentException) {
					return null;
				}
			}

			static byte[] GetKey(ref DataReader reader, byte[] sha1Sum, int offs) {
				var key = (byte[])sha1Sum.Clone();
				reader.Position = (uint)offs;
				for (int i = 0; i < key.Length; i++)
					key[i] ^= reader.ReadByte();
				return key;
			}

			static byte ReadByteAt(ref DataReader reader, int offs) {
				reader.Position = (uint)offs;
				return reader.ReadByte();
			}

			public override byte[] GetMethodsData(EmbeddedResource resource) {
				var reader = resource.CreateReader();
				reader.Position = (uint)startOffset;
				var decrypted = new byte[reader.Read7BitEncodedUInt32()];
				uint origCrc32 = reader.ReadUInt32();
				uint pos = reader.Position;

				var keys = new byte[][] { decryptionKey, decryptionKey6, decryptionKey7 };
				foreach (var key in keys) {
					try {
						reader.Position = pos;
						Decompress(decrypted, ref reader, key, decryptionKeyMod);
						uint crc32 = CRC32.CheckSum(decrypted);
						if (crc32 == origCrc32)
							return decrypted;
					}
					catch (OutOfMemoryException) {
					}
					catch (Exception ex) when (ex is IOException || ex is ArgumentException) {
					}
				}

				throw new ApplicationException("Could not decrypt methods data");
			}
		}

		public string Version => decrypter?.Version;
		public bool Detected => methodsResource != null;
		public StaticMethodsDecrypter(ModuleDefMD module, MainType mainType) : base(module, mainType) { }

		public void Find() {
			foreach (var tmp in module.Resources) {
				var resource = tmp as EmbeddedResource;
				if (resource == null)
					continue;
				var reader = resource.CreateReader();
				reader.Position = 0;
				if (!CheckResourceV100(ref reader) &&
					!CheckResourceV105(ref reader) &&
					!CheckResourceV106(ref reader))
					continue;

				methodsResource = resource;
				break;
			}
		}

		bool CheckResourceV100(ref DataReader reader) {
			decrypter = DecrypterV100.Create(ref reader);
			return decrypter != null;
		}

		bool CheckResourceV105(ref DataReader reader) {
			decrypter = DecrypterV105.Create(ref reader);
			return decrypter != null;
		}

		bool CheckResourceV106(ref DataReader reader) {
			decrypter = DecrypterV106.Create(ref reader);
			return decrypter != null;
		}

		protected override void DecryptInternal() {
			if (methodsResource == null || decrypter == null)
				return;

			foreach (var info in ReadMethodInfos(decrypter.GetMethodsData(methodsResource)))
				methodInfos[info.id] = info;
		}

		static DecryptedMethodInfo[] ReadMethodInfos(byte[] data) {
			var toOffset = new Dictionary<DecryptedMethodInfo, int>();
			var reader = ByteArrayDataReaderFactory.CreateReader(data);
			int numMethods = (int)reader.Read7BitEncodedUInt32();
			/*int totalCodeSize = (int)*/reader.Read7BitEncodedUInt32();
			var methodInfos = new DecryptedMethodInfo[numMethods];
			int offset = 0;
			for (int i = 0; i < numMethods; i++) {
				int id = (int)reader.Read7BitEncodedUInt32();
				int size = (int)reader.Read7BitEncodedUInt32();
				var info = new DecryptedMethodInfo(id, size);
				methodInfos[i] = info;
				toOffset[info] = offset;
				offset += size;
			}
			uint dataOffset = reader.Position;
			foreach (var info in methodInfos) {
				reader.Position = dataOffset + (uint)toOffset[info];
				reader.ReadBytes(info.data, 0, info.data.Length);
			}
			return methodInfos;
		}
	}
}
