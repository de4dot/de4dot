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

#include <stdio.h>
#include "common.h"
#include "log.h"

void dumpMem(void* mem, int size, const char* msg) {
	unsigned char* p = (unsigned char*)mem;

	logvv("\nDUMP %08X (%d) - %s\n", p, size, msg);
	while (size > 0) {
		logvv("%08X", p);
		for (int j = 0; j < 16 && size > 0; j++, size--)
			logvv(" %02X", *p++);
		logvv("\n");
	}
}
