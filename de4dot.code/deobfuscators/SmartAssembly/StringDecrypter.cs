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
using System.Text;

namespace de4dot.code.deobfuscators.SmartAssembly {
	class StringDecrypter {
		int stringOffset;
		byte[] decryptedData;

		public bool CanDecrypt => decryptedData != null;
		public StringDecrypterInfo StringDecrypterInfo { get; private set; }

		public StringDecrypter(StringDecrypterInfo stringDecrypterInfo) {
			StringDecrypterInfo = stringDecrypterInfo;

			if (stringDecrypterInfo != null) {
				if (!stringDecrypterInfo.StringsEncrypted) {
					stringOffset = stringDecrypterInfo.StringOffset;
					decryptedData = stringDecrypterInfo.StringsResource.CreateReader().ToArray();
				}
				else if (stringDecrypterInfo.CanDecrypt) {
					stringOffset = stringDecrypterInfo.StringOffset;
					decryptedData = stringDecrypterInfo.Decrypt();
				}
			}
		}

		public string Decrypt(int token, int id) {
			if (!CanDecrypt)
				throw new ApplicationException("Can't decrypt strings since decryptedData is null");

			int index = id - (token & 0x00FFFFFF) - stringOffset;
			int len = DeobUtils.ReadVariableLengthInt32(decryptedData, ref index);

			switch (StringDecrypterInfo.DecrypterVersion) {
			case StringDecrypterVersion.V1:
				// Some weird problem with 1.x decrypted strings. They all have a \x01 char at the end.
				var buf = Convert.FromBase64String(Encoding.ASCII.GetString(decryptedData, index, len));
				if (buf.Length % 2 != 0)
					Array.Resize(ref buf, buf.Length - 1);
				return Encoding.Unicode.GetString(buf);

			case StringDecrypterVersion.V2:
				return Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.ASCII.GetString(decryptedData, index, len)));

			default:
				return Encoding.UTF8.GetString(Convert.FromBase64String(Encoding.UTF8.GetString(decryptedData, index, len)));
			}
		}
	}
}
