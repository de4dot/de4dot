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


#ifndef DOTNETFILE_H
#define DOTNETFILE_H

#include <Windows.h>
#include <WinNT.h>
#include <CorHdr.h>
#include <stdio.h>
#include "strex.h"

enum MetaDataIndex {
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

struct Stream {
	Stream() : addr(0), size(0), name(0) {}
	~Stream() {
		if (name)
			free((void*)name);
	}

	void init(unsigned char* addr, size_t size, const char* name) {
		this->addr = addr;
		this->size = size;
		this->name = _strdup(name);
	}

	unsigned char* addr;
	size_t size;
	const char* name;
};

struct MetaDataTable {
	MetaDataTable() : addr(0), rows(0) {}
	unsigned char* addr;
	size_t rows;
};

struct MetaDataField {
	unsigned short offset;
	unsigned short size;
};

class MetaDataType {
public:
	MetaDataType(const MetaDataField* _fields, size_t _numFields)
		: totalSize(0), numFields(_numFields), fields(0) {
		fields = new MetaDataField[numFields];
		for (size_t i = 0; i < numFields; i++) {
			fields[i] = _fields[i];
			totalSize += fields[i].size;
		}
	}

	~MetaDataType() {
		if (fields)
			delete[] fields;
	}

	size_t size() const { return totalSize; }

	size_t read(void* addr, size_t field) const {
		if (field >= numFields)
			throw strex("Invalid field index");
		void* p = (unsigned char*)addr + fields[field].offset;
		switch (fields[field].size) {
		case 1: return *(unsigned char*)p; break;
		case 2: return *(unsigned short*)p; break;
		case 4: return *(unsigned int*)p; break;
		default: throw strex("Invalid field size");
		}
	}

private:
	MetaDataType(const MetaDataType&);
	MetaDataType& operator=(const MetaDataType&);

	size_t totalSize;
	size_t numFields;
	MetaDataField* fields;
};

class DotNetFile {
public:
	DotNetFile(const wchar_t* filename);
	~DotNetFile();

	bool isYourImageBase(void* addr) const;
	void* getMethodHeader(unsigned index, void* imageBase = 0) const;
	const char* getMethodName(unsigned index, void* imageBase = 0) const;
	void* getMethodDef(unsigned index, void* imageBase) const;
	size_t getNumMethods() const;
	const MetaDataType* getMethodType() const { return metaDataTypes[iMethodDef]; }
	const MetaDataType* getType(MetaDataIndex t) const { return metaDataTypes[t]; }
	size_t getStandAloneSigBlobIndex(unsigned index, void* imageBase) const;

private:
	static const size_t METADATA_TYPES = 64;
	DotNetFile(const DotNetFile&);
	DotNetFile& operator=(const DotNetFile&);

	void fread(long offset, void* buf, size_t size) const;
	void fread(void* buf, size_t size) const;
	void loadImage(const _IMAGE_FILE_HEADER& fileHeader, DWORD imageSize, DWORD headerSize);
	void loadImage(DWORD rva, DWORD fileOffset, DWORD sizeRawData);
	void* rvaToPtr(DWORD rva, size_t size) const;
	size_t ptrToRva(void* addr) const;
	size_t rvaToOffset(DWORD rva) const;
	void* metaOffsToAddr(DWORD offs, DWORD size) const;
	void initStreams();
	Stream* findStream(const char* name) const;
	void initMetadataTables();
	void* getMetaDataTableElem(MetaDataIndex metaIndex, unsigned index, void* imageBase) const;

	FILE* file;
	_IMAGE_SECTION_HEADER* sectionHeaders;
	size_t numSections;
	unsigned char* image;
	size_t imageSize;
	size_t sizeOfHeaders;
	IMAGE_COR20_HEADER* cor20Header;
	unsigned char* metadata;
	Stream* streams;
	size_t numStreams;
	Stream* streamTilde;
	Stream* streamBlob;
	Stream* streamStrings;
	MetaDataTable metaDataTables[METADATA_TYPES];
	MetaDataType* metaDataTypes[METADATA_TYPES];
};

#endif
