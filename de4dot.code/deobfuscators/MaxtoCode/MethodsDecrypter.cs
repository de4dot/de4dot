/*
    Copyright (C) 2011-2013 de4dot@gmail.com

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
using System.Text;
using dnlib.IO;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.MaxtoCode {
	// Decrypts methods, resources and strings (#US heap)
	class MethodsDecrypter {
		DecrypterInfo decrypterInfo;

		class MethodInfos {
			MainType mainType;
			MyPEImage peImage;
			PeHeader peHeader;
			McKey mcKey;
			uint structSize;
			uint methodInfosOffset;
			uint encryptedDataOffset;
			uint xorKey;
			Dictionary<uint, DecryptedMethodInfo> infos = new Dictionary<uint, DecryptedMethodInfo>();
			List<IDecrypter> decrypters = new List<IDecrypter>();
			const int ENCRYPTED_DATA_INFO_SIZE = 0x13;

			delegate byte[] DecryptFunc(byte[] encrypted);
			readonly DecryptFunc[] decryptHandlersV1;
			readonly DecryptFunc[] decryptHandlersV2;
			readonly DecryptFunc[] decryptHandlersV3;
			readonly DecryptFunc[] decryptHandlersV4;
			readonly DecryptFunc[] decryptHandlersV5a;
			readonly DecryptFunc[] decryptHandlersV5b;
			readonly DecryptFunc[] decryptHandlersV5c;
			readonly DecryptFunc[] decryptHandlersV6a;

			public class DecryptedMethodInfo {
				public uint bodyRva;
				public byte[] body;

				public DecryptedMethodInfo(uint bodyRva, byte[] body) {
					this.bodyRva = bodyRva;
					this.body = body;
				}
			}

			public MethodInfos(MainType mainType, MyPEImage peImage, PeHeader peHeader, McKey mcKey) {
				this.mainType = mainType;
				this.peImage = peImage;
				this.peHeader = peHeader;
				this.mcKey = mcKey;

				decryptHandlersV1 = new DecryptFunc[] { Decrypt1a, Decrypt4a, Decrypt2a, Decrypt3a, Decrypt5, Decrypt6, Decrypt7 };
				decryptHandlersV2 = new DecryptFunc[] { Decrypt3a, Decrypt2a, Decrypt1a, Decrypt4a, Decrypt5, Decrypt6, Decrypt7 };
				decryptHandlersV3 = new DecryptFunc[] { Decrypt1a, Decrypt2a, Decrypt3a, Decrypt4a, Decrypt5, Decrypt6, Decrypt7 };
				decryptHandlersV4 = new DecryptFunc[] { Decrypt2a, Decrypt1a, Decrypt3a, Decrypt4a, Decrypt5, Decrypt6, Decrypt7 };
				decryptHandlersV5a = new DecryptFunc[] { Decrypt4a, Decrypt2a, Decrypt3a, Decrypt1a, Decrypt5, Decrypt6, Decrypt7 };
				decryptHandlersV5b = new DecryptFunc[] { Decrypt4b, Decrypt2b, Decrypt3b, Decrypt1b, Decrypt6, Decrypt7, Decrypt5 };
				decryptHandlersV5c = new DecryptFunc[] { Decrypt4c, Decrypt2c, Decrypt3c, Decrypt1c, Decrypt6, Decrypt7, Decrypt5 };
				decryptHandlersV6a = new DecryptFunc[] { Decrypt4d, Decrypt2d, Decrypt3d, Decrypt1d, Decrypt6, Decrypt7, Decrypt5 };

				structSize = GetStructSize(mcKey);

				uint methodInfosRva = peHeader.GetRva(0x0FF8, mcKey.ReadUInt32(0x005A));
				uint encryptedDataRva = peHeader.GetRva(0x0FF0, mcKey.ReadUInt32(0x0046));

				methodInfosOffset = peImage.RvaToOffset(methodInfosRva);
				encryptedDataOffset = peImage.RvaToOffset(encryptedDataRva);
			}

			static uint GetStructSize(McKey mcKey) {
				uint magicLo = mcKey.ReadUInt32(0x8C0);
				uint magicHi = mcKey.ReadUInt32(0x8C4);
				foreach (var info in EncryptionInfos.McKey8C0h) {
					if (magicLo == info.MagicLo && magicHi == info.MagicHi)
						return 0xC + 6 * ENCRYPTED_DATA_INFO_SIZE;
				}
				return 0xC + 3 * ENCRYPTED_DATA_INFO_SIZE;
			}

			EncryptionVersion GetVersion() {
				if (peHeader.EncryptionVersion != EncryptionVersion.Unknown)
					return peHeader.EncryptionVersion;

				uint m2lo = mcKey.ReadUInt32(0x8C0);
				uint m2hi = mcKey.ReadUInt32(0x8C4);

				foreach (var info in EncryptionInfos.McKey8C0h) {
					if (info.MagicLo == m2lo && info.MagicHi == m2hi)
						return info.Version;
				}

				Logger.w("Could not detect MC version. Magic2: {0:X8} {1:X8}", m2lo, m2hi);
				return EncryptionVersion.Unknown;
			}

			public DecryptedMethodInfo Lookup(uint bodyRva) {
				DecryptedMethodInfo info;
				infos.TryGetValue(bodyRva, out info);
				return info;
			}

			byte ReadByte(uint offset) {
				return peImage.OffsetReadByte(methodInfosOffset + offset);
			}

			short ReadInt16(uint offset) {
				return (short)peImage.OffsetReadUInt16(methodInfosOffset + offset);
			}

			uint ReadUInt32(uint offset) {
				return peImage.OffsetReadUInt32(methodInfosOffset + offset);
			}

			int ReadInt32(uint offset) {
				return (int)ReadUInt32(offset);
			}

			short ReadEncryptedInt16(uint offset) {
				return (short)(ReadInt16(offset) ^ xorKey);
			}

			int ReadEncryptedInt32(uint offset) {
				return (int)ReadEncryptedUInt32(offset);
			}

			uint ReadEncryptedUInt32(uint offset) {
				return ReadUInt32(offset) ^ xorKey;
			}

			interface IDecrypter {
				byte[] Decrypt(int type, byte[] encrypted);
			}

			class Decrypter : IDecrypter {
				DecryptFunc[] decrypterHandlers;

				public Decrypter(DecryptFunc[] decrypterHandlers) {
					this.decrypterHandlers = decrypterHandlers;
				}

				public byte[] Decrypt(int type, byte[] encrypted) {
					if (1 <= type && type <= decrypterHandlers.Length)
						return decrypterHandlers[type - 1](encrypted);
					throw new ApplicationException(string.Format("Invalid encryption type: {0:X2}", type));
				}
			}

			void InitializeDecrypter() {
				switch (GetVersion()) {
				case EncryptionVersion.V1: decrypters.Add(new Decrypter(decryptHandlersV1)); break;
				case EncryptionVersion.V2: decrypters.Add(new Decrypter(decryptHandlersV2)); break;
				case EncryptionVersion.V3: decrypters.Add(new Decrypter(decryptHandlersV3)); break;
				case EncryptionVersion.V4: decrypters.Add(new Decrypter(decryptHandlersV4)); break;
				case EncryptionVersion.V5:
					decrypters.Add(new Decrypter(decryptHandlersV5a));
					decrypters.Add(new Decrypter(decryptHandlersV5b));
					decrypters.Add(new Decrypter(decryptHandlersV5c));
					break;
				case EncryptionVersion.V6:
					decrypters.Add(new Decrypter(decryptHandlersV6a));
					break;

				case EncryptionVersion.Unknown:
				default:
					throw new ApplicationException("Unknown MC version");
				}
			}

			public void InitializeInfos() {
				InitializeDecrypter();
				if (!InitializeInfos2())
					throw new ApplicationException("Could not decrypt methods");
			}

			bool InitializeInfos2() {
				foreach (var decrypter in decrypters) {
					try {
						if (InitializeInfos2(decrypter))
							return true;
					}
					catch {
					}
				}
				return false;
			}

			bool InitializeInfos2(IDecrypter decrypter) {
				int numMethods = ReadInt32(0) ^ ReadInt32(4);
				if (numMethods < 0)
					throw new ApplicationException("Invalid number of encrypted methods");

				xorKey = (uint)numMethods;
				int numEncryptedDataInfos = ((int)structSize - 0xC) / ENCRYPTED_DATA_INFO_SIZE;
				var encryptedDataInfos = new byte[numEncryptedDataInfos][];

				uint offset = 8;
				for (int i = 0; i < numMethods; i++, offset += structSize) {
					uint methodBodyRva = ReadEncryptedUInt32(offset);
					uint totalSize = ReadEncryptedUInt32(offset + 4);
					uint methodInstructionRva = ReadEncryptedUInt32(offset + 8);

					// Read the method body header and method body (instrs + exception handlers).
					// The method body header is always in the first one. The instrs + ex handlers
					// are always in the last 4, and evenly divided (each byte[] is totalLen / 4).
					// The 2nd one is for the exceptions (or padding), but it may be null.
					uint offset2 = offset + 0xC;
					int exOffset = 0;
					for (int j = 0; j < encryptedDataInfos.Length; j++, offset2 += ENCRYPTED_DATA_INFO_SIZE) {
						// readByte(offset2); <-- index
						int encryptionType = ReadEncryptedInt16(offset2 + 1);
						uint dataOffset = ReadEncryptedUInt32(offset2 + 3);
						uint encryptedSize = ReadEncryptedUInt32(offset2 + 7);
						uint realSize = ReadEncryptedUInt32(offset2 + 11);
						if (j == 1)
							exOffset = ReadEncryptedInt32(offset2 + 15);
						if (j == 1 && exOffset == 0)
							encryptedDataInfos[j] = null;
						else
							encryptedDataInfos[j] = Decrypt(decrypter, encryptionType, dataOffset, encryptedSize, realSize);
					}

					var decryptedData = new byte[totalSize];
					int copyOffset = 0;
					copyOffset = CopyData(decryptedData, encryptedDataInfos[0], copyOffset);
					for (int j = 2; j < encryptedDataInfos.Length; j++)
						copyOffset = CopyData(decryptedData, encryptedDataInfos[j], copyOffset);
					CopyData(decryptedData, encryptedDataInfos[1], exOffset); // Exceptions or padding

					if (!MethodBodyParser.Verify(decryptedData))
						throw new InvalidMethodBody();

					var info = new DecryptedMethodInfo(methodBodyRva, decryptedData);
					infos[info.bodyRva] = info;
				}

				return true;
			}

			static int CopyData(byte[] dest, byte[] source, int offset) {
				if (source == null)
					return offset;
				Array.Copy(source, 0, dest, offset, source.Length);
				return offset + source.Length;
			}

			byte[] ReadData(uint offset, int size) {
				return peImage.OffsetReadBytes(encryptedDataOffset + offset, size);
			}

			byte[] Decrypt(IDecrypter decrypter, int type, uint dataOffset, uint encryptedSize, uint realSize) {
				if (realSize == 0)
					return null;
				if (realSize > encryptedSize)
					throw new ApplicationException("Invalid realSize");

				var encrypted = ReadData(dataOffset, (int)encryptedSize);
				var decrypted = decrypter.Decrypt(type, encrypted);
				if (realSize > decrypted.Length)
					throw new ApplicationException("Invalid decrypted length");
				Array.Resize(ref decrypted, (int)realSize);
				return decrypted;
			}

			byte[] Decrypt1a(byte[] encrypted) {
				return Decrypt1(encrypted, 0, 0, 0x2000);
			}

			byte[] Decrypt1b(byte[] encrypted) {
				return Decrypt1(encrypted, 6, 6, 0x500);
			}

			byte[] Decrypt1c(byte[] encrypted) {
				return Decrypt1(encrypted, 6, 0, 0x1000);
			}

			byte[] Decrypt1d(byte[] encrypted) {
				return Decrypt1(encrypted, 5, 5, 0x500);
			}

			byte[] Decrypt1(byte[] encrypted, int keyStart, int keyReset, int keyEnd) {
				var decrypted = new byte[encrypted.Length];
				for (int i = 0, ki = keyStart; i < decrypted.Length; i++) {
					decrypted[i] = (byte)(encrypted[i] ^ mcKey.ReadByte(ki));
					if (++ki == keyEnd)
						ki = keyReset;
				}
				return decrypted;
			}

			byte[] Decrypt2a(byte[] encrypted) {
				return Decrypt2(encrypted, 0x00FA);
			}

			byte[] Decrypt2b(byte[] encrypted) {
				return Decrypt2(encrypted, 0x00FA + 9);
			}

			byte[] Decrypt2c(byte[] encrypted) {
				return Decrypt2(encrypted, 0x00FA + 0x24);
			}

			byte[] Decrypt2d(byte[] encrypted) {
				return Decrypt2(encrypted, 0x00FA + 7);
			}

			byte[] Decrypt2(byte[] encrypted, int offset) {
				if ((encrypted.Length & 7) != 0)
					throw new ApplicationException("Invalid encryption #2 length");
				uint key4 = mcKey.ReadUInt32(offset + 4 * 4);
				uint key5 = mcKey.ReadUInt32(offset + 5 * 4);

				byte[] decrypted = new byte[encrypted.Length & ~7];
				var writer = new BinaryWriter(new MemoryStream(decrypted));

				int loopCount = encrypted.Length / 8;
				for (int i = 0; i < loopCount; i++) {
					uint val0 = BitConverter.ToUInt32(encrypted, i * 8);
					uint val1 = BitConverter.ToUInt32(encrypted, i * 8 + 4);
					uint x = (val1 >> 26) + (val0 << 6);
					uint y = (val0 >> 26) + (val1 << 6);

					writer.Write(x ^ key4);
					writer.Write(y ^ key5);
				}

				return decrypted;
			}

			byte[] Decrypt3a(byte[] encrypted) {
				return Decrypt3(encrypted, 0x015E);
			}

			byte[] Decrypt3b(byte[] encrypted) {
				return Decrypt3(encrypted, 0x015E + 0xE5);
			}

			byte[] Decrypt3c(byte[] encrypted) {
				return Decrypt3(encrypted, 0x015E + 0x28);
			}

			byte[] Decrypt3d(byte[] encrypted) {
				return Decrypt3(encrypted, 0x015E + 8);
			}

			static readonly byte[] decrypt3Shifts = new byte[16] { 5, 11, 14, 21, 6, 20, 17, 29, 4, 10, 3, 2, 7, 1, 26, 18 };
			byte[] Decrypt3(byte[] encrypted, int offset) {
				if ((encrypted.Length & 7) != 0)
					throw new ApplicationException("Invalid encryption #3 length");
				uint key0 = mcKey.ReadUInt32(offset + 0 * 4);
				uint key3 = mcKey.ReadUInt32(offset + 3 * 4);

				byte[] decrypted = new byte[encrypted.Length & ~7];
				var writer = new BinaryWriter(new MemoryStream(decrypted));

				int loopCount = encrypted.Length / 8;
				for (int i = 0; i < loopCount; i++) {
					uint x = BitConverter.ToUInt32(encrypted, i * 8);
					uint y = BitConverter.ToUInt32(encrypted, i * 8 + 4);
					foreach (var shift in decrypt3Shifts) {
						int shift1 = 32 - shift;
						uint x1 = (y >> shift1) + (x << shift);
						uint y1 = (x >> shift1) + (y << shift);
						x = x1;
						y = y1;
					}

					writer.Write(x ^ key0);
					writer.Write(y ^ key3);
				}

				return decrypted;
			}

			byte[] Decrypt4a(byte[] encrypted) {
				return Decrypt4(encrypted, 0, 0, 0x2000);
			}

			byte[] Decrypt4b(byte[] encrypted) {
				return Decrypt4(encrypted, 0x14, 0x14, 0x1000);
			}

			byte[] Decrypt4c(byte[] encrypted) {
				return Decrypt4(encrypted, 5, 0, 0x2000);
			}

			byte[] Decrypt4d(byte[] encrypted) {
				return Decrypt4(encrypted, 0x0B, 0x0B, 0x1000);
			}

			byte[] Decrypt4(byte[] encrypted, int keyStart, int keyReset, int keyEnd) {
				var decrypted = new byte[encrypted.Length / 3 * 2 + 1];

				int count = encrypted.Length / 3;
				int i = 0, ki = keyStart, j = 0;
				while (count-- > 0) {
					byte k1 = mcKey.ReadByte(ki + 1);
					byte k2 = mcKey.ReadByte(ki + 2);
					byte k3 = mcKey.ReadByte(ki + 3);
					decrypted[j++] = (byte)(((encrypted[i + 1] ^ k2) >> 4) | ((encrypted[i] ^ k1) & 0xF0));
					decrypted[j++] = (byte)(((encrypted[i + 1] ^ k2) << 4) + ((encrypted[i + 2] ^ k3) & 0x0F));
					i += 3;
					ki += 4;
					if (ki >= keyEnd)
						ki = keyReset;
				}

				if ((encrypted.Length % 3) != 0)
					decrypted[j] = (byte)(encrypted[i] ^ mcKey.ReadByte(ki));

				return decrypted;
			}

			byte[] Decrypt5(byte[] encrypted) {
				return CryptDecrypter.Decrypt(mcKey.ReadBytes(0x0032, 15), encrypted);
			}

			byte[] Decrypt6(byte[] encrypted) {
				return Decrypter6.Decrypt(mcKey.ReadBytes(0x0096, 32), encrypted);
			}

			byte[] Decrypt7(byte[] encrypted) {
				var decrypted = (byte[])encrypted.Clone();
				new Blowfish(GetBlowfishKey()).Decrypt_LE(decrypted);
				return decrypted;
			}

			byte[] blowfishKey;
			byte[] GetBlowfishKey() {
				if (blowfishKey != null)
					return blowfishKey;
				var key = new byte[100];
				int i;
				for (i = 0; i < key.Length; i++) {
					byte b = mcKey.ReadByte(i);
					if (b == 0)
						break;
					key[i] = b;
				}
				for (; i < key.Length; i++)
					key[i] = 0;
				key[key.Length - 1] = 0;
				return blowfishKey = key;
			}
		}

		public MethodsDecrypter(DecrypterInfo decrypterInfo) {
			this.decrypterInfo = decrypterInfo;
		}

		public bool Decrypt(ref DumpedMethods dumpedMethods) {
			dumpedMethods = DecryptMethods();
			if (dumpedMethods == null)
				return false;

			DecryptResources();
			DecryptStrings();

			return true;
		}

		DumpedMethods DecryptMethods() {
			var dumpedMethods = new DumpedMethods();

			var peImage = decrypterInfo.peImage;
			var methodInfos = new MethodInfos(decrypterInfo.mainType, peImage, decrypterInfo.peHeader, decrypterInfo.mcKey);
			methodInfos.InitializeInfos();

			var methodDef = peImage.DotNetFile.MetaData.TablesStream.MethodTable;
			for (uint rid = 1; rid <= methodDef.Rows; rid++) {
				var dm = new DumpedMethod();
				peImage.ReadMethodTableRowTo(dm, rid);

				var info = methodInfos.Lookup(dm.mdRVA);
				if (info == null)
					continue;

				ushort magic = peImage.ReadUInt16(dm.mdRVA);
				if (magic != 0xFFF3)
					continue;

				var mbHeader = MethodBodyParser.ParseMethodBody(MemoryImageStream.Create(info.body), out dm.code, out dm.extraSections);
				peImage.UpdateMethodHeaderInfo(dm, mbHeader);

				dumpedMethods.Add(dm);
			}

			return dumpedMethods;
		}

		void DecryptResources() {
			var peHeader = decrypterInfo.peHeader;
			var mcKey = decrypterInfo.mcKey;
			var peImage = decrypterInfo.peImage;
			var fileData = decrypterInfo.fileData;

			uint resourceRva = peHeader.GetRva(0x0E10, mcKey.ReadUInt32(0x00A0));
			uint resourceSize = peHeader.ReadUInt32(0x0E14) ^ mcKey.ReadUInt32(0x00AA);
			if (resourceRva == 0 || resourceSize == 0)
				return;
			if (resourceRva != (uint)peImage.Cor20Header.Resources.VirtualAddress ||
				resourceSize != peImage.Cor20Header.Resources.Size) {
				Logger.w("Invalid resource RVA and size found");
			}

			Logger.v("Decrypting resources @ RVA {0:X8}, {1} bytes", resourceRva, resourceSize);

			int resourceOffset = (int)peImage.RvaToOffset(resourceRva);
			for (int i = 0; i < resourceSize; i++)
				fileData[resourceOffset + i] ^= mcKey[i % 0x2000];
		}

		void DecryptStrings() {
			var peHeader = decrypterInfo.peHeader;
			var mcKey = decrypterInfo.mcKey;
			var peImage = decrypterInfo.peImage;
			var fileData = decrypterInfo.fileData;

			uint usHeapRva = peHeader.GetRva(0x0E00, mcKey.ReadUInt32(0x0078));
			uint usHeapSize = peHeader.ReadUInt32(0x0E04) ^ mcKey.ReadUInt32(0x0082);
			if (usHeapRva == 0 || usHeapSize == 0)
				return;
			var usHeap = peImage.DotNetFile.MetaData.USStream;
			if (usHeap.StartOffset == 0 ||	// Start offset is 0 if it's not present in the file
				peImage.RvaToOffset(usHeapRva) != (uint)usHeap.StartOffset ||
				usHeapSize != (uint)(usHeap.EndOffset - usHeap.StartOffset)) {
				Logger.w("Invalid #US heap RVA and size found");
			}

			Logger.v("Decrypting strings @ RVA {0:X8}, {1} bytes", usHeapRva, usHeapSize);
			Logger.Instance.Indent();

			int mcKeyOffset = 0;
			int usHeapOffset = (int)peImage.RvaToOffset(usHeapRva);
			int usHeapEnd = usHeapOffset + (int)usHeapSize;
			usHeapOffset++;
			while (usHeapOffset < usHeapEnd) {
				if (fileData[usHeapOffset] == 0 || fileData[usHeapOffset] == 1) {
					usHeapOffset++;
					continue;
				}

				int usHeapOffsetOrig = usHeapOffset;
				int stringDataLength = DeobUtils.ReadVariableLengthInt32(fileData, ref usHeapOffset);
				int usHeapOffsetString = usHeapOffset;
				int encryptedLength = stringDataLength - (usHeapOffset - usHeapOffsetOrig == 1 ? 1 : 2);
				for (int i = 0; i < encryptedLength; i++) {
					byte k = mcKey.ReadByte(mcKeyOffset++ % 0x2000);
					fileData[usHeapOffset] = Rolb((byte)(fileData[usHeapOffset] ^ k), 3);
					usHeapOffset++;
				}

				try {
					Logger.v("Decrypted string: {0}", Utils.ToCsharpString(Encoding.Unicode.GetString(fileData, usHeapOffsetString, stringDataLength - 1)));
				}
				catch {
					Logger.v("Could not decrypt string at offset {0:X8}", usHeapOffsetOrig);
				}

				usHeapOffset++;
			}

			Logger.Instance.DeIndent();
		}

		byte Rolb(byte b, int n) {
			return (byte)((b << n) | (b >> (8 - n)));
		}
	}
}
