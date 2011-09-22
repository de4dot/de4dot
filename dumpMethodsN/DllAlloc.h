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

#ifndef DLL_ALLOC_H
#define DLL_ALLOC_H

#include <Windows.h>
#include "strex.h"
#include "common.h"

// Alloc code/data from the end of sections in a loaded DLL
class DllAlloc {
public:
	DllAlloc(HMODULE hMod) {
		hDll = hMod;
		if (!hDll)
			throw strex("Invalid hDll");
		imageBase = (unsigned char*)hDll;

		idh = (IMAGE_DOS_HEADER*)imageBase;
		unsigned char* p = imageBase + idh->e_lfanew;
		p += 4;		// Skip IMAGE_NT_SIGNATURE
		ifh = (_IMAGE_FILE_HEADER*)p;
		numSections = ifh->NumberOfSections;
		p += sizeof(_IMAGE_FILE_HEADER);
		ioh = (_IMAGE_OPTIONAL_HEADER*)p;
		p += ifh->SizeOfOptionalHeader;
		sections = (_IMAGE_SECTION_HEADER*)p;

		textEnd = findEndOfSection(".text\0\0\0");
	}

	void* alloc(size_t size) {
		textEnd -= ALIGNIT(size, sizeof(void*));
		return textEnd;
	}

private:
	static const size_t PAGE_SIZE = 0x1000;

	void* rvaToPtr(unsigned rva) const {
		return imageBase + rva;
	}

	_IMAGE_SECTION_HEADER* findSection(const char* name) const {
		for (size_t i = 0; i < numSections; i++) {
			if (!memcmp(sections[i].Name, name, 8))
				return &sections[i];
		}
		return 0;
	}

	unsigned char* findEndOfSection(const char* name) const {
		_IMAGE_SECTION_HEADER* section = findSection(name);
		if (!section)
			throw strex("Could not find the section");

		return (unsigned char*)rvaToPtr(ALIGNIT(section->VirtualAddress + section->SizeOfRawData, PAGE_SIZE));
	}

	HMODULE hDll;
	unsigned char* imageBase;
	size_t numSections;
	IMAGE_DOS_HEADER* idh;
	_IMAGE_FILE_HEADER* ifh;
	_IMAGE_OPTIONAL_HEADER* ioh;
	_IMAGE_SECTION_HEADER* sections;
	unsigned char* textEnd;
};

#endif
