/*
    Copyright (C) 2011-2014 de4dot@gmail.com

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
using System.Runtime.Serialization;

namespace de4dot.code.resources {
	class BuiltInResourceData : IResourceData {
		readonly ResourceTypeCode code;
		readonly object data;

		public object Data {
			get { return data; }
		}

		public ResourceTypeCode Code {
			get { return code; }
		}

		public BuiltInResourceData(ResourceTypeCode code, object data) {
			this.code = code;
			this.data = data;
		}

		public void WriteData(BinaryWriter writer, IFormatter formatter) {
			switch (code) {
			case ResourceTypeCode.Null:
				return;

			case ResourceTypeCode.String:
				writer.Write((string)data);
				break;

			case ResourceTypeCode.Boolean:
				writer.Write((bool)data);
				break;

			case ResourceTypeCode.Char:
				writer.Write((ushort)(char)data);
				break;

			case ResourceTypeCode.Byte:
				writer.Write((byte)data);
				break;

			case ResourceTypeCode.SByte:
				writer.Write((sbyte)data);
				break;

			case ResourceTypeCode.Int16:
				writer.Write((short)data);
				break;

			case ResourceTypeCode.UInt16:
				writer.Write((ushort)data);
				break;

			case ResourceTypeCode.Int32:
				writer.Write((int)data);
				break;

			case ResourceTypeCode.UInt32:
				writer.Write((uint)data);
				break;

			case ResourceTypeCode.Int64:
				writer.Write((long)data);
				break;

			case ResourceTypeCode.UInt64:
				writer.Write((ulong)data);
				break;

			case ResourceTypeCode.Single:
				writer.Write((float)data);
				break;

			case ResourceTypeCode.Double:
				writer.Write((double)data);
				break;

			case ResourceTypeCode.Decimal:
				writer.Write((decimal)data);
				break;

			case ResourceTypeCode.DateTime:
				writer.Write(((DateTime)data).ToBinary());
				break;

			case ResourceTypeCode.TimeSpan:
				writer.Write(((TimeSpan)data).Ticks);
				break;

			case ResourceTypeCode.ByteArray:
			case ResourceTypeCode.Stream:
				var ary = (byte[])data;
				writer.Write(ary.Length);
				writer.Write(ary);
				break;

			default:
				throw new ApplicationException("Unknown resource type code");
			}
		}

		public override string ToString() {
			switch (code) {
			case ResourceTypeCode.Null:
				return "NULL";

			case ResourceTypeCode.String:
			case ResourceTypeCode.Boolean:
			case ResourceTypeCode.Char:
			case ResourceTypeCode.Byte:
			case ResourceTypeCode.SByte:
			case ResourceTypeCode.Int16:
			case ResourceTypeCode.UInt16:
			case ResourceTypeCode.Int32:
			case ResourceTypeCode.UInt32:
			case ResourceTypeCode.Int64:
			case ResourceTypeCode.UInt64:
			case ResourceTypeCode.Single:
			case ResourceTypeCode.Double:
			case ResourceTypeCode.Decimal:
			case ResourceTypeCode.DateTime:
			case ResourceTypeCode.TimeSpan:
				return string.Format("{0}: '{1}'", code, data);

			case ResourceTypeCode.ByteArray:
			case ResourceTypeCode.Stream:
				var ary = data as byte[];
				if (ary != null)
					return string.Format("{0}: Length: {1}", code, ary.Length);
				return string.Format("{0}: '{1}'", code, data);

			default:
				return string.Format("{0}: '{1}'", code, data);
			}
		}
	}
}
