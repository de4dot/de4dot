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

namespace de4dot.PE {
	using MVT = MetadataVarType;

	public class MetadataTables {
		BinaryReader reader;
		Metadata metadata;
		byte heapOffsetSizes;
		MetadataType[] metadataTypes = new MetadataType[64];

		public MetadataTables(BinaryReader reader, Metadata metadata) {
			this.reader = reader;
			this.metadata = metadata;
			init();
		}

		public MetadataType getMetadataType(MetadataIndex index) {
			return metadataTypes[(int)index];
		}

		void seek(uint fileOffset) {
			reader.BaseStream.Position = fileOffset;
		}

		// TODO: This table needs to be updated to support the other metadata tables.
		static MetadataVarType[] metadataVarType = new MetadataVarType[] {
			MVT.byte2, MVT.stringIndex, MVT.guidIndex, MVT.guidIndex, MVT.guidIndex, MVT.end,	// 0
			MVT.resolutionScope, MVT.stringIndex, MVT.stringIndex, MVT.end,			// 1
			MVT.byte4, MVT.stringIndex, MVT.stringIndex, MVT.typeDefOrRef, MVT.fieldIndex, MVT.methodDefIndex, MVT.end,	// 2
			MVT.end,																// 3
			MVT.byte2, MVT.stringIndex, MVT.blobIndex, MVT.end,						// 4
			MVT.methodDefIndex, MVT.end,											// 5
			MVT.byte4, MVT.byte2, MVT.byte2, MVT.stringIndex, MVT.blobIndex, MVT.paramIndex, MVT.end,	// 6
			MVT.end,																// 7
			MVT.byte2, MVT.byte2, MVT.stringIndex, MVT.end,							// 8
			MVT.typeDefIndex, MVT.typeDefOrRef, MVT.end,							// 9
			MVT.memberRefParent, MVT.stringIndex, MVT.blobIndex, MVT.end,			// 10
			MVT.byte1, MVT.byte1, MVT.hasConstant, MVT.blobIndex, MVT.end,			// 11
			MVT.hasCustomAttribute, MVT.customAttributeType, MVT.blobIndex, MVT.end,// 12
			MVT.hasFieldMarshal, MVT.blobIndex, MVT.end,							// 13
			MVT.byte2, MVT.hasDeclSecurity, MVT.blobIndex, MVT.end,					// 14
			MVT.byte2, MVT.byte4, MVT.typeDefIndex, MVT.end,						// 15
			MVT.byte4, MVT.fieldIndex, MVT.end,										// 16
			MVT.blobIndex, MVT.end,													// 17
			MVT.typeDefIndex, MVT.eventIndex, MVT.end,								// 18
			MVT.end,																// 19
			MVT.byte2, MVT.stringIndex, MVT.typeDefOrRef, MVT.end,					// 20
			MVT.typeDefIndex, MVT.propertyIndex, MVT.end,							// 21
			MVT.end,																// 22
			MVT.byte2, MVT.stringIndex, MVT.blobIndex, MVT.end,						// 23
			MVT.byte2, MVT.methodDefIndex, MVT.hasSemantics, MVT.end,				// 24
			MVT.typeDefIndex, MVT.methodDefOrRef, MVT.methodDefOrRef, MVT.end,		// 25
			MVT.stringIndex, MVT.end,												// 26
			MVT.blobIndex, MVT.end,													// 27
			MVT.byte2, MVT.memberForwarded, MVT.stringIndex, MVT.moduleRefIndex, MVT.end,	// 28
			MVT.byte4, MVT.fieldIndex, MVT.end,										// 29
			MVT.end,																// 30
			MVT.end,																// 31
			MVT.byte4, MVT.byte2, MVT.byte2, MVT.byte2, MVT.byte2, MVT.byte4, MVT.blobIndex, MVT.stringIndex, MVT.stringIndex, MVT.end,	// 32
			MVT.byte4, MVT.end,														// 33
			MVT.byte4, MVT.byte4, MVT.byte4, MVT.end,								// 34
			MVT.byte2, MVT.byte2, MVT.byte2, MVT.byte2, MVT.byte4, MVT.blobIndex, MVT.stringIndex, MVT.stringIndex, MVT.blobIndex, MVT.end,	// 35
			MVT.byte4, MVT.assemblyRefIndex, MVT.end,								// 36
			MVT.byte4, MVT.byte4, MVT.byte4, MVT.assemblyRefIndex, MVT.end,			// 37
			MVT.byte4, MVT.stringIndex, MVT.blobIndex, MVT.end,						// 38
			MVT.byte4, MVT.byte4, MVT.stringIndex, MVT.stringIndex, MVT.implementation, MVT.end,// 39
			MVT.byte4, MVT.byte4, MVT.stringIndex, MVT.implementation, MVT.end,		// 40
			MVT.typeDefIndex, MVT.typeDefIndex, MVT.end,							// 41
			MVT.byte2, MVT.byte2, MVT.typeOrMethodDef, MVT.stringIndex, MVT.end,	// 42
			MVT.end,														// 43
			MVT.genericParamIndex, MVT.typeDefOrRef, MVT.end,				// 44
			MVT.end,														// 45
			MVT.end,														// 46
			MVT.end,														// 47
			MVT.end,														// 48
			MVT.end,														// 49
			MVT.end,														// 50
			MVT.end,														// 51
			MVT.end,														// 52
			MVT.end,														// 53
			MVT.end,														// 54
			MVT.end,														// 55
			MVT.end,														// 56
			MVT.end,														// 57
			MVT.end,														// 58
			MVT.end,														// 59
			MVT.end,														// 60
			MVT.end,														// 61
			MVT.end,														// 62
			MVT.end,														// 63

			MVT.stop
		};

		void init() {
			var streamTable = metadata.getStream("#~") ?? metadata.getStream("#-");
			if (streamTable == null)
				throw new ApplicationException("Could not find #~ stream");

			seek(streamTable.Offset);
			reader.ReadUInt32();	// reserved
			reader.ReadUInt16();	// major + minor version
			heapOffsetSizes = reader.ReadByte();
			reader.ReadByte();		// always 1
			ulong validMask = reader.ReadUInt64();
			reader.ReadUInt64();	// sorted

			var numRows = new uint[64];
			for (int i = 0; validMask != 0; i++, validMask >>= 1) {
				if ((validMask & 1) != 0)
					numRows[i] = reader.ReadUInt32();
			}

			var builder = new MetadataTypeBuilder(heapOffsetSizes, numRows);
			uint fileOffset = (uint)reader.BaseStream.Position;
			for (int i = 0, j = 0; ; i++) {
				if (metadataVarType[i] == MVT.end) {
					var mdType = builder.create();
					mdType.rows = numRows[j];
					mdType.fileOffset = fileOffset;
					fileOffset += mdType.rows * mdType.totalSize;
					metadataTypes[j++] = mdType;
				}
				else if (metadataVarType[i] == MVT.stop)
					break;
				else
					builder.field(metadataVarType[i]);
			}
		}
	}
}
