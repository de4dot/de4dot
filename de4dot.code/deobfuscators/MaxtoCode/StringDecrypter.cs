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
using System.Text;
using dnlib.DotNet;
using de4dot.blocks;

namespace de4dot.code.deobfuscators.MaxtoCode {
	class StringDecrypter {
		DecrypterInfo decrypterInfo;
		MethodDef decryptMethod;
		string[] decryptedStrings;
		Encoding encoding;

		public MethodDef Method {
			get { return decryptMethod; }
		}

		public bool Detected {
			get { return decryptMethod != null; }
		}

		public StringDecrypter(DecrypterInfo decrypterInfo) {
			this.decrypterInfo = decrypterInfo;
		}

		public void Find() {
			if (decrypterInfo == null)
				return;
			decryptMethod = FindDecryptMethod(decrypterInfo.mainType.Type);
			if (decryptMethod == null)
				return;
		}

		static MethodDef FindDecryptMethod(TypeDef type) {
			if (type == null)
				return null;
			foreach (var method in type.Methods) {
				if (method.Body == null || !method.IsStatic || method.IsPrivate)
					continue;
				if (!DotNetUtils.IsMethod(method, "System.String", "(System.UInt32)"))
					continue;
				if (!DotNetUtils.CallsMethod(method, "System.String System.Runtime.InteropServices.Marshal::PtrToStringAnsi(System.IntPtr)"))
					continue;

				return method;
			}
			return null;
		}

		public void Initialize(Encoding encoding) {
			this.encoding = encoding;
		}

		void InitializeStrings() {
			if (decryptedStrings != null)
				return;
			var peImage = decrypterInfo.peImage;
			var peHeader = decrypterInfo.peHeader;
			var mcKey = decrypterInfo.mcKey;
			var fileData = decrypterInfo.fileData;

			var stringsRva = peHeader.GetRva(0x0AF0, mcKey.ReadUInt32(0x46));
			if (stringsRva == 0)
				return;
			int stringsOffset = (int)peImage.RvaToOffset(stringsRva);

			int numStrings = peImage.ReadInt32(stringsRva) ^ (int)mcKey.ReadUInt32(0);
			decryptedStrings = new string[numStrings];
			for (int i = 0, ki = 2, soffs = stringsOffset + 4; i < numStrings; i++) {
				int stringLen = BitConverter.ToInt32(fileData, soffs) ^ (int)mcKey.ReadUInt32(ki);
				ki += 2;
				if (ki >= 0x1FF0)
					ki = 0;
				soffs += 4;
				var bytes = new byte[stringLen];
				for (int j = 0; j < stringLen; j++, soffs++) {
					byte b = (byte)(fileData[soffs] ^ mcKey.ReadByte(ki));
					ki = Add(ki, 1);
					bytes[j] = b;
				}

				decryptedStrings[i] = Decode(bytes);
			}
		}

		string Decode(byte[] bytes) {
			string s = encoding.GetString(bytes);
			int len = s.Length;
			if (len == 0 || s[len - 1] != 0)
				return s;
			for (; len > 0; len--) {
				if (s[len - 1] != 0)
					break;
			}
			if (len <= 0)
				return string.Empty;
			return s.Substring(0, len);
		}

		static int Add(int ki, int size) {
			return (ki + size) % 0x1FF0;
		}

		public string Decrypt(uint id) {
			InitializeStrings();
			return decryptedStrings[(int)id - 1];
		}
	}
}
