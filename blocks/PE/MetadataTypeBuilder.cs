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

namespace de4dot.PE {
	enum MetadataVarType {
		end,
		stop,
		byte1,
		byte2,
		byte4,
		stringIndex,		// index into #String heap
		guidIndex,			// index into #GUID heap
		blobIndex,			// index into #Blob heap
		resolutionScope,
		typeDefOrRef,
		fieldIndex,
		methodDefIndex,
		paramIndex,
		typeDefIndex,
		eventIndex,
		propertyIndex,
		moduleRefIndex,
		assemblyRefIndex,
		genericParamIndex,
		memberRefParent,
		hasConstant,
		hasCustomAttribute,
		customAttributeType,
		hasFieldMarshal,
		hasDeclSecurity,
		hasSemantics,
		methodDefOrRef,
		memberForwarded,
		implementation,
		typeOrMethodDef,
	};

	class MetadataTypeBuilder {
		byte heapOffsetSizes;
		uint[] numRows;
		List<MetadataField> fields;
		int offset;

		public MetadataTypeBuilder(byte heapOffsetSizes, uint[] numRows) {
			this.heapOffsetSizes = heapOffsetSizes;
			this.numRows = numRows;
			reset();
		}

		void reset() {
			offset = 0;
			fields = new List<MetadataField>();
		}

		public MetadataType create() {
			var type = new MetadataType(fields);
			reset();
			return type;
		}

		public void field(MetadataVarType type) {
			int size;
			switch (type) {
			case MetadataVarType.byte1:
				size = 1;
				break;
			case MetadataVarType.byte2:
				size = 2;
				break;
			case MetadataVarType.byte4:
				size = 4;
				break;
			case MetadataVarType.stringIndex:
				size = (heapOffsetSizes & 1) != 0 ? 4 : 2;
				break;
			case MetadataVarType.guidIndex:
				size = (heapOffsetSizes & 2) != 0 ? 4 : 2;
				break;
			case MetadataVarType.blobIndex:
				size = (heapOffsetSizes & 4) != 0 ? 4 : 2;
				break;
			case MetadataVarType.resolutionScope:
				size = getSize(14, new MetadataIndex[] { MetadataIndex.iModule, MetadataIndex.iModuleRef, MetadataIndex.iAssemblyRef, MetadataIndex.iTypeRef });
				break;
			case MetadataVarType.typeDefOrRef:
				size = getSize(14, new MetadataIndex[] { MetadataIndex.iTypeDef, MetadataIndex.iTypeRef, MetadataIndex.iTypeSpec });
				break;
			case MetadataVarType.memberRefParent:
				size = getSize(13, new MetadataIndex[] { MetadataIndex.iTypeDef, MetadataIndex.iTypeRef, MetadataIndex.iModuleRef, MetadataIndex.iMethodDef, MetadataIndex.iTypeSpec });
				break;
			case MetadataVarType.hasConstant:
				size = getSize(14, new MetadataIndex[] { MetadataIndex.iField, MetadataIndex.iParam, MetadataIndex.iProperty });
				break;
			case MetadataVarType.hasCustomAttribute:
				size = getSize(11, new MetadataIndex[] {
					MetadataIndex.iMethodDef, MetadataIndex.iField, MetadataIndex.iTypeRef,
					MetadataIndex.iTypeDef, MetadataIndex.iParam, MetadataIndex.iInterfaceImpl,
					MetadataIndex.iMemberRef, MetadataIndex.iModule /*TODO:, MetadataIndex.iPermission*/,
					MetadataIndex.iProperty, MetadataIndex.iEvent, MetadataIndex.iStandAloneSig,
					MetadataIndex.iModuleRef, MetadataIndex.iTypeSpec, MetadataIndex.iAssembly,
					MetadataIndex.iAssemblyRef, MetadataIndex.iFile, MetadataIndex.iExportedType,
					MetadataIndex.iManifestResource,
				});
				break;
			case MetadataVarType.customAttributeType:
				size = getSize(13, new MetadataIndex[] { MetadataIndex.iMethodDef, MetadataIndex.iMemberRef });	// others aren't used
				break;
			case MetadataVarType.hasFieldMarshal:
				size = getSize(15, new MetadataIndex[] { MetadataIndex.iField, MetadataIndex.iParam });
				break;
			case MetadataVarType.hasDeclSecurity:
				size = getSize(14, new MetadataIndex[] { MetadataIndex.iTypeDef, MetadataIndex.iMethodDef, MetadataIndex.iAssembly });
				break;
			case MetadataVarType.hasSemantics:
				size = getSize(15, new MetadataIndex[] { MetadataIndex.iEvent, MetadataIndex.iProperty });
				break;
			case MetadataVarType.methodDefOrRef:
				size = getSize(15, new MetadataIndex[] { MetadataIndex.iMethodDef, MetadataIndex.iMemberRef });
				break;
			case MetadataVarType.memberForwarded:
				size = getSize(15, new MetadataIndex[] { MetadataIndex.iField, MetadataIndex.iMethodDef });
				break;
			case MetadataVarType.implementation:
				size = getSize(14, new MetadataIndex[] { MetadataIndex.iFile, MetadataIndex.iAssemblyRef, MetadataIndex.iExportedType });
				break;
			case MetadataVarType.typeOrMethodDef:
				size = getSize(15, new MetadataIndex[] { MetadataIndex.iTypeDef, MetadataIndex.iMethodDef });
				break;
			case MetadataVarType.fieldIndex:
				size = getSize(MetadataIndex.iField);
				break;
			case MetadataVarType.methodDefIndex:
				size = getSize(MetadataIndex.iMethodDef);
				break;
			case MetadataVarType.paramIndex:
				size = getSize(MetadataIndex.iParam);
				break;
			case MetadataVarType.typeDefIndex:
				size = getSize(MetadataIndex.iTypeDef);
				break;
			case MetadataVarType.eventIndex:
				size = getSize(MetadataIndex.iEvent);
				break;
			case MetadataVarType.propertyIndex:
				size = getSize(MetadataIndex.iProperty);
				break;
			case MetadataVarType.moduleRefIndex:
				size = getSize(MetadataIndex.iModuleRef);
				break;
			case MetadataVarType.assemblyRefIndex:
				size = getSize(MetadataIndex.iAssemblyRef);
				break;
			case MetadataVarType.genericParamIndex:
				size = getSize(MetadataIndex.iGenericParam);
				break;
			default:
				throw new ApplicationException("Unknown type");
			}

			var field = new MetadataField();
			field.offset = offset;
			field.size = size;
			fields.Add(field);
			offset += size;
		}

		uint getMaxRows(MetadataIndex[] indexes) {
			uint maxRows = 0;
			for (int i = 0; i < indexes.Length; i++)
				maxRows = Math.Max(maxRows, numRows[(int)indexes[i]]);
			return maxRows;
		}

		int getSize(int bits, MetadataIndex[] indexes) {
			uint maxNum = 1U << bits;
			uint maxRows = getMaxRows(indexes);
			return maxRows <= maxNum ? 2 : 4;
		}

		int getSize(MetadataIndex index) {
			return getSize(16, new MetadataIndex[] { index });
		}
	}
}
