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

			public string Version {
				get { return ilpVersion; }
			}

			protected void SetVersion(Version version) {
				if (version.Revision == 0)
					ilpVersion = string.Format("{0}.{1}.{2}", version.Major, version.Minor, version.Build);
				else
					ilpVersion = version.ToString();
			}

			public virtual byte[] GetMethodsData(EmbeddedResource resource) {
				var reader = resource.Data;
				reader.Position = startOffset;
				if ((reader.ReadInt32() & 1) != 0)
					return Decompress(reader);
				else
					return reader.ReadRemainingBytes();
			}

			byte[] Decompress(IBinaryReader reader) {
				return Decompress(reader, decryptionKey, decryptionKeyMod);
			}

			static void Copy(byte[] src, int srcIndex, byte[] dst, int dstIndex, int size) {
				for (int i = 0; i < size; i++)
					dst[dstIndex++] = src[srcIndex++];
			}

			static byte[] Decompress(IBinaryReader reader, byte[] key, int keyMod) {
				return Decompress(new byte[reader.Read7BitEncodedUInt32()], reader, key, keyMod);
			}

			protected static byte[] Decompress(byte[] decrypted, IBinaryReader reader, byte[] key, int keyMod) {
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
				this.startOffset = 8;
				this.decryptionKey = ilpPublicKeyToken;
				this.decryptionKeyMod = 8;
			}

			public static DecrypterV100 Create(IBinaryReader reader) {
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
				this.startOffset = 0xA0;
				this.decryptionKey = key;
				this.decryptionKeyMod = key.Length - 1;
			}

			public static DecrypterV105 Create(IBinaryReader reader) {
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
				this.ilpVersion = "1.0.6.x";
				this.startOffset = startOffset;
				this.decryptionKey = key0;
				this.decryptionKey6 = key6;
				this.decryptionKey7 = key7;
				this.decryptionKeyMod = key0.Length - 1;
			}

			public static DecrypterV106 Create(IBinaryReader reader) {
				try {
					int keyXorOffs7 = (ReadByteAt(reader, 0) ^ ReadByteAt(reader, 2)) + 2;
					reader.Position = keyXorOffs7 + (ReadByteAt(reader, 1) ^ ReadByteAt(reader, keyXorOffs7));

					int sha1DataLen = reader.Read7BitEncodedInt32() + 0x80;
					int keyXorOffs6 = (int)reader.Position;
					int encryptedOffs = (int)reader.Position + sha1DataLen;
					var sha1Data = reader.ReadBytes(sha1DataLen);
					uint crc32 = CRC32.CheckSum(sha1Data);

					reader.Position = reader.Length - 0x18;
					uint origCrc32 = reader.ReadUInt32();
					if (crc32 != origCrc32)
						return null;

					var key0 = DeobUtils.Sha1Sum(sha1Data);			// 1.0.6.0
					var key6 = GetKey(reader, key0, keyXorOffs6);	// 1.0.6.6
					var key7 = GetKey(reader, key0, keyXorOffs7);	// 1.0.6.7
					return new DecrypterV106(key0, key6, key7, encryptedOffs);
				}
				catch (IOException) {
					return null;
				}
			}

			static byte[] GetKey(IBinaryReader reader, byte[] sha1Sum, int offs) {
				var key = (byte[])sha1Sum.Clone();
				reader.Position = offs;
				for (int i = 0; i < key.Length; i++)
					key[i] ^= reader.ReadByte();
				return key;
			}

			static byte ReadByteAt(IBinaryReader reader, int offs) {
				reader.Position = offs;
				return reader.ReadByte();
			}

			public override byte[] GetMethodsData(EmbeddedResource resource) {
				var reader = resource.Data;
				reader.Position = startOffset;
				var decrypted = new byte[reader.Read7BitEncodedUInt32()];
				uint origCrc32 = reader.ReadUInt32();
				long pos = reader.Position;

				var keys = new byte[][] { decryptionKey, decryptionKey6, decryptionKey7 };
				foreach (var key in keys) {
					try {
						reader.Position = pos;
						Decompress(decrypted, reader, key, decryptionKeyMod);
						uint crc32 = CRC32.CheckSum(decrypted);
						if (crc32 == origCrc32)
							return decrypted;
					}
					catch (OutOfMemoryException) {
					}
					catch (IOException) {
					}
				}

				throw new ApplicationException("Could not decrypt methods data");
			}
		}

		public string Version {
			get { return decrypter == null ? null : decrypter.Version; }
		}

		public bool Detected {
			get { return methodsResource != null; }
		}

		public StaticMethodsDecrypter(ModuleDefMD module, MainType mainType)
			: base(module, mainType) {
		}

		public void Find() {
			foreach (var tmp in module.Resources) {
				var resource = tmp as EmbeddedResource;
				if (resource == null)
					continue;
				var reader = resource.Data;
				reader.Position = 0;
				if (!CheckResourceV100(reader) &&
					!CheckResourceV105(reader) &&
					!CheckResourceV106(reader))
					continue;

				methodsResource = resource;
				break;
			}
		}

		bool CheckResourceV100(IBinaryReader reader) {
			decrypter = DecrypterV100.Create(reader);
			return decrypter != null;
		}

		bool CheckResourceV105(IBinaryReader reader) {
			decrypter = DecrypterV105.Create(reader);
			return decrypter != null;
		}

		bool CheckResourceV106(IBinaryReader reader) {
			decrypter = DecrypterV106.Create(reader);
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
			var reader = MemoryImageStream.Create(data);
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
			long dataOffset = reader.Position;
			foreach (var info in methodInfos) {
				reader.Position = dataOffset + toOffset[info];
				reader.Read(info.data, 0, info.data.Length);
			}
			return methodInfos;
		}
	}
}
