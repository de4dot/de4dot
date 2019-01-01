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

namespace de4dot.code.deobfuscators.Agile_NET {
	class CsBlowfish : Blowfish {
		public CsBlowfish() {
		}

		public CsBlowfish(byte[] key)
			: base(key) {
		}

		protected override void Encrypt(ref uint rxl, ref uint rxr) {
			uint xl = rxl, xr = rxr;
			for (int i = 0; i < 16; i++) {
				xl ^= P[i];
				uint t = xl;
				xl = (xl >> 24) ^ xr;
				xr = t;
			}
			rxr = xl ^ P[16];
			rxl = xr ^ P[17];
		}

		protected override void Decrypt(ref uint rxl, ref uint rxr) {
			uint xl = rxl, xr = rxr;
			for (int i = 17; i >= 2; i--) {
				xl ^= P[i];
				uint t = xl;
				xl = (xl >> 24) ^ xr;
				xr = t;
			}
			rxr = xl ^ P[1];
			rxl = xr ^ P[0];
		}
	}
}
