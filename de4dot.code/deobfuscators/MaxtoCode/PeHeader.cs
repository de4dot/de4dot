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
using dnlib.PE;

namespace de4dot.code.deobfuscators.MaxtoCode {
	enum EncryptionVersion {
		Unknown,
		V1,
		V2,
		V3,
		V4,
		V5,
		V6,
		V7,
		V8,
	}

	class PeHeader {
		EncryptionVersion version;
		byte[] headerData;
		uint xorKey;

		public EncryptionVersion EncryptionVersion {
			get { return version; }
		}

		public PeHeader(MainType mainType, MyPEImage peImage) {
			uint headerOffset;
			version = GetHeaderOffsetAndVersion(peImage, out headerOffset);
			headerData = peImage.OffsetReadBytes(headerOffset, 0x1000);

			switch (version) {
			case EncryptionVersion.V1:
			case EncryptionVersion.V2:
			case EncryptionVersion.V3:
			case EncryptionVersion.V4:
			case EncryptionVersion.V5:
			default:
				xorKey = 0x7ABF931;
				break;

			case EncryptionVersion.V6:
				xorKey = 0x7ABA931;
				break;

			case EncryptionVersion.V7:
				xorKey = 0x8ABA931;
				break;

			case EncryptionVersion.V8:
				if (CheckMcKeyRva(peImage, 0x99BA9A13))
					break;
				if (CheckMcKeyRva(peImage, 0x18ABA931))
					break;
				if (CheckMcKeyRva(peImage, 0x18ABA933))
					break;
				break;
			}
		}

		bool CheckMcKeyRva(MyPEImage peImage, uint newXorKey) {
			xorKey = newXorKey;
			uint rva = GetMcKeyRva();
			return (rva & 0xFFF) == 0 && peImage.FindSection((RVA)rva) != null;
		}

		public uint GetMcKeyRva() {
			return GetRva(0x0FFC, xorKey);
		}

		public uint GetRva(int offset, uint xorKey) {
			return ReadUInt32(offset) ^ xorKey;
		}

		public uint ReadUInt32(int offset) {
			return BitConverter.ToUInt32(headerData, offset);
		}

		static EncryptionVersion GetHeaderOffsetAndVersion(MyPEImage peImage, out uint headerOffset) {
			headerOffset = 0;

			var version = GetVersion(peImage, headerOffset);
			if (version != EncryptionVersion.Unknown)
				return version;

			var section = peImage.FindSection(".rsrc");
			if (section != null) {
				version = GetHeaderOffsetAndVersion(section, peImage, out headerOffset);
				if (version != EncryptionVersion.Unknown)
					return version;
			}

			foreach (var section2 in peImage.Sections) {
				version = GetHeaderOffsetAndVersion(section2, peImage, out headerOffset);
				if (version != EncryptionVersion.Unknown)
					return version;
			}

			return EncryptionVersion.Unknown;
		}

		static EncryptionVersion GetHeaderOffsetAndVersion(ImageSectionHeader section, MyPEImage peImage, out uint headerOffset) {
			headerOffset = section.PointerToRawData;
			uint end = section.PointerToRawData + section.SizeOfRawData - 0x1000 + 1;
			while (headerOffset < end) {
				var version = GetVersion(peImage, headerOffset);
				if (version != EncryptionVersion.Unknown)
					return version;
				headerOffset++;
			}

			return EncryptionVersion.Unknown;
		}

		static EncryptionVersion GetVersion(MyPEImage peImage, uint headerOffset) {
			uint m1lo = peImage.OffsetReadUInt32(headerOffset + 0x900);
			uint m1hi = peImage.OffsetReadUInt32(headerOffset + 0x904);

			foreach (var info in EncryptionInfos.Rva900h) {
				if (info.MagicLo == m1lo && info.MagicHi == m1hi)
					return info.Version;
			}

			return EncryptionVersion.Unknown;
		}
	}
}
