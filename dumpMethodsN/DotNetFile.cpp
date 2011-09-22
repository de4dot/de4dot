/*
    Copyright (C) 2011 de4dot@gmail.com

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

#include <string>
#include <algorithm>
#include "DotNetFile.h"
#include "strex.h"
#include "common.h"

void DotNetFile::fread(long offset, void* buf, size_t size) const {
	if (fseek(file, offset, SEEK_SET))
		throw strex("Could not seek");
	fread(buf, size);
}

void DotNetFile::fread(void* buf, size_t size) const {
	if (::fread(buf, 1, size, file) != size)
		throw strex("Could not read");
}

DotNetFile::DotNetFile(const wchar_t* filename)
	: image(0), streams(0)
{
	memset(&metaDataTypes, 0, sizeof(metaDataTypes));

	if ((file = _wfopen(filename, L"rb")) == 0)
		throw strex("Could not open file");

	_IMAGE_DOS_HEADER dosHeader;
	fread(0, &dosHeader, sizeof(dosHeader));
	if (dosHeader.e_magic != IMAGE_DOS_SIGNATURE)
		throw strex("Not a EXE file");
	DWORD peMagic;
	fread(dosHeader.e_lfanew, &peMagic, sizeof(peMagic));
	if (peMagic != IMAGE_NT_SIGNATURE)
		throw strex("Invalid PE magic");
	_IMAGE_FILE_HEADER fileHeader;
	fread(&fileHeader, sizeof(fileHeader));
	if (fileHeader.Machine != IMAGE_FILE_MACHINE_I386)
		throw strex("Machine is not i386");
	if (fileHeader.SizeOfOptionalHeader != sizeof(_IMAGE_OPTIONAL_HEADER))
		throw strex("Invalid size of optional header");
	_IMAGE_OPTIONAL_HEADER optionalHeader;
	fread(&optionalHeader, sizeof(optionalHeader));
	if (optionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC)
		throw strex("Not a 32-bit optional header");
	sizeOfHeaders = optionalHeader.SizeOfHeaders;
	loadImage(fileHeader, optionalHeader.SizeOfImage, optionalHeader.SizeOfHeaders);
	cor20Header = (IMAGE_COR20_HEADER*)rvaToPtr(optionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_COMHEADER].VirtualAddress, sizeof(IMAGE_COR20_HEADER));
	if (cor20Header->cb < sizeof(IMAGE_COR20_HEADER))
		throw strex("Invalid IMAGE_COR20_HEADER size");
	metadata = (unsigned char*)rvaToPtr(cor20Header->MetaData.VirtualAddress, cor20Header->MetaData.Size);
	if (*(DWORD*)metadata != 0x424A5342)
		throw strex("Invalid metadata magic");
	initStreams();
}

DotNetFile::~DotNetFile() {
	if (image)
		delete[] image;
	if (streams)
		delete[] streams;
	for (size_t i = 0; i < METADATA_TYPES; i++) {
		if (metaDataTypes[i])
			delete metaDataTypes[i];
	}
}

// file offset is at _IMAGE_SECTION_HEADER
void DotNetFile::loadImage(const _IMAGE_FILE_HEADER& fileHeader, DWORD _imageSize, DWORD headerSize) {
	long sectionHeaderOffs = ftell(file);

	imageSize = _imageSize;
	image = new unsigned char[imageSize];
	memset(image, 0, imageSize);

	loadImage(0, 0, headerSize);

	numSections = fileHeader.NumberOfSections;
	sectionHeaders = (_IMAGE_SECTION_HEADER*)rvaToPtr(sectionHeaderOffs, sizeof(_IMAGE_SECTION_HEADER) * numSections);
	for (size_t i = 0; i < numSections; i++) {
		if (sectionHeaders[i].VirtualAddress == 0)
			throw strex("Invalid section VirtualAddress");
		loadImage(sectionHeaders[i].VirtualAddress, sectionHeaders[i].PointerToRawData, sectionHeaders[i].SizeOfRawData);
	}
}

void DotNetFile::loadImage(DWORD rva, DWORD fileOffset, DWORD sizeRawData) {
	if (rva + sizeRawData < rva || rva + sizeRawData > imageSize)
		throw strex("rva + size too large");
	fread(fileOffset, image + rva, sizeRawData);
}

void* DotNetFile::rvaToPtr(DWORD rva, size_t size) const {
	if (rva + size < rva || rva + size > imageSize)
		throw strex("RVA + size is too big");
	return image + rva;
}

size_t DotNetFile::ptrToRva(void* addr) const {
	size_t rva = (unsigned char*)addr - image;
	if (rva < imageSize)
		return rva;
	throw strex("Addr is not a VA");
}


void DotNetFile::initStreams() {
	unsigned char* p = metadata + 4 + 2 + 2 + 4;	// magic, major, minor, reserved
	p += 4 + *(DWORD*)p;	// Skip version string and string length
	p += 2;	 // Skip Flags
	numStreams = *(WORD*)p;
	p += 2;
	streams = new Stream[numStreams];
	for (size_t i = 0; i < numStreams; i++) {
		DWORD offs = *(DWORD*)p;
		DWORD size = *(DWORD*)(p+4);
		streams[i].init((unsigned char*)metaOffsToAddr(offs, size), size, (const char*)(p+8));
		p += 8;
		size_t len = strlen((const char*)p) + 1;
		p += (len+3)/4 * 4;
	}

	streamTilde = findStream("#~");
	streamBlob = findStream("#Blob");
	streamStrings = findStream("#Strings");
	if (!streamTilde)
		throw strex("No #~ stream");
	if (!streamBlob)
		throw strex("No #Blob stream");
	if (!streamStrings)
		throw strex("No #Strings stream");

	initMetadataTables();
}

void* DotNetFile::metaOffsToAddr(DWORD offs, DWORD size) const {
	if (offs + size < offs || offs + size > cor20Header->MetaData.Size)
		throw strex("Invalid metadata offset");
	return metadata + offs;
}

Stream* DotNetFile::findStream(const char* name) const {
	for (size_t i = 0; i < numStreams; i++) {
		if (!strcmp(name, streams[i].name))
			return &streams[i];
	}
	return 0;
}

enum MetaDataVarType {
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

class MetaDataTypeBuilder {
public:
	MetaDataTypeBuilder(int _heapOffsetSizes, const MetaDataTable* _metaDataTables)
		: heapOffsetSizes(_heapOffsetSizes), metaDataTables(_metaDataTables), numFields(0), offset(0) {
	}

	MetaDataTypeBuilder& field(MetaDataVarType type) {
		if (numFields >= MAX_FIELDS)
			throw strex("Too many fields");

		unsigned short size;
		switch (type) {
		case byte1:
			size = 1;
			break;
		case byte2:
			size = 2;
			break;
		case byte4:
			size = 4;
			break;
		case stringIndex:
			size = heapOffsetSizes & 1 ? 4 : 2;
			break;
		case guidIndex:
			size = heapOffsetSizes & 2 ? 4 : 2;
			break;
		case blobIndex:
			size = heapOffsetSizes & 4 ? 4 : 2;
			break;
		case resolutionScope: {
			static MetaDataIndex indexes[] = {iModule, iModuleRef, iAssemblyRef, iTypeRef};
			size = getSize(indexes, NUM_ELEMS(indexes), 14);
			break;
		}
		case typeDefOrRef: {
			static MetaDataIndex indexes[] = {iTypeDef, iTypeRef, iTypeSpec};
			size = getSize(indexes, NUM_ELEMS(indexes), 14);
			break;
		}
		case memberRefParent: {
			static MetaDataIndex indexes[] = {iTypeDef, iTypeRef, iModuleRef, iMethodDef, iTypeSpec};
			size = getSize(indexes, NUM_ELEMS(indexes), 13);
			break;
		}
		case hasConstant: {
			static MetaDataIndex indexes[] = {iField, iParam, iProperty};
			size = getSize(indexes, NUM_ELEMS(indexes), 14);
			break;
		}
		case hasCustomAttribute: {
			static MetaDataIndex indexes[] = {
				iMethodDef, iField, iTypeRef, iTypeDef, iParam, iInterfaceImpl,
				iMemberRef, iModule /*TODO:, iPermission*/, iProperty,
				iEvent, iStandAloneSig, iModuleRef, iTypeSpec, iAssembly,
				iAssemblyRef, iFile, iExportedType, iManifestResource,
			};
			size = getSize(indexes, NUM_ELEMS(indexes), 11);
			break;
		}
		case customAttributeType: {
			static MetaDataIndex indexes[] = {iMethodDef, iMemberRef};	// others aren't used
			size = getSize(indexes, NUM_ELEMS(indexes), 13);
			break;
		}
		case hasFieldMarshal: {
			static MetaDataIndex indexes[] = {iField, iParam};
			size = getSize(indexes, NUM_ELEMS(indexes), 15);
			break;
		}
		case hasDeclSecurity: {
			static MetaDataIndex indexes[] = {iTypeDef, iMethodDef, iAssembly};
			size = getSize(indexes, NUM_ELEMS(indexes), 14);
			break;
		}
		case hasSemantics: {
			static MetaDataIndex indexes[] = {iEvent, iProperty};
			size = getSize(indexes, NUM_ELEMS(indexes), 15);
			break;
		}
		case methodDefOrRef: {
			static MetaDataIndex indexes[] = {iMethodDef, iMemberRef};
			size = getSize(indexes, NUM_ELEMS(indexes), 15);
			break;
		}
		case memberForwarded: {
			static MetaDataIndex indexes[] = {iField, iMethodDef};
			size = getSize(indexes, NUM_ELEMS(indexes), 15);
			break;
		}
		case implementation: {
			static MetaDataIndex indexes[] = {iFile, iAssemblyRef, iExportedType};
			size = getSize(indexes, NUM_ELEMS(indexes), 14);
			break;
		}
		case typeOrMethodDef: {
			static MetaDataIndex indexes[] = {iTypeDef, iMethodDef};
			size = getSize(indexes, NUM_ELEMS(indexes), 15);
			break;
		}
		case fieldIndex:
			size = getSize(iField);
			break;
		case methodDefIndex:
			size = getSize(iMethodDef);
			break;
		case paramIndex:
			size = getSize(iParam);
			break;
		case typeDefIndex:
			size = getSize(iTypeDef);
			break;
		case eventIndex:
			size = getSize(iEvent);
			break;
		case propertyIndex:
			size = getSize(iProperty);
			break;
		case moduleRefIndex:
			size = getSize(iModuleRef);
			break;
		case assemblyRefIndex:
			size = getSize(iAssemblyRef);
			break;
		case genericParamIndex:
			size = getSize(iGenericParam);
			break;
		default:
			throw strex("Unknown type");
		}

		fields[numFields].offset = offset;
		fields[numFields].size = size;
		numFields++;
		offset += size;

		return *this;
	}

	MetaDataType* create() {
		MetaDataType* mdt = new MetaDataType(fields, numFields);
		numFields = 0;
		offset = 0;
		return mdt;
	}

private:
	MetaDataTypeBuilder(const MetaDataTypeBuilder&);
	MetaDataTypeBuilder& operator=(const MetaDataTypeBuilder&);

	size_t getMaxRows(const MetaDataIndex* indexes, size_t num) const {
		size_t maxRows = 0;
		for (size_t i = 0; i < num; i++) {
			if (metaDataTables[indexes[i]].rows > maxRows)
				maxRows = metaDataTables[indexes[i]].rows;
		}
		return maxRows;
	}

	unsigned short getSize(const MetaDataIndex* indexes, size_t num, int bits) const {
		const size_t maxNum = 1 << bits;
		const size_t maxRows = getMaxRows(indexes, num);
		return maxRows <= maxNum ? 2 : 4;
	}

	unsigned short getSize(MetaDataIndex index) const {
		const MetaDataIndex indexes[1] = {index};
		return getSize(indexes, 1, 16);
	}

	static const size_t MAX_FIELDS = 10;
	const int heapOffsetSizes;
	const MetaDataTable* metaDataTables;
	size_t numFields;
	MetaDataField fields[MAX_FIELDS];
	unsigned short offset;
};

void DotNetFile::initMetadataTables() {
	unsigned char* p = streamTilde->addr;

	p += 4 + 1 + 1; // skip reserved, major, minor
	unsigned char heapOffsetSizes = *p;
	p += 1 + 1; // Skip HeapOffsetSizes and reserved
	unsigned long long valid = *(unsigned long long*)p;
	p += 8 + 8; // Skip Valid and Sorted
	for (int i = 0; valid; i++, valid >>= 1) {
		if (valid & 1) {
			metaDataTables[i].rows = *(DWORD*)p;
			p += 4;
		}
	}

	static MetaDataVarType types[] = {
		byte2, stringIndex, guidIndex, guidIndex, guidIndex, end,	// 0
		resolutionScope, stringIndex, stringIndex, end,				// 1
		byte4, stringIndex, stringIndex, typeDefOrRef, fieldIndex, methodDefIndex, end,	// 2
		end,														// 3
		byte2, stringIndex, blobIndex, end,							// 4
		end,														// 5
		byte4, byte2, byte2, stringIndex, blobIndex, paramIndex, end,	// 6
		end,														// 7
		byte2, byte2, stringIndex, end,								// 8
		typeDefIndex, typeDefOrRef, end,							// 9
		memberRefParent, stringIndex, blobIndex, end,				// 10
		byte1, byte1, hasConstant, blobIndex, end,					// 11
		hasCustomAttribute, customAttributeType, blobIndex, end,	// 12
		hasFieldMarshal, blobIndex, end,							// 13
		byte2, hasDeclSecurity, blobIndex, end,						// 14
		byte2, byte4, typeDefIndex, end,							// 15
		byte4, fieldIndex, end,										// 16
		blobIndex, end,												// 17
		typeDefIndex, eventIndex, end,								// 18
		end,														// 19
		byte2, stringIndex, typeDefOrRef, end,						// 20
		typeDefIndex, propertyIndex, end,							// 21
		end,														// 22
		byte2, stringIndex, blobIndex, end,							// 23
		byte2, methodDefIndex, hasSemantics, end,					// 24
		typeDefIndex, methodDefOrRef, methodDefOrRef, end,			// 25
		stringIndex, end,											// 26
		blobIndex, end,												// 27
		byte2, memberForwarded, stringIndex, moduleRefIndex, end,	// 28
		byte4, fieldIndex, end,										// 29
		end,														// 30
		end,														// 31
		byte4, byte2, byte2, byte2, byte2, byte4, blobIndex, stringIndex, stringIndex, end,	// 32
		byte4, end,													// 33
		byte4, byte4, byte4, end,									// 34
		byte2, byte2, byte2, byte2, byte4, blobIndex, stringIndex, stringIndex, blobIndex, end,	// 35
		byte4, assemblyRefIndex, end,								// 36
		byte4, byte4, byte4, assemblyRefIndex, end,					// 37
		byte4, stringIndex, blobIndex, end,							// 38
		byte4, byte4, stringIndex, stringIndex, implementation, end,// 39
		byte4, byte4, stringIndex, implementation, end,				// 40
		typeDefIndex, typeDefIndex, end,							// 41
		byte2, byte2, typeOrMethodDef, stringIndex, end,			// 42
		end,														// 43
		genericParamIndex, typeDefOrRef, end,						// 44
		end,														// 45
		end,														// 46
		end,														// 47
		end,														// 48
		end,														// 49
		end,														// 50
		end,														// 51
		end,														// 52
		end,														// 53
		end,														// 54
		end,														// 55
		end,														// 56
		end,														// 57
		end,														// 58
		end,														// 59
		end,														// 60
		end,														// 61
		end,														// 62
		end,														// 63

		stop
	};

	MetaDataTypeBuilder builder(heapOffsetSizes, metaDataTables);
	for (int i = 0, j = 0; ; i++) {
		if (types[i] == end)
			metaDataTypes[j++] = builder.create();
		else if (types[i] == stop)
			break;
		else
			builder.field(types[i]);
	}

	for (int i = 0; i < METADATA_TYPES; i++) {
		metaDataTables[i].addr = p;
		p += metaDataTables[i].rows * metaDataTypes[i]->size();
	}

	if (metaDataTables[iMethodDef].rows == 0)
		throw strex("No MethodDef in #~ stream");
}

bool DotNetFile::isYourImageBase(void* addr) const {
	__try {
		unsigned char* p = (unsigned char*)addr;
		size_t offs = 0;

		if (memcmp(image + offs, p + offs, sizeof(IMAGE_DOS_HEADER)))
			return false;
		offs = ((IMAGE_DOS_HEADER*)image)->e_lfanew;

		if (memcmp(image + offs, p + offs, sizeof(DWORD) + sizeof(_IMAGE_FILE_HEADER)))
			return false;
		offs += sizeof(DWORD);
		size_t sizeOh = ((_IMAGE_FILE_HEADER*)(image+offs))->SizeOfOptionalHeader;
		size_t numSections = ((_IMAGE_FILE_HEADER*)(image+offs))->NumberOfSections;
		offs += sizeof(_IMAGE_FILE_HEADER);

		// Ignore ImageBase field which could be different
		size_t lastSize = sizeOh + numSections * sizeof(_IMAGE_SECTION_HEADER);
		void* tempMem = new unsigned char[lastSize];
		memcpy(tempMem, image + offs, lastSize);
		((_IMAGE_OPTIONAL_HEADER*)tempMem)->AddressOfEntryPoint = ((_IMAGE_OPTIONAL_HEADER*)(p + offs))->AddressOfEntryPoint;
		((_IMAGE_OPTIONAL_HEADER*)tempMem)->ImageBase = ((_IMAGE_OPTIONAL_HEADER*)(p + offs))->ImageBase;
		if (memcmp(tempMem, p + offs, lastSize)) {
			delete[] tempMem;
			return false;
		}
		delete[] tempMem;
		offs += sizeOh + numSections * sizeof(_IMAGE_SECTION_HEADER);
	}
	__except (EXCEPTION_EXECUTE_HANDLER) {
		return false;
	}

	return true;
}

void* DotNetFile::getMetaDataTableElem(MetaDataIndex metaIndex, unsigned index, void* imageBase) const {
	if (imageBase == 0)
		imageBase = image;
	index--;
	if (index >= metaDataTables[metaIndex].rows)
		throw strex("Invalid metatable index");

	const MetaDataType* type = metaDataTypes[metaIndex];
	return metaDataTables[metaIndex].addr + type->size() * index;
}

void* DotNetFile::getMethodDef(unsigned index, void* imageBase) const {
	return getMetaDataTableElem(iMethodDef, index, imageBase);
}

void* DotNetFile::getMethodHeader(unsigned index, void* imageBase) const {
	void* methodDef = getMethodDef(index, imageBase);
	const MetaDataType* type = metaDataTypes[iMethodDef];
	return (unsigned char*)imageBase + type->read(methodDef, 0);
}

const char* DotNetFile::getMethodName(unsigned index, void* imageBase) const {
	void* methodDef = getMethodDef(index, imageBase);
	const MetaDataType* type = metaDataTypes[iMethodDef];
	return (const char*)imageBase + ptrToRva(streamStrings->addr + type->read(methodDef, 3));
}

size_t DotNetFile::getStandAloneSigBlobIndex(unsigned index, void* imageBase) const {
	void* elem = getMetaDataTableElem(iStandAloneSig, index, imageBase);
	const MetaDataType* type = metaDataTypes[iStandAloneSig];
	return type->read(elem, 0);
}

size_t DotNetFile::getNumMethods() const {
	return metaDataTables[iMethodDef].rows;
}

size_t DotNetFile::rvaToOffset(DWORD rva) const {
	if (rva < sizeOfHeaders)
		return rva;

	for (size_t i = 0; i < numSections; i++) {
		if (rva >= sectionHeaders[i].VirtualAddress && rva < sectionHeaders[i].VirtualAddress + sectionHeaders[i].SizeOfRawData)
			return rva - sectionHeaders[i].VirtualAddress + sectionHeaders[i].PointerToRawData;
	}

	throw strex("RVA is not part of file");
}
