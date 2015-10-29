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
using System.Collections.Generic;

namespace de4dot.mdecrypt {
	[Serializable]
	public class DecryptMethodsInfo {
		// The <Module>::.cctor() method body bytes.
		// Initialize this so only the methods decrypter method gets executed in
		// <Module>::.cctor(). If null, all code in the original <Module>::.cctor()
		// gets executed.
		public byte[] moduleCctorBytes;

		// The metadata tokens of all methods to decrypt. Use null if all methods should
		// be decrypted.
		public List<uint> methodsToDecrypt;
	}
}
