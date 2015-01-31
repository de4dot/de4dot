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
using dnlib.IO;
using dnlib.DotNet;
using de4dot.code.resources;

namespace de4dot.code.deobfuscators.CodeVeil {
	class ResourceConverter {
		ModuleDefMD module;
		ResourceInfo[] infos;
		ResourceDataCreator dataCreator;

		public ResourceConverter(ModuleDefMD module, ResourceInfo[] infos) {
			this.module = module;
			this.dataCreator = new ResourceDataCreator(module);
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
			reader.Position = info.offset;

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
				resourceData = dataCreator.Create(reader.ReadChars(info.length));
				break;

			case 5:		// sbyte
				resourceData = dataCreator.Create(reader.ReadSByte());
				break;

			case 6:		// char
				resourceData = dataCreator.Create(reader.ReadChar());
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
				resourceData = dataCreator.Create(reader.ReadString());
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
				resourceData = dataCreator.CreateIcon(reader.ReadBytes(info.length));
				break;

			case 20:	// Image
				resourceData = dataCreator.CreateImage(reader.ReadBytes(info.length));
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
}
