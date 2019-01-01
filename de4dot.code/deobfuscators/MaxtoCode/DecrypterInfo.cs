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

namespace de4dot.code.deobfuscators.MaxtoCode {
	class DecrypterInfo : IDisposable {
		public MainType mainType;
		public readonly MyPEImage peImage;
		public readonly PeHeader peHeader;
		public readonly McKey mcKey;
		public readonly byte[] fileData;

		public DecrypterInfo(MainType mainType, byte[] fileData) {
			this.mainType = mainType;
			peImage = new MyPEImage(fileData);
			peHeader = new PeHeader(mainType, peImage);
			mcKey = new McKey(peImage, peHeader);
			this.fileData = fileData;
		}

		public void Dispose() {
			if (peImage != null)
				peImage.Dispose();
		}
	}
}
