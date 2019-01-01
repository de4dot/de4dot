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

using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip.Compression;

namespace de4dot.code.deobfuscators.Babel_NET {
	class BabelInflater : Inflater {
		int magic;

		public BabelInflater(bool noHeader, int magic) : base(noHeader) => this.magic = magic;

		protected override bool ReadHeader(ref bool isLastBlock, out int blockType) {
			const int numBits = 4;

			int type = input.PeekBits(numBits);
			if (type < 0) {
				blockType = -1;
				return false;
			}
			input.DropBits(numBits);

			if ((type & 1) != 0)
				isLastBlock = true;
			switch (type >> 1) {
			case 1: blockType = STORED_BLOCK; break;
			case 5: blockType = STATIC_TREES; break;
			case 6: blockType = DYN_TREES; break;
			default: throw new SharpZipBaseException("Unknown block type: " + type);
			}
			return true;
		}

		protected override bool DecodeStoredLength() {
			if ((uncomprLen = input.PeekBits(16)) < 0)
				return false;
			input.DropBits(16);

			uncomprLen ^= magic;

			return true;
		}
	}
}
