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
using System.Text;

namespace de4dot.deobfuscators.SmartAssembly {
	class StringDecrypter {
		int stringOffset;
		byte[] decryptedData;

		public bool CanDecrypt {
			get { return decryptedData != null; }
		}

		public StringDecrypterInfo StringDecrypterInfo { get; private set; }

		public StringDecrypter(StringDecrypterInfo stringDecrypterInfo) {
			StringDecrypterInfo = stringDecrypterInfo;

			if (stringDecrypterInfo != null) {
				if (!stringDecrypterInfo.StringsEncrypted) {
					stringOffset = stringDecrypterInfo.StringOffset;
					decryptedData = stringDecrypterInfo.StringsResource.GetResourceData();
				}
				else if (stringDecrypterInfo.CanDecrypt) {
					stringOffset = stringDecrypterInfo.StringOffset;
					decryptedData = stringDecrypterInfo.decrypt();
				}
			}
		}

		public string decrypt(int token, int id) {
			if (!CanDecrypt)
				throw new ApplicationException("Can't decrypt strings since decryptedData is null");

			int index = id - (token & 0x00FFFFFF) - stringOffset;

			int len;
			byte b = decryptedData[index++];
			if ((b & 0x80) == 0)
				len = b;
			else if ((b & 0x40) == 0)
				len = ((b & 0x3F) << 8) + decryptedData[index++];
			else {
				len = ((b & 0x1F) << 24) +
						((int)decryptedData[index++] << 16) +
						((int)decryptedData[index++] << 8) +
						decryptedData[index++];
			}

			var decodedData = Convert.FromBase64String(Encoding.UTF8.GetString(decryptedData, index, len));
			return Encoding.UTF8.GetString(decodedData, 0, decodedData.Length);
		}
	}
}
