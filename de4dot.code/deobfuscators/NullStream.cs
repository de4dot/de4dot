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
using System.IO;

namespace de4dot.code.deobfuscators {
	public class NullStream : Stream {
		long offset = 0;
		long length = 0;

		public override bool CanRead => false;
		public override bool CanSeek => true;
		public override bool CanWrite => true;
		public override void Flush() { }
		public override long Length => length;

		public override long Position {
			get => offset;
			set => offset = value;
		}

		public override int Read(byte[] buffer, int offset, int count) => throw new NotImplementedException();

		public override long Seek(long offset, SeekOrigin origin) {
			switch (origin) {
			case SeekOrigin.Begin:
				this.offset = offset;
				break;

			case SeekOrigin.Current:
				this.offset += offset;
				break;

			case SeekOrigin.End:
				this.offset = length + offset;
				break;

			default:
				throw new NotSupportedException();
			}

			return this.offset;
		}

		public override void SetLength(long value) => length = value;

		public override void Write(byte[] buffer, int offset, int count) {
			this.offset += count;
			if (this.offset > length)
				length = this.offset;
		}
	}
}
