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
using ICSharpCode.SharpZipLib.Zip.Compression;
using dnlib.PE;
using dnlib.IO;
using dnlib.DotNet;

namespace de4dot.code.deobfuscators.dotNET_Reactor.v4 {
	class NativeImageUnpacker {
		MyPEImage peImage;
		bool isNet1x;
		const int loaderHeaderSizeV45 = 14;

		public NativeImageUnpacker(IPEImage peImage) {
			this.peImage = new MyPEImage(peImage);
		}

		public byte[] Unpack() {
			if (peImage.PEImage.Win32Resources == null)
				return null;
			var dataEntry = peImage.PEImage.Win32Resources.Find(10, "__", 0);
			if (dataEntry == null)
				return null;

			var encryptedData = dataEntry.Data.ReadAllBytes();

			var keyData = GetKeyData();
			if (keyData == null)
				return null;
			var decrypter = new NativeFileDecrypter(keyData);
			decrypter.Decrypt(encryptedData, 0, encryptedData.Length);

			byte[] inflatedData;
			if (isNet1x)
				inflatedData = DeobUtils.Inflate(encryptedData, false);
			else {
				int inflatedSize = BitConverter.ToInt32(encryptedData, 0);
				inflatedData = new byte[inflatedSize];
				var inflater = new Inflater(false);
				inflater.SetInput(encryptedData, 4, encryptedData.Length - 4);
				int count = inflater.Inflate(inflatedData);
				if (count != inflatedSize)
					return null;
			}

			// CLR 1.x or DNR v4.0 - v4.4
			if (BitConverter.ToInt16(inflatedData, 0) == 0x5A4D)
				return inflatedData;

			// DNR v4.5
			if (BitConverter.ToInt16(inflatedData, loaderHeaderSizeV45) == 0x5A4D)
				return UnpackLoader(inflatedData);

			return null;
		}

		static byte[] UnpackLoader(byte[] loaderData) {
			var loaderBytes = new byte[loaderData.Length - loaderHeaderSizeV45];
			Array.Copy(loaderData, loaderHeaderSizeV45, loaderBytes, 0, loaderBytes.Length);

			try {
				using (var asmLoader = ModuleDefMD.Load(loaderBytes)) {
					if (asmLoader.Resources.Count == 0)
						return null;
					var resource = asmLoader.Resources[0] as EmbeddedResource;
					if (resource == null)
						return null;

					return resource.Data.ReadAllBytes();
				}
			}
			catch {
				return null;
			}
		}

		static readonly uint[] baseOffsets = new uint[] {
			0x1C00,	// DNR 4.0 & 4.1
			0x1900,	// DNR 4.2.7.5
			0x1B60,	// DNR 4.2.8.4, 4.3, 4.4, 4.5
			0x700,	// DNR 4.5.0.0
		};
		static readonly short[] decryptMethodPattern = new short[] {
			/* 00 */	0x83, 0xEC, 0x38,		// sub     esp, 38h
			/* 03 */	0x53,					// push    ebx
			/* 04 */	0xB0, -1,				// mov     al, ??h
			/* 06 */	0x88, 0x44, 0x24, 0x2B,	// mov     [esp+2Bh], al
			/* 0A */	0x88, 0x44, 0x24, 0x2F,	// mov     [esp+2Fh], al
			/* 0E */	0xB0, -1,				// mov     al, ??h
			/* 10 */	0x88, 0x44, 0x24, 0x30,	// mov     [esp+30h], al
			/* 14 */	0x88, 0x44, 0x24, 0x31,	// mov     [esp+31h], al
			/* 18 */	0x88, 0x44, 0x24, 0x33,	// mov     [esp+33h], al
			/* 1C */	0x55,					// push    ebp
			/* 1D */	0x56,					// push    esi
		};
		static readonly short[] startMethodNet1xPattern = new short[] {
			/* 00 */ 0x55,						// push    ebp
			/* 01 */ 0x8B, 0xEC,				// mov     ebp, esp
			/* 03 */ 0xB9, 0x14, 0x00, 0x00, 0x00, // mov  ecx, 14h
			/* 08 */ 0x6A, 0x00,				// push    0
			/* 0A */ 0x6A, 0x00,				// push    0
			/* 0C */ 0x49,						// dec     ecx
			/* 0D */ 0x75, 0xF9,				// jnz     short $-5
			/* 0F */ 0x53,						// push    ebx
			/* 10 */ 0x56,						// push    esi
			/* 11 */ 0x57,						// push    edi
			/* 12 */ 0xB8, -1, -1, -1, -1,		// mov     eax, offset XXXXXXXX
			/* 17 */ 0xE8, -1, -1, -1, -1,		// call    YYYYYYYY
		};
		byte[] GetKeyData() {
			isNet1x = false;
			for (int i = 0; i < baseOffsets.Length; i++) {
				var code = peImage.OffsetReadBytes(baseOffsets[i], decryptMethodPattern.Length);
				if (DeobUtils.IsCode(decryptMethodPattern, code))
					return GetKeyData(baseOffsets[i]);
			}

			var net1xCode = peImage.OffsetReadBytes(0x207E0, startMethodNet1xPattern.Length);
			if (DeobUtils.IsCode(startMethodNet1xPattern, net1xCode)) {
				isNet1x = true;
				return new byte[6] { 0x34, 0x38, 0x63, 0x65, 0x7A, 0x35 };
			}

			return null;
		}

		byte[] GetKeyData(uint baseOffset) {
			return new byte[6] {
				peImage.OffsetReadByte(baseOffset + 5),
				peImage.OffsetReadByte(baseOffset + 0xF),
				peImage.OffsetReadByte(baseOffset + 0x58),
				peImage.OffsetReadByte(baseOffset + 0x6D),
				peImage.OffsetReadByte(baseOffset + 0x98),
				peImage.OffsetReadByte(baseOffset + 0xA6),
			};
		}
	}
}
