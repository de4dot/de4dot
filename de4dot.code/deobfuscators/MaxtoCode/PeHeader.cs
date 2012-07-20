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
using de4dot.PE;

namespace de4dot.code.deobfuscators.MaxtoCode {
	enum EncryptionVersion {
		Unknown,
		V1,
		V2,
		V3,
		V4,
		V5,
	}

	class PeHeader {
		const int XOR_KEY = 0x7ABF931;

		EncryptionVersion version;
		byte[] headerData;

		public EncryptionVersion EncryptionVersion {
			get { return version; }
		}

		public PeHeader(MainType mainType, PeImage peImage) {
			uint headerOffset;
			version = getHeaderOffsetAndVersion(peImage, out headerOffset);
			headerData = peImage.offsetReadBytes(headerOffset, 0x1000);
		}

		public uint getMcKeyRva() {
			return getRva(0x0FFC, XOR_KEY);
		}

		public uint getRva(int offset, uint xorKey) {
			return (readUInt32(offset) ^ xorKey);
		}

		public uint readUInt32(int offset) {
			return BitConverter.ToUInt32(headerData, offset);
		}

		static EncryptionVersion getHeaderOffsetAndVersion(PeImage peImage, out uint headerOffset) {
			headerOffset = 0;

			var version = getVersion(peImage, headerOffset);
			if (version != EncryptionVersion.Unknown)
				return version;

			var section = peImage.findSection(".rsrc");
			if (section == null)
				return EncryptionVersion.Unknown;

			headerOffset = section.pointerToRawData;
			uint end = section.pointerToRawData + section.sizeOfRawData - 0x1000 + 1;
			while (headerOffset < end) {
				version = getVersion(peImage, headerOffset);
				if (version != EncryptionVersion.Unknown)
					return version;
				headerOffset++;
			}

			return EncryptionVersion.Unknown;
		}

		static EncryptionVersion getVersion(PeImage peImage, uint headerOffset) {
			uint m1lo = peImage.offsetReadUInt32(headerOffset + 0x900);
			uint m1hi = peImage.offsetReadUInt32(headerOffset + 0x904);

			foreach (var info in EncryptionInfos.Rva900h) {
				if (info.MagicLo == m1lo && info.MagicHi == m1hi)
					return info.Version;
			}

			return EncryptionVersion.Unknown;
		}
	}
}
