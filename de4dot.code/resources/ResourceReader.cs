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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace de4dot.code.resources {
	[Serializable]
	class ResourceReaderException : Exception {
		public ResourceReaderException(string msg)
			: base(msg) {
		}
	}

	class ResourceReader {
		ModuleDefinition module;
		BinaryReader reader;
		ResourceDataCreator resourceDataCreator;

		ResourceReader(ModuleDefinition module, Stream stream) {
			this.module = module;
			this.reader = new BinaryReader(stream);
			this.resourceDataCreator = new ResourceDataCreator(module);
		}

		public static ResourceElementSet read(ModuleDefinition module, Stream stream) {
			return new ResourceReader(module, stream).read();
		}

		ResourceElementSet read() {
			ResourceElementSet resources = new ResourceElementSet();

			uint sig = reader.ReadUInt32();
			if (sig != 0xBEEFCACE)
				throw new ResourceReaderException(string.Format("Invalid resource sig: {0:X8}", sig));
			if (!checkReaders())
				throw new ResourceReaderException("Invalid resource reader");
			int version = reader.ReadInt32();
			if (version != 2)
				throw new ResourceReaderException(string.Format("Invalid resource version: {0}", version));
			int numResources = reader.ReadInt32();
			if (numResources < 0)
				throw new ResourceReaderException(string.Format("Invalid number of resources: {0}", numResources));
			int numUserTypes = reader.ReadInt32();
			if (numUserTypes < 0)
				throw new ResourceReaderException(string.Format("Invalid number of user types: {0}", numUserTypes));

			var userTypes = new List<UserResourceType>();
			for (int i = 0; i < numUserTypes; i++)
				userTypes.Add(new UserResourceType(reader.ReadString(), ResourceTypeCode.UserTypes + i));
			reader.BaseStream.Position = (reader.BaseStream.Position + 7) & ~7;

			var hashes = new int[numResources];
			for (int i = 0; i < numResources; i++)
				hashes[i] = reader.ReadInt32();
			var offsets = new int[numResources];
			for (int i = 0; i < numResources; i++)
				offsets[i] = reader.ReadInt32();

			long baseOffset = reader.BaseStream.Position;
			long dataBaseOffset = reader.ReadInt32();
			long nameBaseOffset = reader.BaseStream.Position;
			long end = reader.BaseStream.Length;

			var infos = new List<ResourceInfo>(numResources);

			var nameReader = new BinaryReader(reader.BaseStream, Encoding.Unicode);
			for (int i = 0; i < numResources; i++) {
				nameReader.BaseStream.Position = nameBaseOffset + offsets[i];
				var name = nameReader.ReadString();
				long offset = dataBaseOffset + nameReader.ReadInt32();
				infos.Add(new ResourceInfo(name, offset));
			}

			infos.Sort(sortResourceInfo);
			for (int i = 0; i < infos.Count; i++) {
				var info = infos[i];
				var element = new ResourceElement();
				element.Name = info.name;
				reader.BaseStream.Position = info.offset;
				long nextDataOffset = i == infos.Count - 1 ? end : infos[i + 1].offset;
				int size = (int)(nextDataOffset - info.offset);
				element.ResourceData = readResourceData(userTypes, size);

				resources.add(element);
			}

			return resources;
		}

		static int sortResourceInfo(ResourceInfo a, ResourceInfo b) {
			return Utils.compareInt32((int)a.offset, (int)b.offset);
		}

		class ResourceInfo {
			public string name;
			public long offset;
			public ResourceInfo(string name, long offset) {
				this.name = name;
				this.offset = offset;
			}
			public override string ToString() {
				return string.Format("{0:X8} - {1}", offset, name);
			}
		}

		IResourceData readResourceData(List<UserResourceType> userTypes, int size) {
			uint code = readUInt32(reader);
			switch ((ResourceTypeCode)code) {
			case ResourceTypeCode.Null: return resourceDataCreator.createNull();
			case ResourceTypeCode.String: return resourceDataCreator.create(reader.ReadString());
			case ResourceTypeCode.Boolean: return resourceDataCreator.create(reader.ReadBoolean());
			case ResourceTypeCode.Char: return resourceDataCreator.create((char)reader.ReadUInt16());
			case ResourceTypeCode.Byte: return resourceDataCreator.create(reader.ReadByte());
			case ResourceTypeCode.SByte: return resourceDataCreator.create(reader.ReadSByte());
			case ResourceTypeCode.Int16: return resourceDataCreator.create(reader.ReadInt16());
			case ResourceTypeCode.UInt16: return resourceDataCreator.create(reader.ReadUInt16());
			case ResourceTypeCode.Int32: return resourceDataCreator.create(reader.ReadInt32());
			case ResourceTypeCode.UInt32: return resourceDataCreator.create(reader.ReadUInt32());
			case ResourceTypeCode.Int64: return resourceDataCreator.create(reader.ReadInt64());
			case ResourceTypeCode.UInt64: return resourceDataCreator.create(reader.ReadUInt64());
			case ResourceTypeCode.Single: return resourceDataCreator.create(reader.ReadSingle());
			case ResourceTypeCode.Double: return resourceDataCreator.create(reader.ReadDouble());
			case ResourceTypeCode.Decimal: return resourceDataCreator.create(reader.ReadDecimal());
			case ResourceTypeCode.DateTime: return resourceDataCreator.create(new DateTime(reader.ReadInt64()));
			case ResourceTypeCode.TimeSpan: return resourceDataCreator.create(new TimeSpan(reader.ReadInt64()));
			case ResourceTypeCode.ByteArray: return resourceDataCreator.create(reader.ReadBytes(reader.ReadInt32()));
			default:
				int userTypeIndex = (int)(code - (uint)ResourceTypeCode.UserTypes);
				if (userTypeIndex < 0 || userTypeIndex >= userTypes.Count)
					throw new ResourceReaderException(string.Format("Invalid resource data code: {0}", code));
				return resourceDataCreator.createSerialized(reader.ReadBytes(size));
			}
		}

		static uint readUInt32(BinaryReader reader) {
			try {
				return Utils.readEncodedUInt32(reader);
			}
			catch {
				throw new ResourceReaderException("Invalid encoded int32");
			}
		}

		bool checkReaders() {
			bool validReader = false;

			int numReaders = reader.ReadInt32();
			if (numReaders < 0)
				throw new ResourceReaderException(string.Format("Invalid number of readers: {0}", numReaders));
			int readersSize = reader.ReadInt32();
			if (readersSize < 0)
				throw new ResourceReaderException(string.Format("Invalid readers size: {0:X8}", readersSize));

			for (int i = 0; i < numReaders; i++) {
				var resourceReaderFullName = reader.ReadString();
				var resourceSetFullName = reader.ReadString();
				if (Regex.IsMatch(resourceReaderFullName, @"^System\.Resources\.ResourceReader,\s*mscorlib,"))
					validReader = true;
			}

			return validReader;
		}
	}
}
