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

using System.Collections.Generic;

namespace de4dot.PE {
	public enum MetadataIndex {
		iModule = 0,
		iTypeRef = 1,
		iTypeDef = 2,
		iField = 4,
		iMethodDef = 6,
		iParam = 8,
		iInterfaceImpl = 9,
		iMemberRef = 10,
		iConstant = 11,
		iCustomAttribute = 12,
		iFieldMarshal = 13,
		iDeclSecurity = 14,
		iClassLayout = 15,
		iFieldLayout = 16,
		iStandAloneSig = 17,
		iEventMap = 18,
		iEvent = 20,
		iPropertyMap = 21,
		iProperty = 23,
		iMethodSemantics = 24,
		iMethodImpl = 25,
		iModuleRef = 26,
		iTypeSpec = 27,
		iImplMap = 28,
		iFieldRVA = 29,
		iAssembly = 32,
		iAssemblyProcessor = 33,
		iAssemblyOS = 34,
		iAssemblyRef = 35,
		iAssemblyRefProcessor = 36,
		iAssemblyRefOS = 37,
		iFile = 38,
		iExportedType = 39,
		iManifestResource = 40,
		iNestedClass = 41,
		iGenericParam = 42,
		iGenericParamConstraint = 44,
	};

	public class MetadataType {
		public uint fileOffset;
		public uint rows;
		public uint totalSize;
		public List<MetadataField> fields;

		public MetadataType(List<MetadataField> fields) {
			this.fields = fields;
			totalSize = 0;
			foreach (var field in fields)
				totalSize += (uint)field.size;
		}

		public override string ToString() {
			return string.Format("MDType: {0:X8}, {1} rows, {2} bytes, {3} fields", fileOffset, rows, totalSize, fields.Count);
		}
	}

	public struct MetadataField {
		public int offset;
		public int size;

		public override string ToString() {
			return string.Format("offset: {0}, size {1}", offset, size);
		}
	}
}
