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
using System.Drawing;
using System.IO;
using System.Runtime.Serialization;
using dnlib.DotNet;
using dnlib.DotNet.Resources;

namespace de4dot.code.deobfuscators.CodeVeil {
	class ResourceConverter {
		ModuleDefMD module;
		ResourceInfo[] infos;
		MyResourceDataFactory dataCreator;

		sealed class MyResourceDataFactory : ResourceDataFactory {
			public MyResourceDataFactory(ModuleDef module)
				: base(module) {
			}

			protected override string GetAssemblyFullName(string simpleName) {
				var asm = TheAssemblyResolver.Instance.Resolve(new AssemblyNameInfo(simpleName), Module);
				return asm?.FullName;
			}
		}

		public ResourceConverter(ModuleDefMD module, ResourceInfo[] infos) {
			this.module = module;
			dataCreator = new MyResourceDataFactory(module);
			this.infos = infos;
		}

		public byte[] Convert() {
			var resources = new ResourceElementSet();
			foreach (var info in infos)
				resources.Add(Convert(info));

			var memStream = new MemoryStream();
			ResourceWriter.Write(module, memStream, resources);
			return memStream.ToArray();
		}

		ResourceElement Convert(ResourceInfo info) {
			var reader = info.dataReader;
			reader.Position = (uint)info.offset;

			IResourceData resourceData;
			int type = (info.flags & 0x7F);
			switch (type) {
			case 1:		// bool
				resourceData = dataCreator.Create(reader.ReadBoolean());
				break;

			case 2:		// byte
				resourceData = dataCreator.Create(reader.ReadByte());
				break;

			case 3:		// byte[]
				resourceData = dataCreator.Create(reader.ReadBytes(info.length));
				break;

			case 4:		// char[]
				resourceData = new CharArrayResourceData(dataCreator.CreateUserResourceType(CharArrayResourceData.ReflectionTypeName), DataReaderUtils.ReadChars(ref reader, info.length));
				break;

			case 5:		// sbyte
				resourceData = dataCreator.Create(reader.ReadSByte());
				break;

			case 6:		// char
				resourceData = dataCreator.Create(DataReaderUtils.ReadChar(ref reader));
				break;

			case 7:		// decimal
				resourceData = dataCreator.Create(reader.ReadDecimal());
				break;

			case 8:		// double
				resourceData = dataCreator.Create(reader.ReadDouble());
				break;

			case 9:		// short
				resourceData = dataCreator.Create(reader.ReadInt16());
				break;

			case 10:	// int
				resourceData = dataCreator.Create(reader.ReadInt32());
				break;

			case 11:	// long
				resourceData = dataCreator.Create(reader.ReadInt64());
				break;

			case 12:	// float
				resourceData = dataCreator.Create(reader.ReadSingle());
				break;

			case 13:	// string
				resourceData = dataCreator.Create(reader.ReadSerializedString());
				break;

			case 14:	// ushort
				resourceData = dataCreator.Create(reader.ReadUInt16());
				break;

			case 15:	// uint
				resourceData = dataCreator.Create(reader.ReadUInt32());
				break;

			case 16:	// ulong
				resourceData = dataCreator.Create(reader.ReadUInt64());
				break;

			case 17:	// DateTime
				resourceData = dataCreator.Create(DateTime.FromBinary(reader.ReadInt64()));
				break;

			case 18:	// TimeSpan
				resourceData = dataCreator.Create(TimeSpan.FromTicks(reader.ReadInt64()));
				break;

			case 19:	// Icon
				resourceData = new IconResourceData(dataCreator.CreateUserResourceType(IconResourceData.ReflectionTypeName), reader.ReadBytes(info.length));
				break;

			case 20:	// Image
				resourceData = new ImageResourceData(dataCreator.CreateUserResourceType(ImageResourceData.ReflectionTypeName), reader.ReadBytes(info.length));
				break;

			case 31:	// binary
				resourceData = dataCreator.CreateSerialized(reader.ReadBytes(info.length));
				break;

			case 21:	// Point (CV doesn't restore this type)
			default:
				throw new Exception("Unknown type");
			}

			return new ResourceElement() {
				Name = info.name,
				ResourceData = resourceData,
			};
		}
	}

	class CharArrayResourceData : UserResourceData {
		public static readonly string ReflectionTypeName = "System.Char[],mscorlib";
		char[] data;
		public CharArrayResourceData(UserResourceType type, char[] data) : base(type) => this.data = data;
		public override void WriteData(BinaryWriter writer, IFormatter formatter) => formatter.Serialize(writer.BaseStream, data);
		public override string ToString() => $"char[]: Length: {data.Length}";
	}

	class IconResourceData : UserResourceData {
		public static readonly string ReflectionTypeName = "System.Drawing.Icon,System.Drawing";
		Icon icon;
		public IconResourceData(UserResourceType type, byte[] data) : base(type) => icon = new Icon(new MemoryStream(data));
		public override void WriteData(BinaryWriter writer, IFormatter formatter) => formatter.Serialize(writer.BaseStream, icon);
		public override string ToString() => $"Icon: {icon}";
	}

	class ImageResourceData : UserResourceData {
		public static readonly string ReflectionTypeName = "System.Drawing.Bitmap,System.Drawing";
		Bitmap bitmap;
		public ImageResourceData(UserResourceType type, byte[] data) : base(type) => bitmap = new Bitmap(Image.FromStream(new MemoryStream(data)));
		public override void WriteData(BinaryWriter writer, IFormatter formatter) => formatter.Serialize(writer.BaseStream, bitmap);
		public override string ToString() => "Bitmap";
	}
}
