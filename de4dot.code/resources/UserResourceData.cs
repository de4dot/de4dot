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

using System.Drawing;
using System.IO;
using System.Runtime.Serialization;

namespace de4dot.code.resources {
	abstract class UserResourceData : IResourceData {
		readonly UserResourceType type;

		public string TypeName {
			get { return type.Name; }
		}

		public ResourceTypeCode Code {
			get { return type.Code; }
		}

		public UserResourceData(UserResourceType type) {
			this.type = type;
		}

		public abstract void WriteData(BinaryWriter writer, IFormatter formatter);
	}

	class CharArrayResourceData : UserResourceData {
		public static readonly string ReflectionTypeName = "System.Char[],mscorlib";
		char[] data;

		public CharArrayResourceData(UserResourceType type, char[] data)
			: base(type) {
			this.data = data;
		}

		public override void WriteData(BinaryWriter writer, IFormatter formatter) {
			formatter.Serialize(writer.BaseStream, data);
		}

		public override string ToString() {
			return string.Format("char[]: Length: {0}", data.Length);
		}
	}

	class IconResourceData : UserResourceData {
		public static readonly string ReflectionTypeName = "System.Drawing.Icon,System.Drawing";
		Icon icon;

		public IconResourceData(UserResourceType type, byte[] data)
			: base(type) {
			icon = new Icon(new MemoryStream(data));
		}

		public override void WriteData(BinaryWriter writer, IFormatter formatter) {
			formatter.Serialize(writer.BaseStream, icon);
		}

		public override string ToString() {
			return string.Format("Icon: {0}", icon);
		}
	}

	class ImageResourceData : UserResourceData {
		public static readonly string ReflectionTypeName = "System.Drawing.Bitmap,System.Drawing";
		Bitmap bitmap;

		public ImageResourceData(UserResourceType type, byte[] data)
			: base(type) {
			bitmap = new Bitmap(Image.FromStream(new MemoryStream(data)));
		}

		public override void WriteData(BinaryWriter writer, IFormatter formatter) {
			formatter.Serialize(writer.BaseStream, bitmap);
		}

		public override string ToString() {
			return "Bitmap";
		}
	}

	class BinaryResourceData : UserResourceData {
		byte[] data;

		public BinaryResourceData(UserResourceType type, byte[] data)
			: base(type) {
			this.data = data;
		}

		public override void WriteData(BinaryWriter writer, IFormatter formatter) {
			writer.Write(data);
		}

		public override string ToString() {
			return string.Format("Binary: Length: {0}", data.Length);
		}
	}
}
