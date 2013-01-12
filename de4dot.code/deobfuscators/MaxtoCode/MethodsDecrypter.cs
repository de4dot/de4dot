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

			delegate byte[] Decrypt(byte[] encrypted);
			readonly Decrypt[] decryptHandlersV1;
			readonly Decrypt[] decryptHandlersV2;
			readonly Decrypt[] decryptHandlersV3;
			readonly Decrypt[] decryptHandlersV4;
			readonly Decrypt[] decryptHandlersV5a;
			readonly Decrypt[] decryptHandlersV5b;
			readonly Decrypt[] decryptHandlersV5c;
			readonly Decrypt[] decryptHandlersV6a;

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

				decryptHandlersV1 = new Decrypt[] { decrypt1a, decrypt4a, decrypt2a, decrypt3a, decrypt5, decrypt6, decrypt7 };
				decryptHandlersV2 = new Decrypt[] { decrypt3a, decrypt2a, decrypt1a, decrypt4a, decrypt5, decrypt6, decrypt7 };
				decryptHandlersV3 = new Decrypt[] { decrypt1a, decrypt2a, decrypt3a, decrypt4a, decrypt5, decrypt6, decrypt7 };
				decryptHandlersV4 = new Decrypt[] { decrypt2a, decrypt1a, decrypt3a, decrypt4a, decrypt5, decrypt6, decrypt7 };
				decryptHandlersV5a = new Decrypt[] { decrypt4a, decrypt2a, decrypt3a, decrypt1a, decrypt5, decrypt6, decrypt7 };
				decryptHandlersV5b = new Decrypt[] { decrypt4b, decrypt2b, decrypt3b, decrypt1b, decrypt6, decrypt7, decrypt5 };
				decryptHandlersV5c = new Decrypt[] { decrypt4c, decrypt2c, decrypt3c, decrypt1c, decrypt6, decrypt7, decrypt5 };
				decryptHandlersV6a = new Decrypt[] { decrypt4d, decrypt2d, decrypt3d, decrypt1d, decrypt6, decrypt7, decrypt5 };

				structSize = getStructSize(mcKey);

				uint methodInfosRva = peHeader.getRva(0x0FF8, mcKey.readUInt32(0x005A));
				uint encryptedDataRva = peHeader.getRva(0x0FF0, mcKey.readUInt32(0x0046));

				methodInfosOffset = peImage.rvaToOffset(methodInfosRva);
				encryptedDataOffset = peImage.rvaToOffset(encryptedDataRva);
			}

			static uint getStructSize(McKey mcKey) {
				uint magicLo = mcKey.readUInt32(0x8C0);
				uint magicHi = mcKey.readUInt32(0x8C4);
				foreach (var info in EncryptionInfos.McKey8C0h) {
					if (magicLo == info.MagicLo && magicHi == info.MagicHi)
						return 0xC + 6 * ENCRYPTED_DATA_INFO_SIZE;
				}
				return 0xC + 3 * ENCRYPTED_DATA_INFO_SIZE;
			}

			EncryptionVersion getVersion() {
				if (peHeader.EncryptionVersion != EncryptionVersion.Unknown)
					return peHeader.EncryptionVersion;

				uint m2lo = mcKey.readUInt32(0x8C0);
				uint m2hi = mcKey.readUInt32(0x8C4);

				foreach (var info in EncryptionInfos.McKey8C0h) {
					if (info.MagicLo == m2lo && info.MagicHi == m2hi)
						return info.Version;
				}

				Logger.w("Could not detect MC version. Magic2: {0:X8} {1:X8}", m2lo, m2hi);
				return EncryptionVersion.Unknown;
			}

			public DecryptedMethodInfo lookup(uint bodyRva) {
				DecryptedMethodInfo info;
				infos.TryGetValue(bodyRva, out info);
				return info;
			}

			byte readByte(uint offset) {
				return peImage.offsetReadByte(methodInfosOffset + offset);
			}

			short readInt16(uint offset) {
				return (short)peImage.offsetReadUInt16(methodInfosOffset + offset);
			}

			uint readUInt32(uint offset) {
				return peImage.offsetReadUInt32(methodInfosOffset + offset);
			}

			int readInt32(uint offset) {
				return (int)readUInt32(offset);
			}

			short readEncryptedInt16(uint offset) {
				return (short)(readInt16(offset) ^ xorKey);
			}

			int readEncryptedInt32(uint offset) {
				return (int)readEncryptedUInt32(offset);
			}

			uint readEncryptedUInt32(uint offset) {
				return readUInt32(offset) ^ xorKey;
			}

			interface IDecrypter {
				byte[] decrypt(int type, byte[] encrypted);
			}

			class Decrypter : IDecrypter {
				Decrypt[] decrypterHandlers;

				public Decrypter(Decrypt[] decrypterHandlers) {
					this.decrypterHandlers = decrypterHandlers;
				}

				public byte[] decrypt(int type, byte[] encrypted) {
					if (1 <= type && type <= decrypterHandlers.Length)
						return decrypterHandlers[type - 1](encrypted);
					throw new ApplicationException(string.Format("Invalid encryption type: {0:X2}", type));
				}
			}

			void initializeDecrypter() {
				switch (getVersion()) {
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

			public void initializeInfos() {
				initializeDecrypter();
				if (!initializeInfos2())
					throw new ApplicationException("Could not decrypt methods");
			}

			bool initializeInfos2() {
				foreach (var decrypter in decrypters) {
					try {
						if (initializeInfos2(decrypter))
							return true;
					}
					catch {
					}
				}
				return false;
			}

			bool initializeInfos2(IDecrypter decrypter) {
				int numMethods = readInt32(0) ^ readInt32(4);
				if (numMethods < 0)
					throw new ApplicationException("Invalid number of encrypted methods");

				xorKey = (uint)numMethods;
				int numEncryptedDataInfos = ((int)structSize - 0xC) / ENCRYPTED_DATA_INFO_SIZE;
				var encryptedDataInfos = new byte[numEncryptedDataInfos][];

				uint offset = 8;
				for (int i = 0; i < numMethods; i++, offset += structSize) {
					uint methodBodyRva = readEncryptedUInt32(offset);
					uint totalSize = readEncryptedUInt32(offset + 4);
					uint methodInstructionRva = readEncryptedUInt32(offset + 8);

					// Read the method body header and method body (instrs + exception handlers).
					// The method body header is always in the first one. The instrs + ex handlers
					// are always in the last 4, and evenly divided (each byte[] is totalLen / 4).
					// The 2nd one is for the exceptions (or padding), but it may be null.
					uint offset2 = offset + 0xC;
					int exOffset = 0;
					for (int j = 0; j < encryptedDataInfos.Length; j++, offset2 += ENCRYPTED_DATA_INFO_SIZE) {
						// readByte(offset2); <-- index
						int encryptionType = readEncryptedInt16(offset2 + 1);
						uint dataOffset = readEncryptedUInt32(offset2 + 3);
						uint encryptedSize = readEncryptedUInt32(offset2 + 7);
						uint realSize = readEncryptedUInt32(offset2 + 11);
						if (j == 1)
							exOffset = readEncryptedInt32(offset2 + 15);
						if (j == 1 && exOffset == 0)
							encryptedDataInfos[j] = null;
						else
							encryptedDataInfos[j] = decrypt(decrypter, encryptionType, dataOffset, encryptedSize, realSize);
					}

					var decryptedData = new byte[totalSize];
					int copyOffset = 0;
					copyOffset = copyData(decryptedData, encryptedDataInfos[0], copyOffset);
					for (int j = 2; j < encryptedDataInfos.Length; j++)
						copyOffset = copyData(decryptedData, encryptedDataInfos[j], copyOffset);
					copyData(decryptedData, encryptedDataInfos[1], exOffset); // Exceptions or padding

					if (!MethodBodyParser.verify(decryptedData))
						throw new InvalidMethodBody();

					var info = new DecryptedMethodInfo(methodBodyRva, decryptedData);
					infos[info.bodyRva] = info;
				}

				return true;
			}

			static int copyData(byte[] dest, byte[] source, int offset) {
				if (source == null)
					return offset;
				Array.Copy(source, 0, dest, offset, source.Length);
				return offset + source.Length;
			}

			byte[] readData(uint offset, int size) {
				return peImage.offsetReadBytes(encryptedDataOffset + offset, size);
			}

			byte[] decrypt(IDecrypter decrypter, int type, uint dataOffset, uint encryptedSize, uint realSize) {
				if (realSize == 0)
					return null;
				if (realSize > encryptedSize)
					throw new ApplicationException("Invalid realSize");

				var encrypted = readData(dataOffset, (int)encryptedSize);
				var decrypted = decrypter.decrypt(type, encrypted);
				if (realSize > decrypted.Length)
					throw new ApplicationException("Invalid decrypted length");
				Array.Resize(ref decrypted, (int)realSize);
				return decrypted;
			}

			byte[] decrypt1a(byte[] encrypted) {
				return decrypt1(encrypted, 0, 0, 0x2000);
			}

			byte[] decrypt1b(byte[] encrypted) {
				return decrypt1(encrypted, 6, 6, 0x500);
			}

			byte[] decrypt1c(byte[] encrypted) {
				return decrypt1(encrypted, 6, 0, 0x1000);
			}

			byte[] decrypt1d(byte[] encrypted) {
				return decrypt1(encrypted, 5, 5, 0x500);
			}

			byte[] decrypt1(byte[] encrypted, int keyStart, int keyReset, int keyEnd) {
				var decrypted = new byte[encrypted.Length];
				for (int i = 0, ki = keyStart; i < decrypted.Length; i++) {
					decrypted[i] = (byte)(encrypted[i] ^ mcKey.readByte(ki));
					if (++ki == keyEnd)
						ki = keyReset;
				}
				return decrypted;
			}

			byte[] decrypt2a(byte[] encrypted) {
				return decrypt2(encrypted, 0x00FA);
			}

			byte[] decrypt2b(byte[] encrypted) {
				return decrypt2(encrypted, 0x00FA + 9);
			}

			byte[] decrypt2c(byte[] encrypted) {
				return decrypt2(encrypted, 0x00FA + 0x24);
			}

			byte[] decrypt2d(byte[] encrypted) {
				return decrypt2(encrypted, 0x00FA + 7);
			}

			byte[] decrypt2(byte[] encrypted, int offset) {
				if ((encrypted.Length & 7) != 0)
					throw new ApplicationException("Invalid encryption #2 length");
				uint key4 = mcKey.readUInt32(offset + 4 * 4);
				uint key5 = mcKey.readUInt32(offset + 5 * 4);

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

			byte[] decrypt3a(byte[] encrypted) {
				return decrypt3(encrypted, 0x015E);
			}

			byte[] decrypt3b(byte[] encrypted) {
				return decrypt3(encrypted, 0x015E + 0xE5);
			}

			byte[] decrypt3c(byte[] encrypted) {
				return decrypt3(encrypted, 0x015E + 0x28);
			}

			byte[] decrypt3d(byte[] encrypted) {
				return decrypt3(encrypted, 0x015E + 8);
			}

			static readonly byte[] decrypt3Shifts = new byte[16] { 5, 11, 14, 21, 6, 20, 17, 29, 4, 10, 3, 2, 7, 1, 26, 18 };
			byte[] decrypt3(byte[] encrypted, int offset) {
				if ((encrypted.Length & 7) != 0)
					throw new ApplicationException("Invalid encryption #3 length");
				uint key0 = mcKey.readUInt32(offset + 0 * 4);
				uint key3 = mcKey.readUInt32(offset + 3 * 4);

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

			byte[] decrypt4a(byte[] encrypted) {
				return decrypt4(encrypted, 0, 0, 0x2000);
			}

			byte[] decrypt4b(byte[] encrypted) {
				return decrypt4(encrypted, 0x14, 0x14, 0x1000);
			}

			byte[] decrypt4c(byte[] encrypted) {
				return decrypt4(encrypted, 5, 0, 0x2000);
			}

			byte[] decrypt4d(byte[] encrypted) {
				return decrypt4(encrypted, 0x0B, 0x0B, 0x1000);
			}

			byte[] decrypt4(byte[] encrypted, int keyStart, int keyReset, int keyEnd) {
				var decrypted = new byte[encrypted.Length / 3 * 2 + 1];

				int count = encrypted.Length / 3;
				int i = 0, ki = keyStart, j = 0;
				while (count-- > 0) {
					byte k1 = mcKey.readByte(ki + 1);
					byte k2 = mcKey.readByte(ki + 2);
					byte k3 = mcKey.readByte(ki + 3);
					decrypted[j++] = (byte)(((encrypted[i + 1] ^ k2) >> 4) | ((encrypted[i] ^ k1) & 0xF0));
					decrypted[j++] = (byte)(((encrypted[i + 1] ^ k2) << 4) + ((encrypted[i + 2] ^ k3) & 0x0F));
					i += 3;
					ki += 4;
					if (ki >= keyEnd)
						ki = keyReset;
				}

				if ((encrypted.Length % 3) != 0)
					decrypted[j] = (byte)(encrypted[i] ^ mcKey.readByte(ki));

				return decrypted;
			}

			byte[] decrypt5(byte[] encrypted) {
				return CryptDecrypter.decrypt(mcKey.readBytes(0x0032, 15), encrypted);
			}

			byte[] decrypt6(byte[] encrypted) {
				return Decrypter6.decrypt(mcKey.readBytes(0x0096, 32), encrypted);
			}

			byte[] decrypt7(byte[] encrypted) {
				var decrypted = (byte[])encrypted.Clone();
				new Blowfish(getBlowfishKey()).decrypt_LE(decrypted);
				return decrypted;
			}

			byte[] blowfishKey;
			byte[] getBlowfishKey() {
				if (blowfishKey != null)
					return blowfishKey;
				var key = new byte[100];
				int i;
				for (i = 0; i < key.Length; i++) {
					byte b = mcKey.readByte(i);
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

		public bool decrypt(ref DumpedMethods dumpedMethods) {
			dumpedMethods = decryptMethods();
			if (dumpedMethods == null)
				return false;

			decryptResources();
			decryptStrings();

			return true;
		}

		DumpedMethods decryptMethods() {
			var dumpedMethods = new DumpedMethods();

			var peImage = decrypterInfo.peImage;
			var methodInfos = new MethodInfos(decrypterInfo.mainType, peImage, decrypterInfo.peHeader, decrypterInfo.mcKey);
			methodInfos.initializeInfos();

			var methodDef = peImage.DotNetFile.MetaData.TablesStream.MethodTable;
			for (uint rid = 1; rid <= methodDef.Rows; rid++) {
				var dm = new DumpedMethod();
				peImage.readMethodTableRowTo(dm, rid);

				var info = methodInfos.lookup(dm.mdRVA);
				if (info == null)
					continue;

				ushort magic = peImage.readUInt16(dm.mdRVA);
				if (magic != 0xFFF3)
					continue;

				var mbHeader = MethodBodyParser.parseMethodBody(MemoryImageStream.Create(info.body), out dm.code, out dm.extraSections);
				peImage.updateMethodHeaderInfo(dm, mbHeader);

				dumpedMethods.add(dm);
			}

			return dumpedMethods;
		}

		void decryptResources() {
			var peHeader = decrypterInfo.peHeader;
			var mcKey = decrypterInfo.mcKey;
			var peImage = decrypterInfo.peImage;
			var fileData = decrypterInfo.fileData;

			uint resourceRva = peHeader.getRva(0x0E10, mcKey.readUInt32(0x00A0));
			uint resourceSize = peHeader.readUInt32(0x0E14) ^ mcKey.readUInt32(0x00AA);
			if (resourceRva == 0 || resourceSize == 0)
				return;
			if (resourceRva != (uint)peImage.Cor20Header.Resources.VirtualAddress ||
				resourceSize != peImage.Cor20Header.Resources.Size) {
				Logger.w("Invalid resource RVA and size found");
			}

			Logger.v("Decrypting resources @ RVA {0:X8}, {1} bytes", resourceRva, resourceSize);

			int resourceOffset = (int)peImage.rvaToOffset(resourceRva);
			for (int i = 0; i < resourceSize; i++)
				fileData[resourceOffset + i] ^= mcKey[i % 0x2000];
		}

		void decryptStrings() {
			var peHeader = decrypterInfo.peHeader;
			var mcKey = decrypterInfo.mcKey;
			var peImage = decrypterInfo.peImage;
			var fileData = decrypterInfo.fileData;

			uint usHeapRva = peHeader.getRva(0x0E00, mcKey.readUInt32(0x0078));
			uint usHeapSize = peHeader.readUInt32(0x0E04) ^ mcKey.readUInt32(0x0082);
			if (usHeapRva == 0 || usHeapSize == 0)
				return;
			var usHeap = peImage.DotNetFile.MetaData.USStream;
			if (usHeap.StartOffset == 0 ||	// Start offset is 0 if it's not present in the file
				peImage.rvaToOffset(usHeapRva) != (uint)usHeap.StartOffset ||
				usHeapSize != (uint)(usHeap.EndOffset - usHeap.StartOffset)) {
				Logger.w("Invalid #US heap RVA and size found");
			}

			Logger.v("Decrypting strings @ RVA {0:X8}, {1} bytes", usHeapRva, usHeapSize);
			Logger.Instance.indent();

			int mcKeyOffset = 0;
			int usHeapOffset = (int)peImage.rvaToOffset(usHeapRva);
			int usHeapEnd = usHeapOffset + (int)usHeapSize;
			usHeapOffset++;
			while (usHeapOffset < usHeapEnd) {
				if (fileData[usHeapOffset] == 0 || fileData[usHeapOffset] == 1) {
					usHeapOffset++;
					continue;
				}

				int usHeapOffsetOrig = usHeapOffset;
				int stringDataLength = DeobUtils.readVariableLengthInt32(fileData, ref usHeapOffset);
				int usHeapOffsetString = usHeapOffset;
				int encryptedLength = stringDataLength - (usHeapOffset - usHeapOffsetOrig == 1 ? 1 : 2);
				for (int i = 0; i < encryptedLength; i++) {
					byte k = mcKey.readByte(mcKeyOffset++ % 0x2000);
					fileData[usHeapOffset] = rolb((byte)(fileData[usHeapOffset] ^ k), 3);
					usHeapOffset++;
				}

				try {
					Logger.v("Decrypted string: {0}", Utils.toCsharpString(Encoding.Unicode.GetString(fileData, usHeapOffsetString, stringDataLength - 1)));
				}
				catch {
					Logger.v("Could not decrypt string at offset {0:X8}", usHeapOffsetOrig);
				}

				usHeapOffset++;
			}

			Logger.Instance.deIndent();
		}

		byte rolb(byte b, int n) {
			return (byte)((b << n) | (b >> (8 - n)));
		}
	}
}
