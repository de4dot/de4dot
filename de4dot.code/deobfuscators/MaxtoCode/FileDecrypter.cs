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
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mono.MyStuff;
using de4dot.PE;

namespace de4dot.code.deobfuscators.MaxtoCode {
	// Decrypts methods and resources
	class FileDecrypter {
		MainType mainType;
		PeImage peImage;
		PeHeader peHeader;
		McKey mcKey;
		byte[] fileData;

		class PeHeader {
			const int XOR_KEY = 0x7ABF931;

			byte[] headerData;
			uint rvaDispl1;
			uint rvaDispl2;

			public PeHeader(MainType mainType, PeImage peImage) {
				headerData = getPeHeaderData(peImage);

				if (!mainType.IsOld && peImage.readUInt32(0x2008) != 0x48) {
					rvaDispl1 = readUInt32(0x0FB0) ^ XOR_KEY;
					rvaDispl2 = readUInt32(0x0FB4) ^ XOR_KEY;
				}
			}

			public uint getMcKeyRva() {
				return getRva2(0x0FFC, XOR_KEY);
			}

			public uint getRva1(int offset, uint xorKey) {
				return (readUInt32(offset) ^ xorKey) - rvaDispl1;
			}

			public uint getRva2(int offset, uint xorKey) {
				return (readUInt32(offset) ^ xorKey) - rvaDispl2;
			}

			public uint readUInt32(int offset) {
				return BitConverter.ToUInt32(headerData, offset);
			}

			static byte[] getPeHeaderData(PeImage peImage) {
				var data = new byte[0x1000];

				var firstSection = peImage.Sections[0];
				readTo(peImage, data, 0, 0, firstSection.pointerToRawData);

				foreach (var section in peImage.Sections) {
					if (section.virtualAddress >= data.Length)
						continue;
					int offset = (int)section.virtualAddress;
					readTo(peImage, data, offset, section.pointerToRawData, section.sizeOfRawData);
				}

				return data;
			}

			static void readTo(PeImage peImage, byte[] data, int destOffset, uint imageOffset, uint maxLength) {
				if (destOffset > data.Length)
					return;
				int len = Math.Min(data.Length - destOffset, (int)maxLength);
				var newData = peImage.offsetReadBytes(imageOffset, len);
				Array.Copy(newData, 0, data, destOffset, newData.Length);
			}
		}

		class McKey {
			PeHeader peHeader;
			byte[] data;

			public byte this[int index] {
				get { return data[index]; }
			}

			public McKey(PeImage peImage, PeHeader peHeader) {
				this.peHeader = peHeader;
				this.data = peImage.readBytes(peHeader.getMcKeyRva(), 0x2000);
			}

			public byte readByte(int offset) {
				return data[offset];
			}

			public uint readUInt32(int offset) {
				return BitConverter.ToUInt32(data, offset);
			}
		}

		enum EncryptionVersion {
			Unknown,
			V1,
			V2,
			V3,
			V4,
		}

		class EncryptionInfo {
			public uint MagicLo { get; set; }
			public uint MagicHi { get; set; }
			public EncryptionVersion Version { get; set; }
		}

		static EncryptionInfo[] encryptionInfos_Rva900h = new EncryptionInfo[] {
			// PE header timestamp
			// 462FA2D2 = Wed, 25 Apr 2007 18:49:54 (3.20)
			new EncryptionInfo {
				MagicLo = 0xA098B387,
				MagicHi = 0x1E8EBCA3,
				Version = EncryptionVersion.V1,
			},
			// 482384FB = Thu, 08 May 2008 22:55:55 (3.36)
			new EncryptionInfo {
				MagicLo = 0xAA98B387,
				MagicHi = 0x1E8EECA3,
				Version = EncryptionVersion.V2,
			},
			// 4A5EEC64 = Thu, 16 Jul 2009 09:01:24
			// 4C6220EC = Wed, 11 Aug 2010 04:02:52
			// 4C622357 = Wed, 11 Aug 2010 04:13:11
			new EncryptionInfo {
				MagicLo = 0xAA98B387,
				MagicHi = 0x128EECA3,
				Version = EncryptionVersion.V2,
			},
			// 4C6E4605 = Fri, 20 Aug 2010 09:08:21
			// 4D0E220D = Sun, 19 Dec 2010 15:17:33
			// 4DC2FC75 = Thu, 05 May 2011 19:37:25
			// 4DFA3D5D = Thu, 16 Jun 2011 17:29:01
			new EncryptionInfo {
				MagicLo = 0xAA98B387,
				MagicHi = 0xF28EECA3,
				Version = EncryptionVersion.V2,
			},
			// 4DC2FE0C = Thu, 05 May 2011 19:44:12
			new EncryptionInfo {
				MagicLo = 0xAA98B387,
				MagicHi = 0xF28EEAA3,
				Version = EncryptionVersion.V2,
			},
			// 4ED76740 = Thu, 01 Dec 2011 11:38:40
			// 4EE1FAD1 = Fri, 09 Dec 2011 12:10:57
			new EncryptionInfo {
				MagicLo = 0xAA983B87,
				MagicHi = 0xF28EECA3,
				Version = EncryptionVersion.V3,
			},
			// 4F832868 = Mon, Apr 09 2012 20:20:24
			new EncryptionInfo {
				MagicLo = 0xAA913B87,
				MagicHi = 0xF28EE0A3,
				Version = EncryptionVersion.V4,
			},
		};

		static EncryptionInfo[] encryptionInfos_McKey8C0h = new EncryptionInfo[] {
			// 462FA2D2 = Wed, 25 Apr 2007 18:49:54 (3.20)
			new EncryptionInfo {
				MagicLo = 0x6AA13B13,
				MagicHi = 0xD72B991F,
				Version = EncryptionVersion.V1,
			},
			// 482384FB = Thu, 08 May 2008 22:55:55 (3.36)
			new EncryptionInfo {
				MagicLo = 0x6A713B13,
				MagicHi = 0xD72B891F,
				Version = EncryptionVersion.V2,
			},
			// 4A5EEC64 = Thu, 16 Jul 2009 09:01:24
			// 4C6220EC = Wed, 11 Aug 2010 04:02:52
			// 4C622357 = Wed, 11 Aug 2010 04:13:11
			// 4C6E4605 = Fri, 20 Aug 2010 09:08:21
			// 4D0E220D = Sun, 19 Dec 2010 15:17:33
			// 4DC2FC75 = Thu, 05 May 2011 19:37:25
			// 4DC2FE0C = Thu, 05 May 2011 19:44:12
			// 4DFA3D5D = Thu, 16 Jun 2011 17:29:01
			new EncryptionInfo {
				MagicLo = 0x6A713B13,
				MagicHi = 0xD72B891F,
				Version = EncryptionVersion.V2,
			},
			// 4ED76740 = Thu, 01 Dec 2011 11:38:40
			// 4EE1FAD1 = Fri, 09 Dec 2011 12:10:57
			new EncryptionInfo {
				MagicLo = 0x6A731B13,
				MagicHi = 0xD72B891F,
				Version = EncryptionVersion.V3,
			},
			// 4F832868 = Mon, Apr 09 2012 20:20:24
			new EncryptionInfo {
				MagicLo = 0x6AD31B13,
				MagicHi = 0xD72B8A1F,
				Version = EncryptionVersion.V4,
			},
		};

		class MethodInfos {
			MainType mainType;
			PeImage peImage;
			PeHeader peHeader;
			McKey mcKey;
			uint structSize;
			uint methodInfosOffset;
			uint encryptedDataOffset;
			uint xorKey;
			Dictionary<uint, DecryptedMethodInfo> infos = new Dictionary<uint, DecryptedMethodInfo>();
			IDecrypter decrypter;
			const int ENCRYPTED_DATA_INFO_SIZE = 0x13;

			public class DecryptedMethodInfo {
				public uint bodyRva;
				public byte[] body;

				public DecryptedMethodInfo(uint bodyRva, byte[] body) {
					this.bodyRva = bodyRva;
					this.body = body;
				}
			}

			public MethodInfos(MainType mainType, PeImage peImage, PeHeader peHeader, McKey mcKey) {
				this.mainType = mainType;
				this.peImage = peImage;
				this.peHeader = peHeader;
				this.mcKey = mcKey;

				structSize = getStructSize(mcKey);

				uint methodInfosRva = peHeader.getRva2(0x0FF8, mcKey.readUInt32(0x005A));
				uint encryptedDataRva = peHeader.getRva2(0x0FF0, mcKey.readUInt32(0x0046));

				methodInfosOffset = peImage.rvaToOffset(methodInfosRva);
				encryptedDataOffset = peImage.rvaToOffset(encryptedDataRva);
			}

			static uint getStructSize(McKey mcKey) {
				uint magicLo = mcKey.readUInt32(0x8C0);
				uint magicHi = mcKey.readUInt32(0x8C4);
				foreach (var info in encryptionInfos_McKey8C0h) {
					if (magicLo == info.MagicLo && magicHi == info.MagicHi)
						return 0xC + 6 * ENCRYPTED_DATA_INFO_SIZE;
				}
				return 0xC + 3 * ENCRYPTED_DATA_INFO_SIZE;
			}

			EncryptionVersion getVersion() {
				uint m1lo = peHeader.readUInt32(0x900);
				uint m1hi = peHeader.readUInt32(0x904);
				uint m2lo = mcKey.readUInt32(0x8C0);
				uint m2hi = mcKey.readUInt32(0x8C4);

				foreach (var info in encryptionInfos_McKey8C0h) {
					if (info.MagicLo == m2lo && info.MagicHi == m2hi)
						return info.Version;
				}

				foreach (var info in encryptionInfos_Rva900h) {
					if (info.MagicLo == m1lo && info.MagicHi == m1hi)
						return info.Version;
				}

				Log.w("Could not detect MC version. Magic1: {0:X8} {1:X8}, Magic2: {2:X8} {3:X8}", m1lo, m1hi, m2lo, m2hi);
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
				MethodInfos methodInfos;
				int[] typeToMethod;

				public Decrypter(MethodInfos methodInfos, int[] typeToMethod) {
					this.methodInfos = methodInfos;
					this.typeToMethod = typeToMethod;
				}

				public byte[] decrypt(int type, byte[] encrypted) {
					if (0 <= type && type < typeToMethod.Length) {
						switch (typeToMethod[type]) {
						case 1: return methodInfos.decrypt1(encrypted);
						case 2: return methodInfos.decrypt2(encrypted);
						case 3: return methodInfos.decrypt3(encrypted);
						case 4: return methodInfos.decrypt4(encrypted);
						case 5: return methodInfos.decrypt5(encrypted);
						case 6: return methodInfos.decrypt6(encrypted);
						case 7: return methodInfos.decrypt7(encrypted);
						}
					}
					throw new ApplicationException(string.Format("Invalid encryption type: {0:X2}", type));
				}
			}

			static readonly int[] typeToTypesV1 = new int[] { -1, 1, 4, 2, 3, 5, 6, 7 };
			static readonly int[] typeToTypesV2 = new int[] { -1, 3, 2, 1, 4, 5, 6, 7 };
			static readonly int[] typeToTypesV3 = new int[] { -1, 1, 2, 3, 4, 5, 6, 7 };
			static readonly int[] typeToTypesV4 = new int[] { -1, 2, 1, 3, 4, 5, 6, 7 };
			void initializeDecrypter() {
				switch (getVersion()) {
				case EncryptionVersion.V1: decrypter = new Decrypter(this, typeToTypesV1); break;
				case EncryptionVersion.V2: decrypter = new Decrypter(this, typeToTypesV2); break;
				case EncryptionVersion.V3: decrypter = new Decrypter(this, typeToTypesV3); break;
				case EncryptionVersion.V4: decrypter = new Decrypter(this, typeToTypesV4); break;

				case EncryptionVersion.Unknown:
				default:
					throw new ApplicationException("Unknown MC version");
				}
			}

			public void initializeInfos() {
				initializeDecrypter();

				int numMethods = readInt32(0) ^ readInt32(4);
				if (numMethods < 0)
					throw new ApplicationException("Invalid number of encrypted methods");

				xorKey = (uint)numMethods;
				uint rvaDispl = !mainType.IsOld && peImage.readUInt32(0x2008) != 0x48 ? 0x1000U : 0;
				int numEncryptedDataInfos = ((int)structSize - 0xC) / ENCRYPTED_DATA_INFO_SIZE;
				var encryptedDataInfos = new byte[numEncryptedDataInfos][];

				uint offset = 8;
				for (int i = 0; i < numMethods; i++, offset += structSize) {
					uint methodBodyRva = readEncryptedUInt32(offset) - rvaDispl;
					uint totalSize = readEncryptedUInt32(offset + 4);
					uint methodInstructionRva = readEncryptedUInt32(offset + 8) - rvaDispl;

					var decryptedData = new byte[totalSize];

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
							encryptedDataInfos[j] = decrypt(encryptionType, dataOffset, encryptedSize, realSize);
					}

					int copyOffset = 0;
					copyOffset = copyData(decryptedData, encryptedDataInfos[0], copyOffset);
					for (int j = 2; j < encryptedDataInfos.Length; j++)
						copyOffset = copyData(decryptedData, encryptedDataInfos[j], copyOffset);
					copyData(decryptedData, encryptedDataInfos[1], exOffset); // Exceptions or padding

					var info = new DecryptedMethodInfo(methodBodyRva, decryptedData);
					infos[info.bodyRva] = info;
				}
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

			byte[] decrypt(int type, uint dataOffset, uint encryptedSize, uint realSize) {
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

			byte[] decrypt1(byte[] encrypted) {
				var decrypted = new byte[encrypted.Length];
				for (int i = 0; i < decrypted.Length; i++)
					decrypted[i] = (byte)(encrypted[i] ^ mcKey.readByte(i % 0x2000));
				return decrypted;
			}

			byte[] decrypt2(byte[] encrypted) {
				if ((encrypted.Length & 7) != 0)
					throw new ApplicationException("Invalid encryption #2 length");
				const int offset = 0x00FA;
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

			static byte[] decrypt3Shifts = new byte[16] { 5, 11, 14, 21, 6, 20, 17, 29, 4, 10, 3, 2, 7, 1, 26, 18 };
			byte[] decrypt3(byte[] encrypted) {
				if ((encrypted.Length & 7) != 0)
					throw new ApplicationException("Invalid encryption #3 length");
				const int offset = 0x015E;
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

			byte[] decrypt4(byte[] encrypted) {
				var decrypted = new byte[encrypted.Length / 3 * 2 + 1];

				int count = encrypted.Length / 3;
				int i = 0, j = 0, k = 0;
				while (count-- > 0) {
					byte k1 = mcKey.readByte(j + 1);
					byte k2 = mcKey.readByte(j + 2);
					byte k3 = mcKey.readByte(j + 3);
					decrypted[k++] = (byte)(((encrypted[i + 1] ^ k2) >> 4) | ((encrypted[i] ^ k1) & 0xF0));
					decrypted[k++] = (byte)(((encrypted[i + 1] ^ k2) << 4) + ((encrypted[i + 2] ^ k3) & 0x0F));
					i += 3;
					j = (j + 4) % 0x2000;
				}

				if ((encrypted.Length % 3) != 0)
					decrypted[k] = (byte)(encrypted[i] ^ mcKey.readByte(j));

				return decrypted;
			}

			byte[] decrypt5(byte[] encrypted) {
				throw new NotImplementedException("Encryption type #5 not implemented yet");
			}

			byte[] decrypt6(byte[] encrypted) {
				throw new NotImplementedException("Encryption type #6 not implemented yet");
			}

			byte[] decrypt7(byte[] encrypted) {
				throw new NotImplementedException("Encryption type #7 not implemented yet");
			}
		}

		public FileDecrypter(MainType mainType) {
			this.mainType = mainType;
		}

		public bool decrypt(byte[] fileData, ref DumpedMethods dumpedMethods) {
			peImage = new PeImage(fileData);
			peHeader = new PeHeader(mainType, peImage);
			mcKey = new McKey(peImage, peHeader);
			this.fileData = fileData;

			dumpedMethods = decryptMethods();
			if (dumpedMethods == null)
				return false;

			decryptResources();
			decryptStrings();

			return true;
		}

		DumpedMethods decryptMethods() {
			var dumpedMethods = new DumpedMethods();

			var methodInfos = new MethodInfos(mainType, peImage, peHeader, mcKey);
			methodInfos.initializeInfos();

			var metadataTables = peImage.Cor20Header.createMetadataTables();
			var methodDef = metadataTables.getMetadataType(MetadataIndex.iMethodDef);
			uint methodDefOffset = methodDef.fileOffset;
			for (int i = 0; i < methodDef.rows; i++, methodDefOffset += methodDef.totalSize) {
				uint bodyRva = peImage.offsetReadUInt32(methodDefOffset);
				if (bodyRva == 0)
					continue;

				var info = methodInfos.lookup(bodyRva);
				if (info == null)
					continue;

				uint bodyOffset = peImage.rvaToOffset(bodyRva);
				ushort magic = peImage.offsetReadUInt16(bodyOffset);
				if (magic != 0xFFF3)
					continue;

				var dm = new DumpedMethod();
				dm.token = (uint)(0x06000001 + i);
				dm.mdImplFlags = peImage.offsetReadUInt16(methodDefOffset + (uint)methodDef.fields[1].offset);
				dm.mdFlags = peImage.offsetReadUInt16(methodDefOffset + (uint)methodDef.fields[2].offset);
				dm.mdName = peImage.offsetRead(methodDefOffset + (uint)methodDef.fields[3].offset, methodDef.fields[3].size);
				dm.mdSignature = peImage.offsetRead(methodDefOffset + (uint)methodDef.fields[4].offset, methodDef.fields[4].size);
				dm.mdParamList = peImage.offsetRead(methodDefOffset + (uint)methodDef.fields[5].offset, methodDef.fields[5].size);

				var reader = new BinaryReader(new MemoryStream(info.body));
				byte b = reader.ReadByte();
				if ((b & 3) == 2) {
					dm.mhFlags = 2;
					dm.mhMaxStack = 8;
					dm.mhCodeSize = (uint)(b >> 2);
					dm.mhLocalVarSigTok = 0;
				}
				else {
					reader.BaseStream.Position--;
					dm.mhFlags = reader.ReadUInt16();
					dm.mhMaxStack = reader.ReadUInt16();
					dm.mhCodeSize = reader.ReadUInt32();
					dm.mhLocalVarSigTok = reader.ReadUInt32();
					uint codeOffset = (uint)(dm.mhFlags >> 12) * 4;
					reader.BaseStream.Position += codeOffset - 12;
				}

				dm.code = reader.ReadBytes((int)dm.mhCodeSize);
				if ((dm.mhFlags & 8) != 0) {
					reader.BaseStream.Position = (reader.BaseStream.Position + 3) & ~3;
					dm.extraSections = reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position));
				}

				dumpedMethods.add(dm);
			}

			return dumpedMethods;
		}

		void decryptResources() {
			uint resourceRva = peHeader.getRva1(0x0E10, mcKey.readUInt32(0x00A0));
			uint resourceSize = peHeader.readUInt32(0x0E14) ^ mcKey.readUInt32(0x00AA);
			if (resourceRva == 0 || resourceSize == 0)
				return;
			if (resourceRva != peImage.Cor20Header.resources.virtualAddress ||
				resourceSize != peImage.Cor20Header.resources.size) {
				Log.w("Invalid resource RVA and size found");
			}

			Log.v("Decrypting resources @ RVA {0:X8}, {1} bytes", resourceRva, resourceSize);

			int resourceOffset = (int)peImage.rvaToOffset(resourceRva);
			for (int i = 0; i < resourceSize; i++)
				fileData[resourceOffset + i] ^= mcKey[i % 0x2000];
		}

		void decryptStrings() {
			uint usHeapRva = peHeader.getRva1(0x0E00, mcKey.readUInt32(0x0078));
			uint usHeapSize = peHeader.readUInt32(0x0E04) ^ mcKey.readUInt32(0x0082);
			if (usHeapRva == 0 || usHeapSize == 0)
				return;
			var usHeap = peImage.Cor20Header.metadata.getStream("#US");
			if (usHeap == null ||
				peImage.rvaToOffset(usHeapRva) != usHeap.fileOffset ||
				usHeapSize != usHeap.Length) {
				Log.w("Invalid #US heap RVA and size found");
			}

			Log.v("Decrypting strings @ RVA {0:X8}, {1} bytes", usHeapRva, usHeapSize);
			Log.indent();

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
					Log.v("Decrypted string: {0}", Utils.toCsharpString(Encoding.Unicode.GetString(fileData, usHeapOffsetString, stringDataLength - 1)));
				}
				catch {
					Log.v("Could not decrypt string at offset {0:X8}", usHeapOffsetOrig);
				}

				usHeapOffset++;
			}

			Log.deIndent();
		}

		byte rolb(byte b, int n) {
			return (byte)((b << n) | (b >> (8 - n)));
		}
	}
}
