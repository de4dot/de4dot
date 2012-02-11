/*
    Copyright (C) 2011-2012 de4dot@gmail.com

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
using Mono.Cecil;
using de4dot.code.resources;

namespace de4dot.code.deobfuscators.CodeVeil {
	class ResourceConverter {
		ModuleDefinition module;
		ResourceInfo[] infos;
		ResourceDataCreator dataCreator;

		public ResourceConverter(ModuleDefinition module, ResourceInfo[] infos) {
			this.module = module;
			this.dataCreator = new ResourceDataCreator(module);
			this.infos = infos;
		}

		public byte[] convert() {
			var resources = new ResourceElementSet();
			foreach (var info in infos)
				resources.add(convert(info));

			var memStream = new MemoryStream();
			ResourceWriter.write(module, memStream, resources);
			return memStream.ToArray();
		}

		ResourceElement convert(ResourceInfo info) {
			var reader = info.dataReader;
			reader.BaseStream.Position = info.offset;

			IResourceData resourceData;
			int type = (info.flags & 0x7F);
			switch (type) {
			case 1:		// bool
				resourceData = dataCreator.create(reader.ReadBoolean());
				break;

			case 2:		// byte
				resourceData = dataCreator.create(reader.ReadByte());
				break;

			case 3:		// byte[]
				resourceData = dataCreator.create(reader.ReadBytes(info.length));
				break;

			case 4:		// char[]
				resourceData = dataCreator.create(reader.ReadChars(info.length));
				break;

			case 5:		// sbyte
				resourceData = dataCreator.create(reader.ReadSByte());
				break;

			case 6:		// char
				resourceData = dataCreator.create(reader.ReadChar());
				break;

			case 7:		// decimal
				resourceData = dataCreator.create(reader.ReadDecimal());
				break;

			case 8:		// double
				resourceData = dataCreator.create(reader.ReadDouble());
				break;

			case 9:		// short
				resourceData = dataCreator.create(reader.ReadInt16());
				break;

			case 10:	// int
				resourceData = dataCreator.create(reader.ReadInt32());
				break;

			case 11:	// long
				resourceData = dataCreator.create(reader.ReadInt64());
				break;

			case 12:	// float
				resourceData = dataCreator.create(reader.ReadSingle());
				break;

			case 13:	// string
				resourceData = dataCreator.create(reader.ReadString());
				break;

			case 14:	// ushort
				resourceData = dataCreator.create(reader.ReadUInt16());
				break;

			case 15:	// uint
				resourceData = dataCreator.create(reader.ReadUInt32());
				break;

			case 16:	// ulong
				resourceData = dataCreator.create(reader.ReadUInt64());
				break;

			case 17:	// DateTime
				resourceData = dataCreator.create(DateTime.FromBinary(reader.ReadInt64()));
				break;

			case 18:	// TimeSpan
				resourceData = dataCreator.create(TimeSpan.FromTicks(reader.ReadInt64()));
				break;

			case 19:	// Icon
				resourceData = dataCreator.createIcon(reader.ReadBytes(info.length));
				break;

			case 20:	// Image
				resourceData = dataCreator.createImage(reader.ReadBytes(info.length));
				break;

			case 31:	// binary
				resourceData = dataCreator.createSerialized(reader.ReadBytes(info.length));
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
