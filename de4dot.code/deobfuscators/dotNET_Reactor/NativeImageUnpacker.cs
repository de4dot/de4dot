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

			//TODO: Hard coded offsets: DNR 4.0/4.1 + .NET 2.0+
			var keyData = new byte[6] {
				peImage.offsetReadByte(7173),
				peImage.offsetReadByte(7183),
				peImage.offsetReadByte(7256),
				peImage.offsetReadByte(7277),
				peImage.offsetReadByte(7320),
				peImage.offsetReadByte(7334),
			};
			var decrypter = new NativeFileDecrypter(keyData);
			decrypter.decrypt(encryptedData, 0, encryptedData.Length);

			int inflatedSize = BitConverter.ToInt32(encryptedData, 0);
			var inflater = new Inflater();
			byte[] inflatedData = new byte[inflatedSize];
			inflater.SetInput(encryptedData, 4, encryptedData.Length - 4);
			int count = inflater.Inflate(inflatedData);
			if (count != inflatedSize)
				return null;

			return inflatedData;
		}
	}
}
