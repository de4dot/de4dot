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
using de4dot.code.deobfuscators.CodeWall.randomc;

namespace de4dot.code.deobfuscators.CodeWall {
	class KeyGenerator {
		CRandomMersenne mersenne;
		CRandomMother mother;

		public KeyGenerator(int seed) {
			mersenne = new CRandomMersenne(seed);
			mother = new CRandomMother(seed);
		}

		uint Random() => (mersenne.BRandom() >> 1) ^ (uint)Math.Abs((int)(mother.Random() * int.MinValue));

		public byte[] Generate(int size) {
			var key = new byte[size];
			for (int i = 0; i < size; i++)
				key[i] = (byte)Random();
			return key;
		}
	}
}
