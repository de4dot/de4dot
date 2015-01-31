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
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using dnlib.DotNet;
using dnlib.IO;

namespace de4dot.code.resources {
	[Serializable]
	class ResourceReaderException : Exception {
		public ResourceReaderException(string msg)
			: base(msg) {
		}
	}

	struct ResourceReader {
		IBinaryReader reader;
		ResourceDataCreator resourceDataCreator;

		ResourceReader(ModuleDef module, IBinaryReader reader) {
			this.reader = reader;
			this.resourceDataCreator = new ResourceDataCreator(module);
		}

		public static ResourceElementSet Read(ModuleDef module, IBinaryReader reader) {
			return new ResourceReader(module, reader).Read();
		}

		ResourceElementSet Read() {
			ResourceElementSet resources = new ResourceElementSet();

			uint sig = reader.ReadUInt32();
			if (sig != 0xBEEFCACE)
				throw new ResourceReaderException(string.Format("Invalid resource sig: {0:X8}", sig));
			if (!CheckReaders())
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
			reader.Position = (reader.Position + 7) & ~7;

			var hashes = new int[numResources];
			for (int i = 0; i < numResources; i++)
				hashes[i] = reader.ReadInt32();
			var offsets = new int[numResources];
			for (int i = 0; i < numResources; i++)
				offsets[i] = reader.ReadInt32();

			long baseOffset = reader.Position;
			long dataBaseOffset = reader.ReadInt32();
			long nameBaseOffset = reader.Position;
			long end = reader.Length;

			var infos = new List<ResourceInfo>(numResources);

			for (int i = 0; i < numResources; i++) {
				reader.Position = nameBaseOffset + offsets[i];
				var name = reader.ReadString(Encoding.Unicode);
				long offset = dataBaseOffset + reader.ReadInt32();
				infos.Add(new ResourceInfo(name, offset));
			}

			infos.Sort((a, b) => a.offset.CompareTo(b.offset));
			for (int i = 0; i < infos.Count; i++) {
				var info = infos[i];
				var element = new ResourceElement();
				element.Name = info.name;
				reader.Position = info.offset;
				long nextDataOffset = i == infos.Count - 1 ? end : infos[i + 1].offset;
				int size = (int)(nextDataOffset - info.offset);
				element.ResourceData = ReadResourceData(userTypes, size);

				resources.Add(element);
			}

			return resources;
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

		IResourceData ReadResourceData(List<UserResourceType> userTypes, int size) {
			uint code = ReadUInt32(reader);
			switch ((ResourceTypeCode)code) {
			case ResourceTypeCode.Null: return resourceDataCreator.CreateNull();
			case ResourceTypeCode.String: return resourceDataCreator.Create(reader.ReadString());
			case ResourceTypeCode.Boolean: return resourceDataCreator.Create(reader.ReadBoolean());
			case ResourceTypeCode.Char: return resourceDataCreator.Create((char)reader.ReadUInt16());
			case ResourceTypeCode.Byte: return resourceDataCreator.Create(reader.ReadByte());
			case ResourceTypeCode.SByte: return resourceDataCreator.Create(reader.ReadSByte());
			case ResourceTypeCode.Int16: return resourceDataCreator.Create(reader.ReadInt16());
			case ResourceTypeCode.UInt16: return resourceDataCreator.Create(reader.ReadUInt16());
			case ResourceTypeCode.Int32: return resourceDataCreator.Create(reader.ReadInt32());
			case ResourceTypeCode.UInt32: return resourceDataCreator.Create(reader.ReadUInt32());
			case ResourceTypeCode.Int64: return resourceDataCreator.Create(reader.ReadInt64());
			case ResourceTypeCode.UInt64: return resourceDataCreator.Create(reader.ReadUInt64());
			case ResourceTypeCode.Single: return resourceDataCreator.Create(reader.ReadSingle());
			case ResourceTypeCode.Double: return resourceDataCreator.Create(reader.ReadDouble());
			case ResourceTypeCode.Decimal: return resourceDataCreator.Create(reader.ReadDecimal());
			case ResourceTypeCode.DateTime: return resourceDataCreator.Create(new DateTime(reader.ReadInt64()));
			case ResourceTypeCode.TimeSpan: return resourceDataCreator.Create(new TimeSpan(reader.ReadInt64()));
			case ResourceTypeCode.ByteArray: return resourceDataCreator.Create(reader.ReadBytes(reader.ReadInt32()));
			case ResourceTypeCode.Stream: return resourceDataCreator.CreateStream(reader.ReadBytes(reader.ReadInt32()));
			default:
				int userTypeIndex = (int)(code - (uint)ResourceTypeCode.UserTypes);
				if (userTypeIndex < 0 || userTypeIndex >= userTypes.Count)
					throw new ResourceReaderException(string.Format("Invalid resource data code: {0}", code));
				return resourceDataCreator.CreateSerialized(reader.ReadBytes(size));
			}
		}

		static uint ReadUInt32(IBinaryReader reader) {
			try {
				return reader.Read7BitEncodedUInt32();
			}
			catch {
				throw new ResourceReaderException("Invalid encoded int32");
			}
		}

		bool CheckReaders() {
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
