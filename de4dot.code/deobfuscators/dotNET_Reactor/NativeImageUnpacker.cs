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
using ICSharpCode.SharpZipLib.Zip.Compression;
using de4dot.PE;

namespace de4dot.deobfuscators.dotNET_Reactor {
	class NativeImageUnpacker {
		PeImage peImage;

		public NativeImageUnpacker(PeImage peImage) {
			this.peImage = peImage;
		}

		public byte[] unpack() {
			var resources = peImage.Resources;
			var dir = resources.getRoot();
			if ((dir = dir.getDirectory(10)) == null)
				return null;
			if ((dir = dir.getDirectory("__")) == null)
				return null;
			var dataEntry = dir.getData(0);
			if (dataEntry == null)
				return null;

			var encryptedData = peImage.readBytes(dataEntry.RVA, (int)dataEntry.Size);
			if (encryptedData.Length != dataEntry.Size)
				return null;

			var keyData = getKeyData();
			if (keyData == null)
				return null;
			var decrypter = new NativeFileDecrypter(keyData);
			decrypter.decrypt(encryptedData, 0, encryptedData.Length);

			int inflatedSize = BitConverter.ToInt32(encryptedData, 0);
			var inflater = new Inflater();
			byte[] inflatedData = new byte[inflatedSize];
			inflater.SetInput(encryptedData, 4, encryptedData.Length - 4);
			int count = inflater.Inflate(inflatedData);
			if (count != inflatedSize)
				return null;

			if (BitConverter.ToInt16(inflatedData, 0) != 0x5A4D)
				return null;

			return inflatedData;
		}

		static uint[] baseOffsets = new uint[] {
			0x1C00,	// DNR 4.0 & 4.1
			0x1900,	// DNR 4.2.7.5
			0x1B60,	// DNR 4.3 & 4.4
		};
		static short[] decryptMethodPattern = new short[] {
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
		byte[] getKeyData() {
			for (int i = 0; i < baseOffsets.Length; i++) {
				var code = peImage.offsetReadBytes(baseOffsets[i], decryptMethodPattern.Length);
				if (DeobUtils.isCode(decryptMethodPattern, code))
					return getKeyData(baseOffsets[i]);
			}

			//TODO: Check if .NET 1.1 since it uses a hard coded key

			return null;
		}

		byte[] getKeyData(uint baseOffset) {
			return new byte[6] {
				peImage.offsetReadByte(baseOffset + 5),
				peImage.offsetReadByte(baseOffset + 0xF),
				peImage.offsetReadByte(baseOffset + 0x58),
				peImage.offsetReadByte(baseOffset + 0x6D),
				peImage.offsetReadByte(baseOffset + 0x98),
				peImage.offsetReadByte(baseOffset + 0xA6),
			};
		}
	}
}
