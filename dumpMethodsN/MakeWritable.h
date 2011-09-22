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

#ifndef MAKE_WRITABLE_H
#define MAKE_WRITABLE_H

#include <Windows.h>

class MakeWritable {
public:
	MakeWritable(void* _addr, size_t _size) : addr(_addr), size(_size) {
		VirtualProtect(addr, size, PAGE_READWRITE, &oldProtect);
	}
	~MakeWritable() {
		VirtualProtect(addr, size, oldProtect, &oldProtect);
	}

private:
	void* addr;
	size_t size;
	DWORD oldProtect;
};

#endif
