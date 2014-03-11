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
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using dnlib.DotNet;

namespace de4dot.code.resources {
	class ResourceWriter {
		ModuleDefMD module;
		BinaryWriter writer;
		ResourceElementSet resources;
		ResourceDataCreator typeCreator;
		Dictionary<UserResourceData, UserResourceType> dataToNewType = new Dictionary<UserResourceData, UserResourceType>();

		ResourceWriter(ModuleDefMD module, Stream stream, ResourceElementSet resources) {
			this.module = module;
			this.typeCreator = new ResourceDataCreator(module);
			this.writer = new BinaryWriter(stream);
			this.resources = resources;
		}

		public static void Write(ModuleDefMD module, Stream stream, ResourceElementSet resources) {
			new ResourceWriter(module, stream, resources).Write();
		}

		void Write() {
			InitializeUserTypes();

			writer.Write(0xBEEFCACE);
			writer.Write(1);
			WriteReaderType();
			writer.Write(2);
			writer.Write(resources.Count);
			writer.Write(typeCreator.Count);
			foreach (var userType in typeCreator.GetSortedTypes())
				writer.Write(userType.Name);
			int extraBytes = 8 - ((int)writer.BaseStream.Position & 7);
			if (extraBytes != 8) {
				for (int i = 0; i < extraBytes; i++)
					writer.Write((byte)'X');
			}

			var nameOffsetStream = new MemoryStream();
			var nameOffsetWriter = new BinaryWriter(nameOffsetStream, Encoding.Unicode);
			var dataStream = new MemoryStream();
			var dataWriter = new BinaryWriter(dataStream);
			var hashes = new int[resources.Count];
			var offsets = new int[resources.Count];
			var formatter = new BinaryFormatter(null, new StreamingContext(StreamingContextStates.File | StreamingContextStates.Persistence));
			int index = 0;
			foreach (var info in resources.ResourceElements) {
				offsets[index] = (int)nameOffsetWriter.BaseStream.Position;
				hashes[index] = (int)Hash(info.Name);
				index++;
				nameOffsetWriter.Write(info.Name);
				nameOffsetWriter.Write((int)dataWriter.BaseStream.Position);
				WriteData(dataWriter, info, formatter);
			}

			Array.Sort(hashes, offsets);
			foreach (var hash in hashes)
				writer.Write(hash);
			foreach (var offset in offsets)
				writer.Write(offset);
			writer.Write((int)writer.BaseStream.Position + (int)nameOffsetStream.Length + 4);
			writer.Write(nameOffsetStream.ToArray());
			writer.Write(dataStream.ToArray());
		}

		void WriteData(BinaryWriter writer, ResourceElement info, IFormatter formatter) {
			var code = GetResourceType(info.ResourceData);
			WriteUInt32(writer, (uint)code);
			info.ResourceData.WriteData(writer, formatter);
		}

		static void WriteUInt32(BinaryWriter writer, uint value) {
			while (value >= 0x80) {
				writer.Write((byte)(value | 0x80));
				value >>= 7;
			}
			writer.Write((byte)value);
		}

		ResourceTypeCode GetResourceType(IResourceData data) {
			if (data is BuiltInResourceData)
				return data.Code;

			var userData = (UserResourceData)data;
			return dataToNewType[userData].Code;
		}

		static uint Hash(string key) {
			uint val = 0x1505;
			foreach (var c in key)
				val = ((val << 5) + val) ^ (uint)c;
			return val;
		}

		void InitializeUserTypes() {
			foreach (var resource in resources.ResourceElements) {
				var data = resource.ResourceData as UserResourceData;
				if (data == null)
					continue;
				var newType = typeCreator.CreateUserResourceType(data.TypeName);
				dataToNewType[data] = newType;
			}
		}

		void WriteReaderType() {
			var memStream = new MemoryStream();
			var headerWriter = new BinaryWriter(memStream);
			var mscorlibFullName = GetMscorlibFullname();
			headerWriter.Write("System.Resources.ResourceReader, " + mscorlibFullName);
			headerWriter.Write("System.Resources.RuntimeResourceSet");
			writer.Write((int)memStream.Position);
			writer.Write(memStream.ToArray());
		}

		string GetMscorlibFullname() {
			var mscorlibRef = module.GetAssemblyRef("mscorlib");
			if (mscorlibRef != null)
				return mscorlibRef.FullName;

			return "mscorlib, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
		}
	}
}
